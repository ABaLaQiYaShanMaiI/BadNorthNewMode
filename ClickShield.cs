using UnityEngine;
using UnityEngine.UI;

namespace BadNorthNewMode
{
    /// <summary>菜单矩形上的不可见 UI 拦截面：EventSystem 射线先命中它 → 原版手势接收器收不到点击（IMGUI 不属于 EventSystem）。见 §5/§6 T2。</summary>
    internal static class ClickShield
    {
        static GameObject _root;
        static RawImage _block;

        internal static void Sync(bool menuOpen, Rect menuRect)
        {
            if (!menuOpen || !Util.V(ModConfig.MenuBlocksWorldClicks, true)) { Hide(); return; }

            Ensure();
            if (_root == null) return;                                   // 创建失败 → 静默退化为旧行为

            if (!_root.activeSelf) _root.SetActive(true);

            float uiY = Screen.height - menuRect.y - menuRect.height;     // IMGUI 左上原点/y 向下 → UI 左下原点/y 向上
            RectTransform rt = _block.rectTransform;
            rt.anchoredPosition = new Vector2(menuRect.x, uiY);
            rt.sizeDelta = new Vector2(menuRect.width, menuRect.height);
        }

        internal static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        internal static void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _block = null;
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

                GameObject go = new GameObject("Block");
                go.transform.SetParent(_root.transform, false);
                _block = go.AddComponent<RawImage>();
                _block.texture = Texture2D.whiteTexture;
                _block.color = new Color(0f, 0f, 0f, 0f);                 // 不可见；raycastTarget 与 alpha 无关
                _block.raycastTarget = true;

                RectTransform rt = _block.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 菜单拦截面创建失败（{0}）→ 回到旧行为", e.Message));
                _root = null;
                _block = null;
            }
        }
    }
}
