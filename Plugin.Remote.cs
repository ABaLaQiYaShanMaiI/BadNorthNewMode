using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>遥控半边（v1.7.3 / P2 拆分自 `Plugin.cs`，纯搬动、IL 级零行为变更）：菜单关闭时的点击处理
    /// （`RemoteCursor` 取不到时的旧路径兜底 + 挂起 2 帧仲裁）、前进下令与集结点、全选/释放、以及原版世界点击事件的订阅。</summary>
    public partial class Plugin : BaseUnityPlugin
    {
        /// <summary>遥控模式（菜单关闭时）。v1.5.6：点击路由交给 RemoteCursor（可用时）——本方法只负责旧路径兜底。</summary>
        void HandleRemoteMode(IslandGameplayManager gm)
        {
            PlacementMarker.Get().Hide();
            if (VanillaUI.ModalShowing) return;             // 原版确认框在前台：点击全归它（否则点确认会顺带指挥单位）

            // v1.7.1：联机中遥控让路（客机被主机姿态流覆盖 / 主机会污染对方账本）——
            // 这是 RemoteCursor 取不到时的旧路径兜底（v1.5.4 那套点击逻辑）。
            string blockWhy;
            if (Compat.RemoteBlocked(out blockWhy)) { _pendingMoveFrame = 0; Compat.NoteRemoteRefused(blockWhy); return; }

            if (RemoteCursor.Available) return;             // 按下即定归属，这里不再处理点击（见 RemoteCursor）

            // 按下瞬间记下"原版此刻是否正选着我方小队"：OneButton 模式下原版点地块会「移动我方小队 + 取消选择」，
            // 松开/事后都已查不出来 → 必须在按下那一帧留证（见 §5/T22）。
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) _pressVanillaSelected = MarqueeSelect.VanillaSelected;

            FlushPendingMove(gm);                                             // ① 先结算上一次"普通点击下令"

            if (MarqueeSelect.Dragging) return;                               // 正在框选：不下令
            if (!MarqueeSelect.HasPending) return;                            // 我们这边没选中 → 左右键完全归原版（未选中不接管，见 §5）
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

            if (MarqueeSelect.VanillaSelected || MarqueeSelect.Dragging || _pendingVanillaBusy || VanillaUI.ModalShowing)
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
            if (VanillaUI.ModalShowing) return;                              // 确认框在前台：不下令
            if (!InBattle(gm, out why)) { IngameMenu.Say(why); return; }
            if (IngameMenu.AnyPanelContains(screenPos) || SquadBar.Contains(screenPos)) return;   // 面板 / 提示框 / 头像条内不做下令

            NavSpot spot = MarqueeSelect.NavSpotAt(screenPos, gm.island);
            if (spot == null) { FailedTarget(Loc.T("那里不是可站立的陆地地块")); return; }   // 内部按"有没有选中"选音效

            Vector2 center;
            List<ForeignUnit> pending = MarqueeSelect.TakePending(out center);

            // 没有"本次选中"就什么都不做：哪怕册上已有遥控小队也不移动（未选中不得移动，见 §5）
            if (pending == null || pending.Count == 0)
            {
                IngameMenu.Say(Loc.T("没选中单位：先点一个单位"));
                return;
            }

            string msg;
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

        /// <summary>释放遥控（F1 菜单）：受控小队全部还原接管前的 order，交还原版 AI（见 §5）。</summary>
        internal static void ReleaseRemoteControl()
        {
            int groups = RemoteGroup.GroupCount;
            int units = RemoteGroup.TotalCount;

            if (groups == 0 && units == 0)
            {
                VanillaUI.Error();
                IngameMenu.Say(Loc.T("没有受控的遥控小队"));
                return;
            }

            // 破坏性操作：先弹原版确认框（可关掉），未弹成/取消时按原样直接执行
            if (VanillaUI.Confirm(Loc.T("释放遥控"),
                    Loc.F("把 {0} 支小队 / {1} 个单位交还原版 AI？", groups, units), DoReleaseRemoteControl)) return;

            DoReleaseRemoteControl();
        }

        static void DoReleaseRemoteControl()
        {
            int groups = RemoteGroup.GroupCount;
            int units = RemoteGroup.TotalCount;

            MarqueeSelect.ClearPending();
            RemoteGroup.Clear();                                     // 还原 order + 销毁我们挂的组件

            string msg = Loc.F("已释放遥控：{0} 支小队 / {1} 个单位", groups, units);
            IngameMenu.Say(msg);
            Util.Log(Loc.T("[NewMode][遥控] ") + msg);
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
                if (IngameMenu.AnyPanelContains(screenPos) || SquadBar.Contains(screenPos))   // 面板 / 提示框 / 头像条内的左键不属于投放
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

    }
}