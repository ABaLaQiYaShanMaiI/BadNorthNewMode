using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>左键从"非原生单位附近"起拖 = 框选（框选期间相机拖拽让位）；别处左键拖动仍由游戏平移相机。见 PROJECT_SPEC §4/§5。</summary>
    internal static class MarqueeSelect
    {
        static bool _held;
        static bool _grab;                 // 本次按下是否落在非原生单位附近（是 → 拖动即框选）
        static bool _dragging;
        static Vector2 _start;             // 屏幕坐标（原点左下，与 Input.mousePosition 一致）
        static Rect _screenRect;
        static CursorManager.IDragListener _detached;
        static CameraController _camera;
        static Camera _cam;
        static List<ForeignUnit> _pending;      // 已选中/框选、等待"点地块"成队的目标
        static Vector2 _pendingCenter;
        static ForeignUnit _pressUnit;          // 按下时指针下的非原生单位（单击选择用）
        static bool _slowMo;                    // 是否已向 TimeManager 申请减速
        static readonly object SlowMoOwner = new object();   // TimeManager 按 requester 记账（取最小值合并）

        internal static bool Dragging { get { return _dragging; } }
        internal static List<ForeignUnit> Pending { get { return _pending; } }
        internal static bool HasPending { get { return _pending != null && _pending.Count > 0; } }

        /// <summary>原版此刻是否正选中着"我方小队"（是 → 点击归原版，我们不接管）。</summary>
        internal static bool VanillaSelected { get { return !object.ReferenceEquals(VanillaSelection(), null); } }

        static EnglishSquad VanillaSelection()
        {
            SquadSelector ss = Singleton<SquadSelector>.instance;
            return (ss != null) ? ss.selectedSquad : null;
        }

        /// <summary>取消原版"我方小队"的选择（与原版 `SquadMover.MoveTo` 末尾同一句，见 §4）。</summary>
        static void ClearVanillaSelection()
        {
            SquadSelector ss = Singleton<SquadSelector>.instance;
            if (ss != null && !object.ReferenceEquals(ss.selectedSquad, null)) ss.SelectSquad(null, false);
        }

        /// <summary>取走待成队的框选结果（点地块时调用；取走即清空）。</summary>
        internal static List<ForeignUnit> TakePending(out Vector2 center)
        {
            List<ForeignUnit> p = _pending;
            center = _pendingCenter;
            _pending = null;
            return p;
        }

        internal static void Tick(IslandGameplayManager gm)
        {
            if (!Util.V(ModConfig.RemoteControl, true)) { Cancel(gm); return; }
            if (IngameMenu.IsOpen) { Cancel(gm); return; }

            string why;
            if (!Plugin.InBattle(gm, out why)) { Cancel(gm); return; }

            if (SelectAllKeyDown()) SelectAll();          // R = 全选所有可选的非原生单位

            // 互斥：你选了我方小队 → 我们的选择让位（这次点击完全归原版，绝不双控）
            if (VanillaSelected && HasPending)
            {
                ClearPending();
                if (Util.V(ModConfig.VerboseLog, false)) Util.Log("[NewMode][遥控] 你选中了我方小队 → 已清空遥控选择");
            }

            if (Input.GetMouseButtonDown(0))
            {
                _held = true;
                _dragging = false;
                _start = Input.mousePosition;
                _pressUnit = NearestForeignUnitWorld(_start);                 // 主路径：世界距离（与投放同一套 NavSpotCast）
                if (_pressUnit == null) _pressUnit = NearestForeignUnit(_start);   // 兜底：屏幕半径
                _grab = FreeMarqueeKeyHeld() || (_pressUnit != null);   // ① 按住 FreeMarqueeKey ② 从单位上起拖
                if (_grab) DetachCamera(gm);     // 按下的瞬间就接管相机：这一次拖动不平移，避免"先平移一点再被接管"
            }

            if (_held && Input.GetMouseButton(0))
            {
                Vector2 now = Input.mousePosition;
                if (_grab && !_dragging && (now - _start).magnitude >= Util.V(ModConfig.RemoteMarqueePixels, 8))
                {
                    _dragging = true;
                    if (Util.V(ModConfig.VerboseLog, false)) Util.Log("[NewMode][遥控] 开始框选");
                }
                if (_dragging) _screenRect = RectFrom(_start, now);
            }

            if (_held && (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)))   // 也兜底"按住时切窗口丢 Up 事件"
            {
                bool wasDrag = _dragging;
                ForeignUnit pressUnit = _pressUnit;
                _held = false;
                _dragging = false;
                _grab = false;
                _pressUnit = null;
                AttachCamera(gm);

                if (wasDrag) ApplySelection();                              // 拖动 = 框选（可选）
                else if (pressUnit != null && pressUnit.agent != null && ShiftHeld())
                    ClickSelectSquad(pressUnit);                            // Shift + 点单位 = 选中它所在的整队（v1.4.7）
            }

            UpdateSlowMo(_dragging || HasPending);             // 框选中 / 已有选中 → 减速（更易框住移动中的敌人）
        }

        /// <summary>Shift + 点击：选中该单位所在的**整队**（同一次投送的 squad，通常一船 4 个）；再点同一队 = 取消。</summary>
        static void ClickSelectSquad(ForeignUnit unit)
        {
            List<ForeignUnit> squad = SquadOf(unit);
            if (squad.Count == 0) return;

            ClearVanillaSelection();                     // 选中遥控单位前，取消我方小队的选择（保持"当前只选中一方"）

            if (_pending == null) _pending = new List<ForeignUnit>();
            _pendingCenter = Input.mousePosition;

            bool all = true;
            for (int i = 0; i < squad.Count; i++)
                if (!_pending.Contains(squad[i])) { all = false; break; }

            if (all)
            {
                for (int i = 0; i < squad.Count; i++) _pending.Remove(squad[i]);
            }
            else
            {
                for (int i = 0; i < squad.Count; i++)
                    if (!_pending.Contains(squad[i])) _pending.Add(squad[i]);
            }

            string msg = (_pending.Count == 0)
                ? "已清空选择"
                : string.Format("已选中 {0}（再 Shift 点同一队 = 取消；左/右键点地块 = 集结前进）", DescribePending());
            IngameMenu.Say(msg);
            Util.Log("[NewMode][遥控] " + msg);
        }

        /// <summary>取该单位所在**引擎小队**（同一次投送的 squad）里所有"可选"的非原生单位。</summary>
        static List<ForeignUnit> SquadOf(ForeignUnit unit)
        {
            List<ForeignUnit> res = new List<ForeignUnit>();
            EnsureCandidates();

            Agent a = (unit != null) ? unit.agent : null;
            Squad squad = (a != null) ? a.squad : null;
            if (squad != null && squad.agents != null)
            {
                for (int i = 0; i < squad.agents.Count; i++)
                {
                    Agent m = squad.agents[i];
                    if (!Usable(m)) continue;

                    ForeignUnit f = m.GetComponent<ForeignUnit>();
                    if (f != null && !res.Contains(f)) res.Add(f);
                }
            }

            if (res.Count == 0 && unit != null) res.Add(unit);
            return res;
        }

        /// <summary>下达移动后清空"已选中"（原版 `SquadMover.MoveTo` 也是移动后取消选择），并恢复时间流速。</summary>
        internal static void ClearPending()
        {
            if (_pending != null) _pending.Clear();
            UpdateSlowMo(false);
        }

        /// <summary>框选：默认替换选择，按住 Shift 则并入现有选择。</summary>
        static void ApplySelection()
        {
            List<ForeignUnit> picked = Pick(_screenRect);

            // 屏幕投影不可信时（相机异常等）用"世界四边形"兜底：矩形四角 NavSpotCast 成世界点，再判单位是否落在里面
            if (picked.Count == 0)
            {
                EnsureCameraForRect(_screenRect);
                picked = PickWorld(_screenRect);
            }

            // 细长/退化的拖动（几乎一条线）→ 按"点一下"处理：取矩形中心附近最近的单位
            if (picked.Count == 0 && (_screenRect.width < 8f || _screenRect.height < 8f))
            {
                ForeignUnit only = NearestForeignUnitWorld(_screenRect.center);
                if (only != null) picked.Add(only);
            }
            if (_pending == null) _pending = new List<ForeignUnit>();
            if (!ShiftHeld()) _pending.Clear();
            for (int i = 0; i < picked.Count; i++)
                if (!_pending.Contains(picked[i])) _pending.Add(picked[i]);
            if (_pending.Count > 0) ClearVanillaSelection();   // 框选也算"选中了遥控单位" → 取消我方选择

            _pendingCenter = _screenRect.center;
            string msg = (_pending.Count == 0)
                ? "框里没有非原生单位"
                : string.Format("已选中 {0} 个非原生单位（左键点地块 = 成队并前进）", _pending.Count);
            IngameMenu.Say(msg);
            Util.Log(string.Format("[NewMode][遥控] {0}（矩形 {1:F0}×{2:F0}；登记 {3}，可用 {4}，命中 {5}；最近 {6}）",
                msg, _screenRect.width, _screenRect.height, ForeignUnit.All.Count, ForeignUnit.UsableCount(), picked.Count,
                NearestScreenInfo(_pendingCenter)));
        }

        /// <summary>把待成队列表按兵种写成一串（HUD 提示用）。</summary>
        static string DescribePending()
        {
            if (_pending == null || _pending.Count == 0) return "无";

            List<string> types = new List<string>();
            List<int> counts = new List<int>();
            for (int i = 0; i < _pending.Count; i++)
            {
                ForeignUnit f = _pending[i];
                if (f == null || f.agent == null) continue;

                int idx = types.IndexOf(f.unitType);
                if (idx < 0) { types.Add(f.unitType); counts.Add(1); }
                else counts[idx] = counts[idx] + 1;
            }

            string s = null;
            for (int i = 0; i < types.Count; i++)
            {
                s = (s == null) ? "" : (s + "、");
                s += UnitNames.Of(types[i]) + "×" + counts[i];
            }
            return (s == null) ? "无" : s;
        }

        /// <summary>用矩形中心的地面点校正相机（拿不到地面点就保持原样）。</summary>
        static void EnsureCameraForRect(Rect r)
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (gm != null) ? gm.island : null;
            if (island == null) return;

            Vector3 land;
            string diag;
            if (Plugin.TryGetLandPoint(island, r.center, out land, out diag)) CamFor(land, r.center);
        }

        /// <summary>世界四边形兜底框选：用 NavSpotCast 把矩形四角变成世界点，判单位脚点是否在四边形内（完全不依赖屏幕投影）。</summary>
        static List<ForeignUnit> PickWorld(Rect r)
        {
            List<ForeignUnit> res = new List<ForeignUnit>();
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (gm != null) ? gm.island : null;
            if (island == null) return res;

            Vector3 c0, c1, c2, c3;
            string diag;
            if (!Plugin.TryGetLandPoint(island, new Vector2(r.xMin, r.yMin), out c0, out diag)) return res;
            if (!Plugin.TryGetLandPoint(island, new Vector2(r.xMax, r.yMin), out c1, out diag)) return res;
            if (!Plugin.TryGetLandPoint(island, new Vector2(r.xMax, r.yMax), out c2, out diag)) return res;
            if (!Plugin.TryGetLandPoint(island, new Vector2(r.xMin, r.yMax), out c3, out diag)) return res;

            EnsureCandidates();
            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 p = a.wPos;
                if (InQuadXZ(p, c0, c1, c2, c3)) res.Add(f);
            }
            return res;
        }

        /// <summary>XZ 平面上的凸四边形包含判定（四角按矩形顺/逆时针皆可，取叉积同号）。</summary>
        static bool InQuadXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float s0 = Cross(a, b, p);
            float s1 = Cross(b, c, p);
            float s2 = Cross(c, d, p);
            float s3 = Cross(d, a, p);

            bool allNeg = (s0 <= 0f && s1 <= 0f && s2 <= 0f && s3 <= 0f);
            bool allPos = (s0 >= 0f && s1 >= 0f && s2 >= 0f && s3 >= 0f);
            return allNeg || allPos;
        }

        static float Cross(Vector3 a, Vector3 b, Vector3 p)
        {
            return (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
        }

        static List<ForeignUnit> Pick(Rect screenRect)
        {
            EnsureCandidates();
            List<ForeignUnit> res = new List<ForeignUnit>();

            Camera cam = Cam();
            if (cam == null) return res;

            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
                if (sp.z <= 0f) continue;                                    // 相机背后
                if (screenRect.Contains(new Vector2(sp.x, sp.y))) res.Add(f);
            }
            return res;
        }

        /// <summary>
        /// 候选来源：标记注册表 ∪ 我们登记过的 squad 成员（**自愈**：在册单位缺标记就补上）。
        /// 这样即使 `AttachAgentBehaviours` 没跑到（旧存档/异常路径），也照样能选中。
        /// </summary>
        static void EnsureCandidates()
        {
            ForeignUnit.Prune();

            List<Agent> ours = new List<Agent>();
            SpawnLedger.CollectOurAgents(ours);
            for (int i = 0; i < ours.Count; i++)
            {
                Agent a = ours[i];
                if (a == null || a.GetComponent<ForeignUnit>() != null) continue;

                VikingAgent va = a.GetComponent<VikingAgent>();
                ForeignUnit.Attach(a, (va != null && va.vikingReference != null) ? va.vikingReference.name : a.name);
            }
        }

        /// <summary>按下点附近最近的非原生单位（决定"单击选择"的目标；也决定拖动是否算框选）。</summary>
        static ForeignUnit NearestForeignUnit(Vector2 screenPos)
        {
            EnsureCandidates();
            Camera cam = Cam();
            if (cam == null) return null;

            int radius = Util.V(ModConfig.RemoteGrabRadius, 64);
            float r2 = (radius <= 0) ? float.MaxValue : (float)radius * radius;

            ForeignUnit best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
                if (sp.z <= 0f) continue;

                float dx = sp.x - screenPos.x;
                float dy = sp.y - screenPos.y;
                float d = dx * dx + dy * dy;
                if (d > r2 || d >= bestD) continue;

                bestD = d;
                best = f;
            }
            return best;
        }

        /// <summary>点选（世界距离版，主路径）：用投放同一套 NavSpotCast 取世界点，再比单位世界距离——不依赖屏幕投影。</summary>
        static ForeignUnit NearestForeignUnitWorld(Vector2 screenPos)
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (gm != null) ? gm.island : null;

            Vector3 land;
            string diag;
            if (!Plugin.TryGetLandPoint(island, screenPos, out land, out diag)) return null;

            CamFor(land, screenPos);                     // 用"地面点 ↔ 点击处"校正相机（框选/标记也受益）

            EnsureCandidates();
            float radius = Util.V(ModConfig.RemoteClickRadius, 1.2f);
            float best = radius;
            ForeignUnit pick = null;

            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 p = a.wPos;
                p.y = land.y;
                float d = Vector3.Distance(p, land);
                if (d < best) { best = d; pick = f; }
            }
            return pick;
        }

        static bool SelectAllKeyDown()
        {
            return KeyDown(ModConfig.RemoteSelectAllKey);
        }

        /// <summary>我们自己的快捷键是否按下（None = 关闭；cfg 为空也不炸）。</summary>
        static bool KeyDown(ConfigEntry<KeyCode> key)
        {
            return key != null && key.Value != KeyCode.None && Input.GetKeyDown(key.Value);
        }

        /// <summary>一键全选：把所有"可选"的非原生单位加入待成队选择（等价于 Shift 全框）。</summary>
        internal static void SelectAll()
        {
            EnsureCandidates();
            ClearVanillaSelection();                     // 与 Shift 点选一致：选中遥控单位前先取消我方选择
            if (_pending == null) _pending = new List<ForeignUnit>();
            _pending.Clear();

            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (Usable(a)) _pending.Add(f);
            }

            string msg = (_pending.Count == 0)
                ? "可选的非原生单位为 0（可能都还在船上或已阵亡）"
                : string.Format("已全选 {0}（左键点地块 = 成队前进）", DescribePending());
            IngameMenu.Say(msg);
            Util.Log("[NewMode][遥控] " + msg);
        }

        /// <summary>最近可用单位的屏幕信息（距离/原始坐标/z/鼠标/屏幕尺寸）——用来判定投影是否可信。</summary>
        static string NearestScreenInfo(Vector2 screenPos)
        {
            Camera cam = Cam();
            if (cam == null) return "相机不可用";

            float best = float.MaxValue;
            string info = "无（没有可用的非原生单位）";
            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
                float dx = sp.x - screenPos.x;
                float dy = sp.y - screenPos.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d >= best) continue;

                best = d;
                info = string.Format("{0:F0}px（屏 {1:F0},{2:F0} z={3:F1}｜鼠标 {4:F0},{5:F0}｜屏幕 {6}×{7}｜相机 {8}）",
                    d, sp.x, sp.y, sp.z, screenPos.x, screenPos.y, Screen.width, Screen.height, cam.name);
            }
            return info;
        }

        /// <summary>按住"自由框选键"→ 从任意位置起拖都算框选（相机交给框选）。左右 Alt 都认。</summary>
        static bool FreeMarqueeKeyHeld()
        {
            ConfigEntry<KeyCode> key = ModConfig.RemoteFreeMarqueeKey;
            if (key == null || key.Value == KeyCode.None) return false;
            if (Input.GetKey(key.Value)) return true;

            return (key.Value == KeyCode.LeftAlt || key.Value == KeyCode.RightAlt) &&
                   (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
        }

        static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        internal static float ScreenDist2(Agent a, Vector2 center)
        {
            Camera cam = Cam();
            if (cam == null || a == null) return float.MaxValue;

            Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
            if (sp.z <= 0f) return float.MaxValue;

            float dx = sp.x - center.x;
            float dy = sp.y - center.y;
            return dx * dx + dy * dy;
        }

        /// <summary>可被框选/标记：还活着、已生成、且已踩在岛上（下船完成）。</summary>
        static bool Usable(Agent a)
        {
            return a != null && a.spawned.active && a.aliveState.active && a.navPos.island;
        }

        static void Cancel(IslandGameplayManager gm)
        {
            _pressUnit = null;
            if (_pending != null && _pending.Count > 0) { _pending.Clear(); }   // 离开战局/开菜单时清掉待成队
            ClearSlowMo();
            if (!_held && !_dragging) return;
            _held = false;
            _grab = false;
            _dragging = false;
            AttachCamera(gm);
        }

        /// <summary>
        /// 框选中或已有选中 → 向 TimeManager 申请减速（原版"选中我方小队"用的是 0.1，同一套 API、按 requester 取最小值合并）。
        /// 敌方单位一直在动，减速后更容易框住（见 PROJECT_SPEC §5）。
        /// </summary>
        static void UpdateSlowMo(bool want)
        {
            bool on = want && Util.V(ModConfig.RemoteSlowMo, true);
            if (on == _slowMo) return;

            _slowMo = on;
            if (on)
            {
                TimeManager.RequestTimeScale(SlowMoOwner, Util.V(ModConfig.RemoteSlowMoScale, 0.1f));
                if (Util.V(ModConfig.VerboseLog, false)) Util.Log("[NewMode][遥控] 减速中（框选/已选中）");
            }
            else
            {
                TimeManager.RemoveTimeScale(SlowMoOwner);
            }
        }

        /// <summary>释放减速（菜单打开 / 离开战局 / 清场 / 插件卸载时调用，避免残留把全局时间卡住）。</summary>
        internal static void ClearSlowMo()
        {
            if (!_slowMo) return;
            _slowMo = false;
            TimeManager.RemoveTimeScale(SlowMoOwner);
        }

        /// <summary>框选期间把自己压到拖拽栈顶，等价于"让相机这一会儿不听拖拽"（见 §4 坑表）。</summary>
        static void DetachCamera(IslandGameplayManager gm)
        {
            if (_detached != null || gm == null || gm.cursorManager == null) return;

            CameraController cam = FindCameraController(gm);
            if (cam == null) return;

            CursorManager.IDragListener l = cam;
            if (gm.cursorManager.Contains(l))
            {
                gm.cursorManager.Remove(l);
                _detached = l;
            }
        }

        static void AttachCamera(IslandGameplayManager gm)
        {
            if (_detached == null) return;

            CursorManager cm = (gm != null) ? gm.cursorManager : null;
            if (cm != null && !cm.Contains(_detached)) cm.Add(_detached);
            _detached = null;
        }

        static CameraController FindCameraController(IslandGameplayManager gm)
        {
            if (_camera != null) return _camera;
            if (gm != null) _camera = gm.GetComponentInChildren<CameraController>(true);
            if (_camera == null) _camera = UnityEngine.Object.FindObjectOfType<CameraController>();
            return _camera;
        }

        static Camera Cam()
        {
            if (_cam != null) return _cam;                 // 优先用"自验证"过的相机（见 CamFor）
            LevelCamera lc = Singleton<LevelCamera>.instance;
            _cam = (lc != null && lc.cameraRef != null) ? lc.cameraRef : Camera.main;
            return _cam;
        }

        /// <summary>
        /// 用"已知世界点 ↔ 已知屏幕点"验证并挑相机：`LevelCamera.cameraRef` 在战局里可能指向 CampaignCamera（实测会把单位投到屏幕外），
        /// 所以拿 NavSpotCast 给出的地面点来测误差，谁准用谁（见 PROJECT_SPEC §4 坑表）。
        /// </summary>
        static Camera CamFor(Vector3 world, Vector2 screen)
        {
            Camera best = null;
            float bestErr = float.MaxValue;

            LevelCamera lc = Singleton<LevelCamera>.instance;
            if (lc != null) ScoreCamera(lc.cameraRef, world, screen, ref best, ref bestErr);
            ScoreCamera(Camera.main, world, screen, ref best, ref bestErr);

            if (bestErr > 32f)                              // 前两个都不准 → 遍历场景里的相机
            {
                Camera[] all = Camera.allCameras;
                for (int i = 0; i < all.Length; i++) ScoreCamera(all[i], world, screen, ref best, ref bestErr);
            }

            if (best != null)
            {
                if (!object.ReferenceEquals(best, _cam) && Util.V(ModConfig.VerboseLog, false))
                {
                    Util.Log(string.Format("[NewMode][遥控] 改用相机 {0}（与已知点误差 {1:F0}px；原 {2}）",
                        best.name, bestErr, (_cam != null) ? _cam.name : "无"));
                }
                _cam = best;
            }
            return best;
        }

        static void ScoreCamera(Camera c, Vector3 world, Vector2 screen, ref Camera best, ref float bestErr)
        {
            if (c == null) return;

            Vector3 sp = c.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            float dx = sp.x - screen.x;
            float dy = sp.y - screen.y;
            float err = Mathf.Sqrt(dx * dx + dy * dy);
            if (err < bestErr) { bestErr = err; best = c; }
        }

        static Rect RectFrom(Vector2 a, Vector2 b)
        {
            float x = Mathf.Min(a.x, b.x);
            float y = Mathf.Min(a.y, b.y);
            return new Rect(x, y, Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        static Rect ToGui(Rect r)
        {
            return new Rect(r.x, Screen.height - r.y - r.height, r.width, r.height);   // IMGUI 的 y 轴翻转
        }

        /// <summary>框选矩形 + 受控单位 / 目标点标记（由 Plugin.OnGUI 调用）。</summary>
        internal static void DrawOverlay()
        {
            if (!Util.V(ModConfig.RemoteHighlight, true)) return;

            if (_dragging)
            {
                Rect r = ToGui(_screenRect);
                GUI.color = new Color(0.4f, 1f, 1f, 0.12f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(0.4f, 1f, 1f, 0.9f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            Camera cam = Cam();
            if (cam == null) return;

            if (_pending != null && _pending.Count > 0)                  // 待成队/已选中：亮青十字
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    ForeignUnit f = _pending[i];
                    Agent a = (f != null) ? f.agent : null;
                    if (!Usable(a)) continue;
                    DrawCross(cam, a.wPos, new Color(0.4f, 1f, 1f, 0.9f), 9f);
                }
            }

            if (RemoteGroup.Any || HasPending) DrawHoverCursor(cam);      // 原版同款：选中后鼠标下的地块亮起

            if (!RemoteGroup.Any) return;

            for (int gi = 0; gi < RemoteGroup.GroupCount; gi++)
            {
                RemoteGroup.Group g = RemoteGroup.At(gi);
                if (g == null) continue;

                if (g.target != null)
                    DrawRing(cam, g.target.navPos.wPos,
                        g.selected ? new Color(0.4f, 1f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.25f), 20f);

                Color color = g.selected ? new Color(1f, 0.85f, 0.2f, 0.8f) : new Color(1f, 1f, 1f, 0.35f);
                for (int i = 0; i < g.orders.Count; i++)
                {
                    GroupOrder o = g.orders[i];
                    Agent a = (o != null) ? o.agent : null;
                    if (!Usable(a)) continue;
                    DrawCross(cam, a.wPos, color, g.selected ? 7f : 5f);
                }
            }
        }

        /// <summary>十字标记（比小方块更容易看见：选中/受控单位用）。</summary>
        static void DrawCross(Camera cam, Vector3 world, Color c, float size)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            float x = sp.x;
            float y = Screen.height - sp.y;
            float h = size * 0.5f;

            GUI.color = c;
            GUI.DrawTexture(new Rect(x - h, y - 0.5f, size, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 0.5f, y - h, 1f, size), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>空心方框（目标格用）。</summary>
        static void DrawRing(Camera cam, Vector3 world, Color c, float size)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            float x = sp.x;
            float y = Screen.height - sp.y;
            float h = size * 0.5f;

            GUI.color = c;
            GUI.DrawTexture(new Rect(x - h, y - h, size, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - h, y + h - 1f, size, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - h, y - h, 1f, size), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + h - 1f, y - h, 1f, size), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>鼠标落点光标：指针处的地面点吸附到最近的 NavSpot 再画框（原版选中我队时也用这个提示）。</summary>
        static void DrawHoverCursor(Camera cam)
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (gm != null) ? gm.island : null;
            if (island == null) return;

            Vector3 land;
            string diag;
            if (!Plugin.TryGetLandPoint(island, Input.mousePosition, out land, out diag)) return;

            NavSpot spot = NavSpot.GetNavSpot(land, true);
            if (spot == null) return;

            DrawRing(cam, spot.navPos.wPos, new Color(0.4f, 1f, 1f, 0.5f), 22f);
        }
    }
}
