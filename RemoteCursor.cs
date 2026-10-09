using UnityEngine;
using UnityEngine.EventSystems;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>点击路由：按下瞬间把自己压到 CursorManager 栈顶吃掉这一次点击（原版不误选我方 / 不播错误音 / 免 2 帧仲裁），松开还回去。见 PROJECT_SPEC §5。</summary>
    internal sealed class RemoteCursor : MonoBehaviour, CursorManager.IPointerCursor
    {
        static RemoteCursor _instance;
        static bool _unavailable;
        bool _pushed;
        bool _intentOrder;          // 按下时定"选 or 走"（单键模式靠按钮分不出来）
        int _pushFrame;
        ForeignUnit _lastUnit;      // 双击检测：同一个单位 + 0.35s 内再点 = 选整队
        float _lastClickAt;

        /// <summary>点击路由是否可用；false → 回退到 v1.5.4 的点击逻辑。</summary>
        internal static bool Available { get { return _instance != null; } }

        internal static RemoteCursor Get()
        {
            if (_instance != null) return _instance;

            GameObject go = new GameObject("BadNorthNewMode.RemoteCursor");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<RemoteCursor>();
            return _instance;
        }

        /// <summary>启用点击路由（不再反射订阅 onButtonDown —— 该事件在实机上取不到，见 §4）；cfg 选 Left 则走 v1.5.4 旧逻辑。</summary>
        internal static void Ensure()
        {
            if (_instance != null || _unavailable) return;

            if (string.Equals(Util.V(ModConfig.RemoteOrderButton, "Auto"), "Left", System.StringComparison.OrdinalIgnoreCase))
            {
                _unavailable = true;
                return;
            }

            Get();
            Util.Log(Loc.T("[NewMode] 点击路由已启用：左键选整队、右键前进（自动跟随单/双键设置）。"));
        }

        /// <summary>按下瞬间定归属与意图（"选/走"由单双键设置决定）；只有归我们才压栈顶。</summary>
        void TryCaptureOnPress()
        {
            try
            {
                if (!Util.V(ModConfig.RemoteControl, true)) return;
                if (IngameMenu.IsOpen) return;                          // 投放模式：点击归投放

                Vector2 screenPos = Input.mousePosition;
                if (!Plugin.InBattle(Singleton<IslandGameplayManager>.instance, out _)) return;

                ForeignUnit over = MarqueeSelect.PickAt(screenPos);

                if (MarqueeSelect.SingleButtonMode())
                {
                    // 单键/触摸：照抄原版映射（没选中→选；有选中→走）
                    if (MarqueeSelect.VanillaSelected) return;
                    if (MarqueeSelect.ShiftHeld() && over != null) { Push(false); return; }
                    if (MarqueeSelect.HasSelection()) { Push(true); return; }
                    if (over != null) { Push(false); return; }
                    return;
                }

                if (Input.GetMouseButtonDown(0))
                {
                    if (over == null) return;                           // 不点在可选单位上 → 归原版
                    Push(false);
                }
                else if (Input.GetMouseButtonDown(1))
                {
                    if (MarqueeSelect.VanillaSelected) return;           // 原版管着我方小队 → 归原版
                    Push(true);
                }
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 点击路由异常：{0}", e));
            }
        }

        void Push(bool intentOrder)
        {
            CursorManager cm = Manager();
            if (cm == null) return;

            CursorManager.IPointerCursor self = this;
            if (cm.Contains(self)) return;

            cm.Add(self);                                   // Add 会把原版 Navigator 临时 SetActive(false)
            _pushed = true;
            _intentOrder = intentOrder;
            _pushFrame = Time.frameCount;
        }

        void Pop()
        {
            if (!_pushed) return;
            _pushed = false;

            CursorManager cm = Manager();
            if (cm == null) return;

            CursorManager.IPointerCursor self = this;
            if (cm.Contains(self)) cm.Remove(self);          // Remove 会把 Navigator SetActive(true) 还回去
        }

        static CursorManager Manager()
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            return (gm != null) ? gm.cursorManager : null;
        }

        static PointerRationalizer Pointer()
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            return (gm != null) ? gm.pointerRationalizer : null;
        }

        void Update()
        {
            if (!_pushed)
            {
                if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) TryCaptureOnPress();
                return;
            }

            PointerRationalizer pr = Pointer();
            bool dragging = (pr != null) && pr.state == PointerRationalizer.State.Dragging;
            bool released = !Input.GetMouseButton(0) && !Input.GetMouseButton(1);
            if (dragging || (released && Time.frameCount > _pushFrame)) Pop();
        }

        void CursorManager.ICursor.SetActive(bool active) { }

        void CursorManager.IPointerCursor.OnButtonDown(PointerEventData.InputButton button, Vector2 screenPos) { }

        void CursorManager.IPointerCursor.OnButtonUp(PointerEventData.InputButton button, Vector2 screenPos)
        {
            bool ours = _pushed;
            bool order = _intentOrder;
            Pop();
            if (!ours) return;

            try
            {
                string why;
                if (!Plugin.InBattle(Singleton<IslandGameplayManager>.instance, out why)) return;

                if (order) { Plugin.RemoteOrderAt(screenPos); return; }

                ForeignUnit unit = MarqueeSelect.PickAt(screenPos);
                if (unit == null) return;

                bool squad = false;                                  // 双击同一个单位 = 选整队（v1.5.7）
                if (object.ReferenceEquals(unit, _lastUnit) && (Time.unscaledTime - _lastClickAt) <= 0.35f)
                {
                    squad = true;
                    _lastUnit = null;
                }
                else
                {
                    _lastUnit = unit;
                    _lastClickAt = Time.unscaledTime;
                }

                MarqueeSelect.SelectUnitAt(unit, MarqueeSelect.ShiftHeld(), squad);
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 点击处理异常：{0}", e));
            }
        }

        // 原版这两个也是空实现（见 §5）→ 空着等价于原版：光标贴图与悬停都不变
        void CursorManager.IPointerCursor.UpdateHoverTarget(PointerRationalizer.State state, Vector2 screenPos) { }

        void CursorManager.IPointerCursor.OverrideCursorTexture(PointerRationalizer.State state, ref Texture2D texture, ref Vector2 position) { }

        /// <summary>退出战局 / 卸载时不在 CursorManager 栈上留残账。</summary>
        internal static void ForceRelease()
        {
            if (_instance != null) _instance.Pop();
        }
    }
}
