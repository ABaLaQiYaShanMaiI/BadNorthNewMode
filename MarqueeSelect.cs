using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>MarqueeSelect 的**状态机半边**（v1.7.3 / P2 拆分）：按下→拖动→松开的输入时序、选择应用、相机接管、
    /// 选中减速与选中集合（`_pending`）。另两半：`MarqueeSelect.Hits.cs`（候选登记 / 命中测试 / 修饰键与光标行为）、
    /// `MarqueeSelect.Overlay.cs`（框选矩形与标记绘制）。三份同为 `partial`，对外仍是同一个 `MarqueeSelect`。
    /// 原职责说明：左键从"非原生单位附近"起拖 = 框选（框选期间相机拖拽让位）；别处左键拖动仍由游戏平移相机。见 PROJECT_SPEC §4/§5。</summary>
    internal static partial class MarqueeSelect
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
        static bool _clickUsedForSelect;        // 这一次按下已判给"选整队 / 框选起手"→ 不能再被当成前进命令（按下时置位，保持到松开）
        static bool _pressWasDrag;              // 这一次按下最终变成了拖动（框选/平移相机）→ 普通点击的延迟下令据此排除
        static bool _slowMo;                    // 是否已向 TimeManager 申请减速
        static readonly object SlowMoOwner = new object();   // TimeManager 按 requester 记账（取最小值合并）
        static int _candFrame = -1;                          // v1.7.3：候选扫描的帧号（同帧去重用）
        static readonly List<Agent> _candOurs = new List<Agent>();   // v1.7.3：复用"我们的单位"暂存表（别再每次 new）

        internal static bool Dragging { get { return _dragging; } }
        internal static bool ClickUsedForSelect { get { return _clickUsedForSelect; } }
        internal static bool PressWasDrag { get { return _pressWasDrag; } }
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
            // 每次按下先清零、再由下面的分支置位——两个标记要**从按下保持到松开**（普通点击的延迟下令据此判断"这次点击属于选队/框选"）
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) { _clickUsedForSelect = false; _pressWasDrag = false; }
            if (!Util.V(ModConfig.RemoteControl, true)) { Cancel(gm); return; }
            string blockWhy;                                   // v1.7.1：联机中遥控让路（含框选与减速）—— 见兼容方案 §3.4
            if (Compat.RemoteBlocked(out blockWhy)) { Compat.NoteRemoteRefused(blockWhy); Cancel(gm); return; }
            if (IngameMenu.IsOpen) { Cancel(gm); return; }
            if (VanillaUI.ModalShowing) { Cancel(gm); return; }            // 原版确认框在前台：不框选也不拖相机

            string why;
            if (!Plugin.InBattle(gm, out why)) { Cancel(gm); return; }

            if (SelectAllKeyDown()) SelectAll();          // R = 全选所有可选的非原生单位

            // 互斥：你选了我方小队 → 我们的选择让位（这次点击完全归原版，绝不双控）
            if (VanillaSelected && HasPending)
            {
                ClearPending();
                if (Util.V(ModConfig.VerboseLog, false)) Util.Log(Loc.T("[NewMode][遥控] 你选中了我方小队 → 已清空遥控选择"));
            }

            bool routed = RemoteCursor.Available;          // v1.5.6：点击路由交给 RemoteCursor（按下即定归属，无 2 帧等待）
            bool single = routed && SingleButtonMode();    // 单键/触摸：空点由路由按"有没有选中"决定，不在这里补清空

            if (Input.GetMouseButtonDown(0))
            {
                if (IngameMenu.AnyPanelContains(Input.mousePosition) || SquadBar.Contains(Input.mousePosition)) return;   // 按在面板 / 头像条上：这次点击归它们
                if (Util.PointerOverUi()) return;                        // v1.7.4：按在原版 UI（技能条等）上 → 这次点击完全归 UI（**默认关**：`[Remote] UiClickGate`，见 §11.10 修订 4）
                _held = true;
                _dragging = false;
                _start = Input.mousePosition;
                _pressUnit = NearestForeignUnitWorld(_start);                 // 主路径：世界距离（与投放同一套 NavSpotCast）
                if (_pressUnit == null) _pressUnit = NearestForeignUnit(_start);   // 兜底：屏幕半径
                // 框选入口：按住 Alt（v1.6.0 起唯一）；"从自己单位上起拖"要 cfg RemoteMarqueeFromUnit 开（见 §5）
                _grab = FreeMarqueeKeyHeld() ||
                        (Util.V(ModConfig.RemoteMarqueeFromUnit, false) && _pressUnit != null);
                _clickUsedForSelect = FreeMarqueeKeyHeld() || (!routed && ShiftHeld() && (_pressUnit != null));
                if (_grab) DetachCamera(gm);     // 按下的瞬间就接管相机：这一次拖动不平移，避免"先平移一点再被接管"

                // 左键点在"我们的单位 / 我方小队"以外 = 取消我们的选中；**不看落点有效性**（否则贴岸海面会漏取消，见 §5/T34）
                if (routed && !single && !FreeMarqueeKeyHeld() && !VanillaSelected && !OverVanillaSquad(_start)
                    && HasPending && PickAtTight(_start) == null)
                    CancelSelectionLikeVanilla();
            }

            // 接管模式下右键专用于下令，所以只有非接管模式才需要"Shift + 右键点单位 = 选整队"（右键无拖动路径，按下即判定）
            if (!routed && Input.GetMouseButtonDown(1) && ShiftHeld())
            {
                ForeignUnit hit = NearestForeignUnitWorld(Input.mousePosition);
                if (hit == null) hit = NearestForeignUnit(Input.mousePosition);
                if (hit != null && hit.agent != null)
                {
                    _clickUsedForSelect = true;                  // 这一次按下用于选队，不做前进
                    ClickSelect(hit, true, false);
                }
            }

            if (_held && Input.GetMouseButton(0))
            {
                Vector2 now = Input.mousePosition;
                if (!_grab && FreeMarqueeKeyHeld())              // 先按下左键、再补按 FreeMarqueeKey：就地转成框选（起点 = 补按处）
                {
                    _grab = true;
                    _dragging = false;
                    _start = now;
                    _clickUsedForSelect = true;                  // 已转框选 → 这一次按下不算下令
                    DetachCamera(gm);
                }
                if (_grab && !_dragging && (now - _start).magnitude >= Util.V(ModConfig.RemoteMarqueePixels, 8))
                {
                    _dragging = true;
                    if (Util.V(ModConfig.VerboseLog, false)) Util.Log(Loc.T("[NewMode][遥控] 开始框选"));
                }
                if (_dragging) _screenRect = RectFrom(_start, now);
            }

            if (_held && (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)))   // 也兜底"按住时切窗口丢 Up 事件"
            {
                bool wasDrag = _dragging;
                ForeignUnit pressUnit = _pressUnit;
                _pressWasDrag = wasDrag;
                _held = false;
                _dragging = false;
                _grab = false;
                _pressUnit = null;
                AttachCamera(gm);

                if (wasDrag) ApplySelection();                              // 拖动 = 框选（可选）
                else if (!routed && pressUnit != null && pressUnit.agent != null && ShiftHeld())
                    ClickSelect(pressUnit, true, false);                     // 非接管模式：Shift + 左键点单位 = 并入该单位
            }

            UpdateSlowMo(_dragging || HasPending);             // 框选中 / 已有选中 → 减速（更易框住移动中的敌人）
        }

        /// <summary>点选：默认只选**一个单位**（精确）；wholeSquad（双击）= 选中它所在整队。append（Shift）= 并入 / 移出。</summary>
        static void ClickSelect(ForeignUnit unit, bool append, bool wholeSquad)
        {
            List<ForeignUnit> pick = wholeSquad ? SquadOf(unit) : new List<ForeignUnit>();
            if (!wholeSquad && unit != null) pick.Add(unit);
            SelectMany(pick, append);
        }

        /// <summary>整组一起选（底部头像条点一下用）：默认替换（再点同一组 = 清空），按住 Shift = 并入 / 移出。列表会被复制，不会被改动。</summary>
        internal static void SelectMany(List<ForeignUnit> pick, bool append)
        {
            if (pick == null || pick.Count == 0) return;

            ClearVanillaSelection();                     // 选中遥控单位前，取消我方小队的选择（保持"当前只选中一方"）
            if (_pending == null) _pending = new List<ForeignUnit>();
            _pendingCenter = Input.mousePosition;

            bool all = true;
            for (int i = 0; i < pick.Count; i++)
                if (!_pending.Contains(pick[i])) { all = false; break; }

            if (!append)
            {
                bool solo = all && _pending.Count == pick.Count;      // 正好只选了这些 → 再点 = 取消
                _pending.Clear();
                if (!solo) for (int i = 0; i < pick.Count; i++) _pending.Add(pick[i]);
            }
            else if (all)
            {
                for (int i = 0; i < pick.Count; i++) _pending.Remove(pick[i]);
            }
            else
            {
                for (int i = 0; i < pick.Count; i++)
                    if (!_pending.Contains(pick[i])) _pending.Add(pick[i]);
            }

            string msg = (_pending.Count == 0)
                ? Loc.T("已清空选择")
                : Loc.F("已选中 {0}（点地块 = 前进；双击单位 = 选整队）", DescribePending());
            IngameMenu.Say(msg);
            Util.Log(Loc.T("[NewMode][遥控] ") + msg);
        }

        /// <summary>该单位当前是否在"已选中"里（头像条高亮用）。</summary>
        internal static bool IsSelected(Agent a)
        {
            if (a == null || _pending == null) return false;

            for (int i = 0; i < _pending.Count; i++)
            {
                ForeignUnit f = _pending[i];
                if (f != null && object.ReferenceEquals(f.agent, a)) return true;
            }
            return false;
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

        /// <summary>v1.7.6：**换岛 / 回地图 / 关总开关**时把遥控侧的运行时状态全部复位（见 `SessionCleanup` / §11.14）。
        /// 相机让位要**先还回去再丢引用** —— 同岛重开时 `CursorManager` 与相机控制器可能还是同一批对象，
        /// 直接丢 `_detached` 会让"相机再也不能拖动"（这是最容易漏、也最难查的一类残留）。</summary>
        internal static void ClearSession()
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            if (gm != null && gm.cursorManager != null) AttachCamera(gm);
            _detached = null;

            _pending = null;
            _pendingCenter = Vector2.zero;
            _held = false;
            _grab = false;
            _dragging = false;
            _pressUnit = null;
            _clickUsedForSelect = false;
            _pressWasDrag = false;
            _screenRect = new Rect(0f, 0f, 0f, 0f);
            ClearSlowMo();
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
                ? Loc.T("框里没有可指挥的单位")
                : Loc.F("已选中 {0} 个单位，点地块前进", _pending.Count);
            IngameMenu.Say(msg);
            Util.Log(Loc.F("[NewMode][遥控] {0}（矩形 {1:F0}×{2:F0}；登记 {3}，可用 {4}，命中 {5}；最近 {6}）",
                msg, _screenRect.width, _screenRect.height, ForeignUnit.All.Count, ForeignUnit.UsableCount(), picked.Count,
                NearestScreenInfo(_pendingCenter)));
        }

        static string DescribePending()
        {
            if (_pending == null || _pending.Count == 0) return Loc.T("无");

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
                s = (s == null) ? "" : (s + Loc.T("、"));
                s += UnitNames.Of(types[i]) + "×" + counts[i];
            }
            return (s == null) ? Loc.T("无") : s;
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

        /// <summary>接管点击用的严格命中：落点不可站立（点海面）时跳过世界距离匹配，只留 24px 屏幕兜底（否则点击会被抢走，见 §5/T34）。</summary>
        internal static ForeignUnit PickAtTight(Vector2 screenPos)
        {
            ForeignUnit f = ValidStand(screenPos) ? NearestForeignUnitWorld(screenPos) : null;
            if (f == null) f = NearestForeignUnitWithin(screenPos, 24);
            return f;
        }

        /// <summary>点选入口（RemoteCursor 的点击回调）；wholeSquad = 双击。</summary>
        internal static void SelectUnitAt(ForeignUnit unit, bool append, bool wholeSquad)
        {
            ClickSelect(unit, append, wholeSquad);
        }

        /// <summary>原版此刻指针下有没有我方小队（决定右键归谁；同 `Navigator.SelectPC`）。</summary>
        internal static bool OverVanillaSquad(Vector2 screenPos)
        {
            SquadSelector ss = Singleton<SquadSelector>.instance;
            if (ss == null) return false;
            return !object.ReferenceEquals(ss.GetSquadFromRaycast(screenPos), null);
        }

        /// <summary>原版"有效目标格"的判据（同 `NavSpotter.NavSpotCast`）：Voxels/Modules 两层取最近格，**离落点 >1m 即无效**（点崖壁/建筑侧面/海面都会命中）。</summary>
        internal static NavSpot NavSpotAt(Vector2 screenPos, Island island)
        {
            // ① 原版同源：返回 null 就是"无效格"，直接采信——用自家全场景射线"翻案"会把点海面判成有效（见 §5/T34）
            try { return NavSpot.NavSpotCast(screenPos); }
            catch { }                                        // 只有取点本身抛异常（currentLevel 未就绪等）才兜底 // silent-ok（v1.7.3：既有设计——失败即静默降级，不刷日志）

            // ② 兜底：照抄原版 NavSpotter.NavSpotCast（Voxels/Modules 命中点 → GetNavSpot → 同一道 1m 闸门）
            if (island != null && island.navSpotter != null)
            {
                try
                {
                    RaycastHit hit;
                    island.navSpotter.NavSpotCast(screenPos, out hit);
                    if (hit.collider == null) return null;
                    NavSpot spot = NavSpot.GetNavSpot(hit.point, true);
                    if (spot == null) return null;
                    return (Vector3.SqrMagnitude(spot.navPos.pos - hit.point) <= 1f) ? spot : null;
                }
                catch { } // silent-ok（v1.7.3：既有设计——失败即静默降级，不刷日志）
            }

            Vector3 land;
            string diag;
            if (island == null || !Plugin.TryGetLandPoint(island, screenPos, out land, out diag)) return null;

            NavSpot near = NavSpot.GetNavSpot(land, true);
            return (near != null && Vector3.SqrMagnitude(near.navPos.pos - land) <= 1f) ? near : null;
        }

        static Island CurrentIsland()
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            return (gm != null) ? gm.island : null;
        }

        static bool ValidStand(Vector2 screenPos)
        {
            return NavSpotAt(screenPos, CurrentIsland()) != null;
        }

        /// <summary>照抄原版：点海面 / 非可站立地块 = 取消选中（有选中播 `UnitDeselect`，否则播 `Error`）。</summary>
        internal static void CancelSelectionLikeVanilla()
        {
            if (HasPending)
            {
                ClearPending();
                FabricWrapper.PostEvent("UI/InGame/UnitDeselect");
                IngameMenu.Say(Loc.T("已取消选择"));
                return;
            }

            FabricWrapper.PostEvent("UI/InGame/Error");
        }

        /// <summary>一键全选：把所有"可选"的非原生单位加入待成队选择（等价于 Shift 全框）。</summary>
        internal static void SelectAll()
        {
            EnsureCandidates();
            ClearVanillaSelection();                     // 与 Shift 点选一致：选中遥控单位前先取消我方选择
            _pendingCenter = Input.mousePosition;        // 供 Capture 按"离光标由近到远"分队（否则用的是上一次的陈旧中心）
            if (_pending == null) _pending = new List<ForeignUnit>();
            _pending.Clear();

            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (Usable(a)) _pending.Add(f);
            }

            string msg = (_pending.Count == 0)
                ? Loc.T("没有可选单位（未生成或已阵亡）")
                : Loc.F("已全选 {0}，点地块前进", DescribePending());
            IngameMenu.Say(msg);
            Util.Log(Loc.T("[NewMode][遥控] ") + msg);
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

        /// <summary>框选中 / 已有选中 → 向原版 TimeManager 申请减速（同一套 API、按 requester 取最小值合并；见 PROJECT_SPEC §5）。</summary>
        static void UpdateSlowMo(bool want)
        {
            bool on = want && Util.V(ModConfig.RemoteSlowMo, true);
            if (on == _slowMo) return;

            _slowMo = on;
            if (on)
            {
                TimeManager.RequestTimeScale(SlowMoOwner, Util.V(ModConfig.RemoteSlowMoScale, 0.1f));
                if (Util.V(ModConfig.VerboseLog, false)) Util.Log(Loc.T("[NewMode][遥控] 减速中（框选/已选中）"));
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
            // v1.7.6：相机可能已随场景销毁（离开战局 / 重开战局）→ 不要把它塞回 CursorManager
            //（否则原版 `CameraController.OnDrag` 会在销毁对象上被调用）。Unity 的 `!= null` 能识别已销毁对象。
            CameraController cam = _detached as CameraController;
            if (cam != null)
            {
                if (cm != null && !cm.Contains(_detached)) cm.Add(_detached);
            }
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

        /// <summary>用"已知世界点 ↔ 已知屏幕点"测误差挑相机：`cameraRef` 可能指向 CampaignCamera（会把单位投到屏幕外），谁准用谁（见 §4 坑表）。</summary>
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
                    Util.Log(Loc.F("[NewMode][遥控] 改用相机 {0}（与已知点误差 {1:F0}px；原 {2}）",
                        best.name, bestErr, (_cam != null) ? _cam.name : Loc.T("无")));
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

    }
}
