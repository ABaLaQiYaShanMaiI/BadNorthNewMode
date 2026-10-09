using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>入口与输入：菜单（IngameMenu）→ 世界点击 → 解析目标（DropPlanner）→ 投放（LandingInjector）。</summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "badnorth.newmode";
        public const string NAME = "Bad North - New Mode";
        public const string VERSION = "1.5.7";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        bool _subscribed;
        bool _subscribeFailed;
        Vector2 _pressPos;                 // 本次按下的屏幕位置（区分"点击"与"拖动/平移相机"）
        Vector2 _pendingMovePos;           // 挂起的"普通点击下令"（见 HandleRemoteMode / FlushPendingMove）
        int _pendingMoveFrame;             // 0 = 无挂起
        bool _pressOrdered;                // 本次按下是否已下过令（避免"按下立即下令 + 松开又挂起"）
        bool _pressVanillaSelected;        // **按下那一帧**原版是否正选着我方小队（OneButton 模式下原版会"移动并取消选择"，松开时已查不出来）
        bool _pendingVanillaBusy;          // 挂起的这次点击当时原版正管着我方小队 → 整次点击归原版
        float _nextCandidateScan;          // 候选刷新节流（原生单位落地后尽快可选中）

        void Awake()
        {
            Instance = this;
            Log = Logger;
            try { ModConfig.Bind(Config); }
            catch (System.Exception e)
            {
                // 配置绑定失败不再让整个插件在 Awake 抛异常（否则游戏里完全无反馈），只报错后降级。
                Log.LogError(Loc.F("[NewMode] 配置绑定失败：{0}", e));
            }

            // v1.5.0：默认不生成日志文件（尽早执行，cfg 里的启动日志也一并清掉）。
            LogFileSwitch.Apply(Util.V(ModConfig.LogToFile, false));

            ConfigEntry<KeyboardShortcut> hk = ModConfig.Hotkey;
            Log.LogInfo(Loc.F("[NewMode] v{0} 已加载：{1} 开关投放模式，点击滩头陆地投放敌舰。",
                VERSION, (hk != null) ? hk.Value.ToString() : Loc.T("(热键未绑定)")));
        }

        void Update()
        {
            LogFileSwitch.Tick();                       // 重试删日志残留（≤30s，见 LogFileSwitch）
            ClickShield.Sync(IngameMenu.IsOpen, IngameMenu.MenuRect);   // 菜单开=盖住原版点击（见 §6 T2）；用上一帧矩形
            if (ModConfig.Hotkey == null) return;

            TrySubscribeGameClick();
            RemoteCursor.Ensure();                      // v1.5.6：点击路由（左键点单位即选中、右键点地块即前进）

            // 候选定时刷新：原生单位一落地就能被选中 / 计入 HUD（否则要等玩家先点一下别处才登记，见 §5）
            if (Time.unscaledTime >= _nextCandidateScan)
            {
                _nextCandidateScan = Time.unscaledTime + 0.25f;
                MarqueeSelect.EnsureCandidates();
            }

            if (ModConfig.Hotkey.Value.IsDown())
            {
                IngameMenu.Toggle();                      // 菜单即投放模式
                if (!IngameMenu.IsOpen) return;
            }

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;

            MarqueeSelect.Tick(gm);                        // 菜单开着时内部自动取消
            RemoteGroup.Tick();                            // 组维护与投放/遥控模式无关
            LevelTools.Tick(gm);                           // F3 强制胜利 + 拦下原版波次（v1.5.6）

            if (IngameMenu.IsOpen) { _pendingMoveFrame = 0; HandleDropMode(gm); return; }   // 开菜单：丢弃挂起的下令
            HandleRemoteMode(gm);                          // v1.4.0：菜单关闭时 = 遥控模式
        }

        /// <summary>投放模式（F1 菜单开着时）：滩头悬停预览 + 点击投放。</summary>
        void HandleDropMode(IslandGameplayManager gm)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                IngameMenu.Close();
                IngameMenu.Say(Loc.T("已取消投放"));
                return;
            }

            string why;
            if (!InBattle(gm, out why)) { PlacementMarker.Get().Hide(); IngameMenu.Hover = why; return; }

            IngameMenu.EnsureUnits(gm.island);

            // 悬停预览（点击本身由游戏事件负责）
            Vector2 screenPos = Input.mousePosition;
            if (IngameMenu.Contains(screenPos)) { PlacementMarker.Get().Hide(); IngameMenu.Hover = Loc.T("（指针在菜单上）"); return; }

            Vector3 land;
            string diag;
            bool hasLand = TryGetLandPoint(gm.island, screenPos, out land, out diag);

            DropTarget target = default(DropTarget);
            string reason = null;
            bool ok = hasLand && DropPlanner.TryResolve(gm.island, land, out target, out reason);

            if (Util.V(ModConfig.ShowHoverPreview, true))
            {
                if (!hasLand) PlacementMarker.Get().Hide();
                else PlacementMarker.Get().Show(ok ? target.beach.navPos.pos : land, ok, 0.35f);
            }

            IngameMenu.Hover = !hasLand
                ? Loc.F("指针不在陆地上（{0}）", diag)
                : (ok ? Loc.F("滩头可用：落差 {0:F2}m，距点击处 {1:F1}m",
                        target.beach.navPos.pos.y - Util.V(ModConfig.WaterLevelY, 0f), target.shoreDist)
                      : reason);

            // 兜底：未订阅成功才轮询（避免一次点击投两艘）
            if (!_subscribed && Input.GetMouseButtonDown(0))
                DoDrop(gm, screenPos, Loc.T("轮询兜底"));
        }

        /// <summary>遥控模式（菜单关闭时）。v1.5.6：点击路由交给 RemoteCursor（可用时）——本方法只负责旧路径兜底。</summary>
        void HandleRemoteMode(IslandGameplayManager gm)
        {
            PlacementMarker.Get().Hide();
            if (RemoteCursor.Available) return;            // 按下即定归属，这里不再处理点击（见 RemoteCursor）

            // 按下瞬间记下"原版此刻是否正选着我方小队"：OneButton 模式下原版点地块会「移动我方小队 + 取消选择」，
            // 松开/事后都已查不出来 → 必须在按下那一帧留证（见 §5/T22）。
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) _pressVanillaSelected = MarqueeSelect.VanillaSelected;

            FlushPendingMove(gm);                                             // ① 先结算上一次"普通点击下令"

            if (MarqueeSelect.Dragging) return;                               // 正在框选：不下令
            if (!RemoteGroup.Any && !MarqueeSelect.HasPending) return;         // 既没有小队、也没有待成队 → 左右键完全归原版
            if (MarqueeSelect.VanillaSelected) return;                        // 你正选着我方小队 → 这次点击归原版（绝不双控）

            bool down = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
            bool up = Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1);

            if (down)
            {
                _pressOrdered = false;
                _pressPos = Input.mousePosition;
                if (MarqueeSelect.ClickUsedForSelect) return;                 // 这一下是"选整队 / 框选起手"
                if (!MarqueeSelect.CommandModifierHeld()) return;             // 普通点击：留到松开后再挂起（见下方 up）

                ExecuteMove(gm, _pressPos);                                   // Shift / 按住 R：立即下令
                _pressOrdered = true;
                return;
            }

            // v1.5.4：普通点击（无需修饰键）也能下令——松手时挂起，满 2 帧确认原版没接管这次点击才执行（见 §5/T22）
            if (!up || _pressOrdered) return;
            if (MarqueeSelect.ClickUsedForSelect || MarqueeSelect.PressWasDrag) return;
            if (MarqueeSelect.CommandModifierHeld()) return;                  // 修饰键在按下帧已立即下令
            if (Util.V(ModConfig.RemoteMoveRequiresModifier, false)) return;  // cfg：要求修饰键 → 普通点击 100% 归原版
            if (((Vector2)Input.mousePosition - _pressPos).magnitude > Util.V(ModConfig.RemoteMarqueePixels, 8)) return;   // 拖过 → 是平移/框选，不是点击
            if (_pressVanillaSelected) return;                                // 按下时你正管着我方小队 → 这次点击归原版

            _pendingMovePos = Input.mousePosition;
            _pendingMoveFrame = Time.frameCount;
            _pendingVanillaBusy = _pressVanillaSelected;
        }

        /// <summary>结算挂起的"普通点击下令"：满 2 帧、且原版没把它当成"选/移我方小队"也没在框选 → 执行。</summary>
        void FlushPendingMove(IslandGameplayManager gm)
        {
            if (_pendingMoveFrame <= 0) return;
            if (Time.frameCount <= _pendingMoveFrame + 1) return;             // 等 2 帧：给原版的点击处理表态的时间

            Vector2 pos = _pendingMovePos;
            _pendingMoveFrame = 0;

            if (MarqueeSelect.VanillaSelected || MarqueeSelect.Dragging || _pendingVanillaBusy)
            {
                if (Util.V(ModConfig.VerboseLog, false)) Util.Log(Loc.T("[NewMode][遥控] 原版接管了这次点击 → 放弃遥控下令"));
                return;
            }

            string why;
            if (!InBattle(gm, out why)) return;                              // 开菜单 / 暂停 / 离岛：静默丢弃
            ExecuteMove(gm, pos);
        }

        internal static void RemoteOrderAt(Vector2 screenPos)
        {
            if (Instance == null) return;
            Instance.ExecuteMove(Singleton<IslandGameplayManager>.instance, screenPos);
        }

        /// <summary>下达前进（右键路由 / 立即下令 / 延迟路径共用）；还在船上的只记"登陆后集结点"（见 §5）。</summary>
        void ExecuteMove(IslandGameplayManager gm, Vector2 screenPos)
        {
            string why;
            if (!InBattle(gm, out why)) { IngameMenu.Say(why); return; }
            if (IngameMenu.Contains(screenPos)) return;                      // 菜单区域内的点击不做下令

            Vector3 land;
            string diag;
            if (!TryGetLandPoint(gm.island, screenPos, out land, out diag))
            {
                FailedTarget(Loc.F("那里不是可站立的地面：{0}", diag));
                return;
            }

            NavSpot spot = NavSpot.GetNavSpot(land, true);
            if (spot == null) { FailedTarget(Loc.T("那里不是可站立的陆地地块")); return; }

            // 有待成队 → 先按兵种分队（多兵种就地分到相邻格），再一起前进；没有待成队 → 直接命令已有小队
            Vector2 center;
            List<ForeignUnit> pending = MarqueeSelect.TakePending(out center);

            if ((pending == null || pending.Count == 0) && RemoteGroup.GroupCount == 0)
            {
                IngameMenu.Say(Loc.T("没有选中任何单位：先点一个单位选中它"));
                return;
            }

            string msg;
            if (pending != null && pending.Count > 0)
            {
                List<ForeignUnit> ready = new List<ForeignUnit>();
                List<ForeignUnit> aboard = new List<ForeignUnit>();
                for (int i = 0; i < pending.Count; i++)
                {
                    ForeignUnit f = pending[i];
                    if (f == null) continue;
                    if (ForeignUnit.Commandable(f.agent)) ready.Add(f);
                    else if (ForeignUnit.Selectable(f.agent)) aboard.Add(f);   // 还在船上 → 只记集结点
                }

                msg = null;
                if (ready.Count > 0)
                {
                    string capMsg;
                    RemoteGroup.Capture(ready, center, out capMsg);
                    msg = capMsg;
                }
                if (aboard.Count > 0)
                {
                    string rallyMsg;
                    RemoteGroup.RequestRally(aboard, spot, out rallyMsg);
                    msg = string.IsNullOrEmpty(msg) ? rallyMsg : (msg + " → " + rallyMsg);
                }
                if (RemoteGroup.GroupCount > 0)
                {
                    string moveMsg;
                    RemoteGroup.MoveTo(spot, out moveMsg);
                    msg = string.IsNullOrEmpty(msg) ? moveMsg : (msg + " → " + moveMsg);
                }
                if (string.IsNullOrEmpty(msg)) msg = Loc.T("没有可下令的单位");
            }
            else
            {
                RemoteGroup.MoveTo(spot, out msg);
            }

            IngameMenu.Say(msg);
            Util.Log(Loc.T("[NewMode][遥控] ") + msg);
            MarqueeSelect.ClearPending();                                    // 移动后取消选择（与原版 SquadMover 一致，也恢复时间流速）
        }

        /// <summary>照抄原版 `Navigator.SelectPC`：点在海面/非可站立地块 = 有选中则**取消选中**（原版 `DeselectUnit` + `UnitDeselect` 音），没选中则算无效点击（`Error` 音，见 §5/T23）。</summary>
        void FailedTarget(string reason)
        {
            if (MarqueeSelect.HasPending)
            {
                MarqueeSelect.ClearPending();                                // 对应原版 squadSelector.SelectSquad(null,false)：取消选中 + 恢复时间流速
                FabricWrapper.PostEvent("UI/InGame/UnitDeselect");
                IngameMenu.Say(reason + Loc.T("（已取消选择）"));
                return;
            }

            FabricWrapper.PostEvent("UI/InGame/Error");
            IngameMenu.Say(reason);
        }

        /// <summary>反射订阅 pointerRationalizer.onClick（System.Core 3.5 的 Action`2，见 §4 坑表）。</summary>
        void TrySubscribeGameClick()
        {
            if (_subscribed || _subscribeFailed) return;
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            if (gm == null) return;
            PointerRationalizer pr = gm.pointerRationalizer;
            if (pr == null) return;

            try
            {
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                System.Reflection.EventInfo ev = pr.GetType().GetEvent("onClick");
                System.Reflection.MethodInfo mi = typeof(Plugin).GetMethod("OnWorldClick", flags);
                // 注意：不能用 == null —— MemberInfo/EventInfo 的 op_Equality 是 .NET 4.0 才有的，
                // 游戏 mscorlib 2.0 没有（ReferenceEquals 走 object，安全）。
                if (object.ReferenceEquals(ev, null) || object.ReferenceEquals(mi, null))
                {
                    _subscribeFailed = true;
                    Util.Warn(Loc.T("[NewMode] 找不到 onClick 事件或回调方法 → 改用轮询兜底。"));
                    return;
                }

                System.Delegate d = System.Delegate.CreateDelegate(ev.EventHandlerType, this, mi);
                ev.AddEventHandler(pr, d);
                _subscribed = true;
                Util.Log(Loc.T("[NewMode] 已反射订阅原版世界点击事件 pointerRationalizer.onClick（同 Navigator/ConfirmButton 的数据源）。"));
            }
            catch (System.Exception e)
            {
                _subscribeFailed = true;
                Util.Warn(Loc.F("[NewMode] 订阅 onClick 失败：{0} → 改用轮询兜底。", e.Message));
            }
        }

        /// <summary>原版世界点击回调（在游戏的事件分发里执行，必须自吞异常，不能污染游戏输入链）。</summary>
        void OnWorldClick(PointerEventData.InputButton button, Vector2 screenPos)
        {
            try
            {
                if (!IngameMenu.IsOpen) return;
                if (button != PointerEventData.InputButton.Left) return;
                if (IngameMenu.Contains(screenPos))                            // 菜单内的左键属于选兵种，不做投放
                {
                    if (Util.V(ModConfig.VerboseLog, false)) Util.Log(Loc.T("[NewMode] 菜单内点击 → 忽略投放"));
                    return;
                }

                IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
                string why;
                if (!InBattle(gm, out why)) { Util.Warn(Loc.T("[NewMode][点击] ") + why); return; }

                DoDrop(gm, screenPos, Loc.T("游戏事件"));
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode][点击] 处理异常：{0}", e));
            }
        }

        /// <summary>一次投放尝试：全程打日志（排查"点了没反应"的唯一依据）。</summary>
        void DoDrop(IslandGameplayManager gm, Vector2 screenPos, string source)
        {
            Util.Log(Loc.F("[NewMode][点击] 屏幕 ({0:F0},{1:F0}) 来源={2}", screenPos.x, screenPos.y, source));

            Vector3 land;
            string diag;
            if (!TryGetLandPoint(gm.island, screenPos, out land, out diag))
            {
                Util.Warn(Loc.F("[NewMode][点击] 地形未命中：{0}", diag));
                IngameMenu.Say(Loc.F("这一点不是陆地地块：{0}", diag));
                return;
            }
            Util.Log(Loc.F("[NewMode][点击] 命中地形：{0}，点 {1}，海拔 {2:F2}m",
                diag, Util.Fmt(land), land.y - Util.V(ModConfig.WaterLevelY, 0f)));

            DropTarget t;
            string reason;
            if (!DropPlanner.TryResolve(gm.island, land, out t, out reason))
            {
                Util.Warn(Loc.F("[NewMode][点击] 无法投放：{0}", reason));
                IngameMenu.Say(Loc.F("无法投放：{0}", reason));
                return;
            }

            string info;
            if (LandingInjector.TrySpawn(gm.island, land, out info))
            {
                Util.Log(Loc.T("[NewMode][点击] ") + info);
                IngameMenu.Say(info);
            }
            else
            {
                Util.Warn(Loc.F("[NewMode][点击] 投放失败：{0}", info));
                IngameMenu.Say(Loc.F("投放失败：{0}", info));
            }
        }

        /// <summary>只在"正常战局、岛屿处于 Playing"时可投放 / 可下令。</summary>
        internal static bool InBattle(IslandGameplayManager gm, out string why)
        {
            why = null;
            if (gm == null) { why = Loc.T("不在战局中"); return false; }
            if (gm.levelPauser != null && gm.levelPauser.isPaused) { why = Loc.T("已暂停"); return false; }
            Island island = gm.island;
            if (island == null || !island.generated) { why = Loc.T("岛屿未就绪"); return false; }
            if (island.state != Island.State.Playing) { why = Loc.T("岛屿未进入 Playing 状态"); return false; }
            if (island.raid == null) { why = Loc.T("Raid 未就绪"); return false; }
            return true;
        }

        /// <summary>屏幕坐标 → 地面点（首选 NavSpotter.NavSpotCast，兜底 viewport 射线 × Voxels 层）。</summary>
        internal static bool TryGetLandPoint(Island island, Vector2 screenPos, out Vector3 point, out string diag)
        {
            point = Vector3.zero;
            diag = null;

            if (island != null && island.navSpotter != null)
            {
                try
                {
                    RaycastHit hit;
                    island.navSpotter.NavSpotCast(screenPos, out hit);
                    if (hit.collider != null)
                    {
                        point = hit.point;
                        diag = Loc.F("NavSpotCast 命中 {0}", HitName(hit));
                        return true;
                    }
                    diag = Loc.T("NavSpotCast 未命中");
                }
                catch (System.Exception e)
                {
                    diag = Loc.F("NavSpotCast 异常 {0}", e.GetType().Name);
                }
            }
            else
            {
                diag = Loc.T("navSpotter 不可用");
            }

            LevelCamera lc = Singleton<LevelCamera>.instance;
            Camera cam = (lc != null) ? lc.cameraRef : Camera.main;
            if (cam == null) return false;

            Vector2 vp = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
            Ray ray = cam.ViewportPointToRay(vp);
            int mask = LayerMaster.voxelMask.value;
            RaycastHit h;
            if (mask != 0 && Physics.Raycast(ray, out h, 500f, mask))
            {
                point = h.point;
                diag += Loc.F("；Voxels 层命中 {0}", HitName(h));
                return true;
            }
            if (Physics.Raycast(ray, out h, 500f))
            {
                point = h.point;
                diag += Loc.F("；任意碰撞体命中 {0}", HitName(h));
                return true;
            }
            diag += Loc.F("；射线仍未命中（mask={0}，vp={1:F3},{2:F3}）", mask, vp.x, vp.y);
            return false;
        }

        static string HitName(RaycastHit hit)
        {
            GameObject go = hit.collider.gameObject;
            string layer = LayerMask.LayerToName(go.layer);
            return go.name + "@" + (string.IsNullOrEmpty(layer) ? go.layer.ToString() : layer);
        }

        /// <summary>HUD + 投放菜单（IngameMenu）+ 遥控框选与标记（MarqueeSelect）。</summary>
        void OnGUI()
        {
            IngameMenu.Draw();
            MarqueeSelect.DrawOverlay();
        }

        void OnDestroy()
        {
            MarqueeSelect.ClearSlowMo();          // 卸载时释放减速，避免 TimeManager 里留残账
            ClickShield.Destroy();
            RemoteCursor.ForceRelease();
        }

    }
}
