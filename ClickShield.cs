using UnityEngine;
using UnityEngine.UI;

namespace BadNorthNewMode
{
    /// <summary>菜单 / 小队头像条上的**不可见 UI 拦截面**：EventSystem 射线先命中它 → 原版手势接收器收不到点击（IMGUI 不属于 EventSystem）。见 §5/§6 T2。</summary>
    internal static class ClickShield
    {
        static GameObject _root;
        static RawImage[] _blocks = new RawImage[3];

        /// <summary>菜单 / 头像条 / 提示框各一块（v1.6.1：提示框也能拖了，所以要挡）。</summary>
        internal static void Sync(bool menuOpen, Rect menuRect, bool barOpen, Rect barRect, bool hudOpen, Rect hudRect)
        {
            if (!Util.V(ModConfig.MenuBlocksWorldClicks, true)) { Hide(); return; }
            if (!menuOpen && !barOpen && !hudOpen) { Hide(); return; }

            Ensure();
            if (_root == null) return;                                   // 创建失败 → 静默退化为旧行为

            if (!_root.activeSelf) _root.SetActive(true);
            Place(0, menuOpen, menuRect);
            Place(1, barOpen, barRect);
            Place(2, hudOpen, hudRect);
        }

        static void Place(int index, bool on, Rect rect)
        {
            RawImage img = (index < _blocks.Length) ? _blocks[index] : null;
            if (img == null) return;

            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
            if (!on) return;

            float uiY = Screen.height - rect.y - rect.height;             // IMGUI 左上原点/y 向下 → UI 左下原点/y 向上
            RectTransform rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(rect.x, uiY);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
        }

        internal static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        internal static void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _blocks = new RawImage[3];
        }

        static void Ensure()
        {
            if (_root != null) return;

            try
            {
                _root = new GameObject("BadNorthNewMode.ClickShield");
                UnityEngine.Object.DontDestroyOnLoad(_root);             // 菜单开着跨岛/换场景时仍能复用

                Canvas canvas = _root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32767;                             // 压过游戏自己的 UI，保证射线先命中我们
                _root.AddComponent<GraphicRaycaster>();

                for (int i = 0; i < _blocks.Length; i++)
                {
                    GameObject go = new GameObject("Block" + i);
                    go.transform.SetParent(_root.transform, false);

                    RawImage img = go.AddComponent<RawImage>();
                    img.texture = Texture2D.whiteTexture;
                    img.color = new Color(0f, 0f, 0f, 0f);               // 不可见；raycastTarget 与 alpha 无关
                    img.raycastTarget = true;

                    RectTransform rt = img.rectTransform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.zero;
                    rt.pivot = Vector2.zero;

                    _blocks[i] = img;
                }
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 菜单拦截面创建失败（{0}）→ 回到旧行为", e.Message));
                _root = null;
                _blocks = new RawImage[3];
            }
        }
    }
}
