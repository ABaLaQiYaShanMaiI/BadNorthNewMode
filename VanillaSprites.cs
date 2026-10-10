using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BadNorthNewMode
{
    /// <summary>运行时**借用原版 UI 贴图**：按 sprite 名从已加载资源里找，并用原版 Image 的 tint 上色；找不到就返回 null 让调用方回退自绘（见 §9）。</summary>
    internal static class VanillaSprites
    {
        // 原版 UI 图集里真实存在的名字（见 BadNorthDatabase 的 extracted_assets/Sprite）
        internal const string Fill = "UI_Buttons_Filled_NoBorder";
        internal const string Edge = "UI_Buttons_Filled_Border";
        internal const string Dashed = "UI_Buttons_Dashed_White";

        /// <summary>原版**面板底**（白/灰 9 宫格板：1~2px 深描边 + 浅填充）——大地图底部那条棕色横幅就是它 tint 成棕色。</summary>
        internal const string Panel = "UISprite";

        /// <summary>原版装饰底纹（贴在面板上做"底纹"）。</summary>
        internal const string Flair = "UI_Flair";

        // 兵种头像用的原版图标（按兵种大类挑；借不到就退回空框）
        internal const string IconInfantry = "Hero_Class_Infantry";
        internal const string IconSwords = "UI_OutlineDistanceField_CrossedSwords";

        static readonly string[] Wanted =
        {
            Fill, Edge, Dashed, Panel, Flair, IconInfantry, IconSwords,
        };

        static Sprite _btnSprite;
        static Color _btnColor = Color.white;
        static float _btnNextScan = -1f;

        static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(System.StringComparer.Ordinal);
        static readonly Dictionary<Sprite, Color> _tints = new Dictionary<Sprite, Color>();
        static float _nextScan;
        static int _scans;

        /// <summary>按名字取原版 sprite；null = 没有（调用方用自己的画法）。</summary>
        internal static Sprite Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            Sprite sp;
            if (_sprites.TryGetValue(name, out sp) && sp != null) return sp;

            if (Time.unscaledTime >= _nextScan) Scan();
            return (_sprites.TryGetValue(name, out sp) && sp != null) ? sp : null;
        }

        /// <summary>一次遍历把想要的都挑出来（原版图集按需加载，找不到就下次再扫；扫够次数就停手并报一次结果）。</summary>
        static void Scan()
        {
            _nextScan = Time.unscaledTime + 2f;
            _scans++;

            try
            {
                Object[] all = Resources.FindObjectsOfTypeAll(typeof(Sprite));
                for (int i = 0; i < all.Length; i++)
                {
                    Sprite s = all[i] as Sprite;
                    if (s == null || string.IsNullOrEmpty(s.name)) continue;

                    for (int k = 0; k < Wanted.Length; k++)
                    {
                        if (string.Equals(s.name, Wanted[k], System.StringComparison.Ordinal) && !_sprites.ContainsKey(Wanted[k]))
                        {
                            _sprites[Wanted[k]] = s;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (_scans < 6) return;

            _nextScan = float.MaxValue;                       // 收工：别为了几个图标每 2 秒遍历一次场景
            Util.LogOnce("sprites", Loc.F("[NewMode] 原版贴图借用结果：{0}/{1} 张（缺的用自绘回退）", _sprites.Count, Wanted.Length));
        }

        /// <summary>**直接借原版按钮控件**（`ButtonWidget`）的底图与配色 —— 不依赖贴图命名，比按名字找更可靠。</summary>
        internal static Sprite VanillaButton(out Color color)
        {
            if (_btnSprite == null && Time.unscaledTime >= _btnNextScan)
            {
                _btnNextScan = Time.unscaledTime + 2f;            // 找不到就过 2 秒再试（原版 UI 是逐步加载的）
                try
                {
                    Object[] all = Resources.FindObjectsOfTypeAll(typeof(ButtonWidget));
                    for (int i = 0; i < all.Length; i++)
                    {
                        ButtonWidget w = all[i] as ButtonWidget;
                        if (w == null) continue;

                        Image img = PickButtonImage(w);
                        if (img == null) continue;

                        _btnSprite = img.sprite;
                        _btnColor = img.color;
                        Util.LogOnce("btnbg", Loc.F("[NewMode] 已借用原版按钮底：{0}（色 {1}）", _btnSprite.name, _btnColor));
                        break;
                    }

                    if (_btnSprite == null && Time.unscaledTime > 20f)
                        Util.LogOnce("btnbg:miss", Loc.T("[NewMode] 没借到原版按钮底（ButtonWidget/Image 都没有 sprite）→ 用自绘圆角"));
                }
                catch { }
            }

            color = _btnColor;
            return (_btnSprite != null) ? _btnSprite : null;
        }

        static Image PickButtonImage(ButtonWidget w)
        {
            Image[] imgs = w.GetComponentsInChildren<Image>(true);
            Image best = null;
            int bestScore = -1;

            for (int i = 0; i < imgs.Length; i++)
            {
                Image img = imgs[i];
                if (img == null || img.sprite == null) continue;

                int score = 0;
                if (LooksLikeButton(img.sprite.name)) score += 4;                 // 名字像按钮底
                if (img.sprite.border != Vector4.zero) score += 2;                // 有九宫格边界 → 才是"底"
                if (img.rectTransform.rect.width >= 24f && img.rectTransform.rect.height >= 16f) score += 1;

                if (score > bestScore) { bestScore = score; best = img; }
            }

            return best;
        }

        static bool LooksLikeButton(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOf("Button", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>借原版**正在使用**这个 sprite 的 Image 的颜色（配色天然跟随原版皮肤）。</summary>
        internal static Color Tint(Sprite sp, Color fallback)
        {
            if (sp == null) return fallback;

            Color c;
            if (_tints.TryGetValue(sp, out c)) return c;

            try
            {
                Object[] all = Resources.FindObjectsOfTypeAll(typeof(Image));
                for (int i = 0; i < all.Length; i++)
                {
                    Image img = all[i] as Image;
                    if (img == null || !object.ReferenceEquals(img.sprite, sp)) continue;

                    c = img.color;
                    c.a = Mathf.Max(c.a, 0.85f);          // 原版可能压得很淡，菜单要看得清
                    _tints[sp] = c;
                    return c;
                }
            }
            catch { }

            return fallback;
        }

        /// <summary>按 `Sprite.border` 九宫格画一块（目标比边距还小时自动收窄，不会画反）。</summary>
        internal static void DrawSliced(Rect r, Sprite sp, Color tint)
        {
            if (sp == null) return;

            Texture tex = sp.texture;
            if (tex == null) return;

            Rect tr = sp.textureRect;
            if (tr.width <= 0f || tr.height <= 0f) return;

            Vector4 b = sp.border;
            Core(r, tex, tr.x, tr.y, tr.width, tr.height, b.x, b.y, b.z, b.w, tint);
        }

        /// <summary>同上，但用于**自绘**贴图（border = 生成时用的圆角半径）。</summary>
        internal static void DrawSliced(Rect r, Texture2D tex, Rect uv, float border, Color tint)
        {
            if (tex == null) return;
            Core(r, tex, uv.x, uv.y, uv.width, uv.height, border, border, border, border, tint);
        }

        // 九宫格绘制的四个切分坐标：复用静态数组（DrawSliced 每帧会调几十次，别每次 new 4 个数组）
        static readonly float[] _xs = new float[4];
        static readonly float[] _us = new float[4];
        static readonly float[] _ys = new float[4];
        static readonly float[] _vs = new float[4];

        static void Core(Rect r, Texture tex, float trX, float trY, float trW, float trH, float bl, float bb, float br, float bt, Color tint)
        {
            if (tex == null || trW <= 0f || trH <= 0f) return;

            float tw = tex.width;
            float th = tex.height;

            Color old = GUI.color;
            GUI.color = tint;

            if (bl <= 0f && bb <= 0f && br <= 0f && bt <= 0f)
            {
                Quad(r, tex, trX / tw, (trX + trW) / tw, trY / th, (trY + trH) / th);
                GUI.color = old;
                return;
            }

            float x0 = r.x, x1 = r.x + bl, x2 = r.xMax - br, x3 = r.xMax;
            float y0 = r.y, y1 = r.y + bt, y2 = r.yMax - bb, y3 = r.yMax;     // GUI 的 y 向下：bt = 上边
            if (x2 < x1) { float m = (x0 + x3) * 0.5f; x1 = m; x2 = m; }
            if (y2 < y1) { float m = (y0 + y3) * 0.5f; y1 = m; y2 = m; }

            _xs[0] = x0; _xs[1] = x1; _xs[2] = x2; _xs[3] = x3;
            _us[0] = trX / tw;
            _us[1] = (trX + bl) / tw;
            _us[2] = (trX + trW - br) / tw;
            _us[3] = (trX + trW) / tw;
            _ys[0] = y0; _ys[1] = y1; _ys[2] = y2; _ys[3] = y3;
            _vs[0] = (trY + trH) / th;                                        // GUI y 向下 → v 反向
            _vs[1] = (trY + trH - bt) / th;
            _vs[2] = (trY + bb) / th;
            _vs[3] = trY / th;

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    Rect dst = new Rect(_xs[col], _ys[row], _xs[col + 1] - _xs[col], _ys[row + 1] - _ys[row]);
                    Quad(dst, tex, _us[col], _us[col + 1], _vs[row + 1], _vs[row]);
                }
            }

            GUI.color = old;
        }

        /// <summary>把 sprite 整图等比缩放进目标矩形（居中）。</summary>
        internal static void DrawIcon(Rect r, Sprite sp, Color tint)
        {
            if (sp == null) return;

            Texture tex = sp.texture;
            if (tex == null) return;

            Rect tr = sp.textureRect;
            if (tr.width <= 0f || tr.height <= 0f) return;

            float scale = Mathf.Min(r.width / tr.width, r.height / tr.height);
            float w = tr.width * scale;
            float h = tr.height * scale;
            Rect dst = new Rect(r.x + (r.width - w) * 0.5f, r.y + (r.height - h) * 0.5f, w, h);

            Color old = GUI.color;
            GUI.color = tint;
            Quad(dst, tex, tr.x / tex.width, (tr.x + tr.width) / tex.width, tr.y / tex.height, (tr.y + tr.height) / tex.height);
            GUI.color = old;
        }

        /// <summary>头像格：按宽度铺满，**只取 sprite 上方一段**（头 / 上身）→ 小格子里也像"头像"（比整张全身像更清楚）。</summary>
        internal static void DrawPortrait(Rect box, Sprite sp, Color tint)
        {
            if (sp == null) return;

            Texture tex = sp.texture;
            if (tex == null) return;

            Rect tr = sp.textureRect;
            if (tr.width <= 0f || tr.height <= 0f) return;

            float scale = box.width / tr.width;        // 先按宽度铺满
            float need = box.height / scale;           // 目标高度需要多少 sprite 像素
            float h = Mathf.Min(need, tr.height);      // 不够高就整张画
            float top = tr.y + tr.height;              // sprite 顶部（v 越大越靠上）
            float bot = top - h;

            Rect dst = new Rect(box.x, box.y, box.width, h * scale);
            Color old = GUI.color;
            GUI.color = tint;
            Quad(dst, tex, tr.x / tex.width, (tr.x + tr.width) / tex.width, bot / tex.height, top / tex.height);
            GUI.color = old;
        }

        static void Quad(Rect dst, Texture tex, float ua, float ub, float va, float vb)
        {
            if (dst.width <= 0f || dst.height <= 0f) return;
            GUI.DrawTextureWithTexCoords(dst, tex, new Rect(ua, va, ub - ua, vb - va));
        }
    }
}
