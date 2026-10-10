using UnityEngine;

namespace BadNorthNewMode
{
    /// <summary>菜单外观：**借原版贴图**（面板底 `UISprite` / 按键 `UI_Buttons_*`）+ 大地图底部那条棕色横幅的配色 + 原版艺术字（带描边）；借不到就回退自绘。见 §9。</summary>
    internal static class MenuSkin
    {
        internal static GUIStyle Title, Section, Hint, Line, Value, RowName, Row, RowOn, Btn, BtnOn;
        internal static GUIStyle HitLeft, HitCenter;                       // 只吃点击、不画字的按钮

        static GUIStyle _titleOl, _sectionOl, _hintOl, _lineOl, _valueOl, _rowNameOl, _rowOl, _rowOnOl, _btnOl, _btnOnOl;

        // 配色：坏北最标志的那套 —— 灰蓝底 + 黄键 + 黑字（面板上的说明文字用浅色 + 深描边，保证看得清）
        internal static Color PanelColor = new Color(0.24f, 0.29f, 0.35f, 0.96f);   // 灰蓝 #3D4A59
        internal static Color BarColor = new Color(0.31f, 0.37f, 0.44f, 0.98f);     // 标题栏稍亮
        internal static Color BtnColor = new Color(0.96f, 0.78f, 0.42f, 1f);        // 按键黄 #F5C76B
        internal static Color BtnHover = new Color(1f, 0.86f, 0.54f, 1f);           // 悬停更亮
        internal static Color BtnOnColor = new Color(1f, 0.90f, 0.62f, 1f);         // 选中更亮
        internal static Color Gold = new Color(1f, 0.84f, 0.55f, 1f);               // 面板上的强调（数值/分区标题）
        internal static Color TextColor = new Color(0.95f, 0.93f, 0.89f, 1f);       // 面板上的浅字
        internal static Color DimColor = new Color(0.80f, 0.80f, 0.79f, 1f);        // 面板上的次要字
        internal static Color BtnText = new Color(0.12f, 0.10f, 0.07f, 1f);        // 黄色按键上的黑字
        internal static Color OutlineColor = new Color(0.06f, 0.08f, 0.11f, 0.9f);  // 浅字的深描边

        const int PanelRadius = 8;
        const int BtnRadius = 6;

        static Texture2D _panel, _bar, _row, _rowHover, _rowOn;
        static int _size = -1;
        static Font _font;
        static string _appliedColor;
        static string _appliedButton;
        static bool _appliedOutline = true;

        internal static void Ensure()
        {
            int size = Mathf.Clamp(Util.V(ModConfig.UiFontSize, 13), 9, 20);
            Font font = VanillaUI.GameFont();
            string color = Util.V(ModConfig.UiPanelColor, null);
            string button = Util.V(ModConfig.UiButtonColor, null);
            bool outline = Util.V(ModConfig.UiTextOutline, true);

            if (Title != null && _size == size && _appliedOutline == outline && object.ReferenceEquals(_font, font) &&
                string.Equals(_appliedColor, color, System.StringComparison.Ordinal) &&
                string.Equals(_appliedButton, button, System.StringComparison.Ordinal))
                return;

            Build(size, font, color, button, outline);
        }

        static void Build(int size, Font font, string color, string button, bool outline)
        {
            _size = size;
            _font = font;
            _appliedColor = color;
            _appliedButton = button;
            _appliedOutline = outline;

            ApplyConfig(color, button);

            _panel = Round(28, PanelRadius, PanelColor, Light(PanelColor, 0.7f), 1f);
            _bar = Round(28, PanelRadius, BarColor, Light(BarColor, 0.7f), 1f);
            _row = Round(18, BtnRadius, BtnColor, Light(BtnColor, 0.8f), 1f);
            _rowHover = Round(18, BtnRadius, BtnHover, Light(BtnHover, 0.85f), 1f);
            _rowOn = Round(18, BtnRadius, BtnOnColor, Light(BtnOnColor, 0.8f), 1f);

            Title = Label(font, size + 1, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft);
            Section = Label(font, size - 1, FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
            Hint = Label(font, size - 1, FontStyle.Normal, DimColor, TextAnchor.UpperLeft);
            Line = Label(font, size, FontStyle.Normal, TextColor, TextAnchor.MiddleLeft);
            Value = Label(font, size, FontStyle.Bold, Gold, TextAnchor.MiddleCenter);
            RowName = Label(font, size - 1, FontStyle.Normal, new Color(0.14f, 0.11f, 0.07f, 0.8f), TextAnchor.MiddleRight);   // 黄键上的暗字

            Row = Padded(Label(font, size, FontStyle.Normal, BtnText, TextAnchor.MiddleLeft));
            RowOn = Padded(Label(font, size, FontStyle.Bold, BtnText, TextAnchor.MiddleLeft));
            Btn = Padded(Label(font, size, FontStyle.Normal, BtnText, TextAnchor.MiddleCenter));
            BtnOn = Padded(Label(font, size, FontStyle.Bold, BtnText, TextAnchor.MiddleCenter));
            HitLeft = Padded(Label(font, size, FontStyle.Normal, new Color(0f, 0f, 0f, 0f), TextAnchor.MiddleLeft));
            HitCenter = Padded(Label(font, size, FontStyle.Normal, new Color(0f, 0f, 0f, 0f), TextAnchor.MiddleCenter));

            _titleOl = outline ? Label(font, size + 1, FontStyle.Bold, OutlineColor, TextAnchor.MiddleLeft) : null;
            _sectionOl = outline ? Label(font, size - 1, FontStyle.Bold, OutlineColor, TextAnchor.MiddleLeft) : null;
            _hintOl = outline ? Label(font, size - 1, FontStyle.Normal, OutlineColor, TextAnchor.UpperLeft) : null;
            _lineOl = outline ? Label(font, size, FontStyle.Normal, OutlineColor, TextAnchor.MiddleLeft) : null;
            _valueOl = outline ? Label(font, size, FontStyle.Bold, OutlineColor, TextAnchor.MiddleCenter) : null;
            _rowNameOl = outline ? Label(font, size - 1, FontStyle.Normal, OutlineColor, TextAnchor.MiddleRight) : null;
            _rowOl = null;                       // 黄色按键上的黑字本来就够清楚，不需要描边
            _rowOnOl = null;
            _btnOl = null;
            _btnOnOl = null;
        }

        static GUIStyle Padded(GUIStyle s)
        {
            s.padding = new RectOffset(9, 9, 2, 2);
            return s;
        }

        static void ApplyConfig(string panelHex, string btnHex)
        {
            Color panel = Color.clear;
            if (TryHex(panelHex, ref panel))
            {
                PanelColor = panel;
                BarColor = Light(panel, 1.22f);
            }

            Color btn = Color.clear;
            if (TryHex(btnHex, ref btn))
            {
                BtnColor = btn;
                BtnHover = Light(btn, 1.10f);
                BtnOnColor = Light(btn, 1.18f);
            }
        }

        static Color Light(Color c, float k)
        {
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        /// <summary>解析 `RRGGBB` / `RRGGBBAA`（可带 #）。</summary>
        static bool TryHex(string s, ref Color color)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string h = s.Trim().TrimStart('#');
            if (h.Length != 6 && h.Length != 8) return false;

            int r, g, b, a = 255;
            if (!int.TryParse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)) return false;
            if (!int.TryParse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)) return false;
            if (!int.TryParse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b)) return false;
            if (h.Length == 8 && !int.TryParse(h.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out a)) return false;

            color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            return true;
        }

        // ---------- 底图：原版贴图优先 ----------

        internal static void PanelBg(Rect r) { Slice(r, PanelColor, false, false, _panel, PanelRadius, VanillaSprites.Panel, VanillaSprites.Fill); Pattern(r, 0.10f); }

        internal static void BarBg(Rect r) { Slice(r, BarColor, false, false, _bar, PanelRadius, VanillaSprites.Panel, VanillaSprites.Edge); Pattern(r, 0.10f); }

        /// <summary>面板上的原版**底纹**（借 `UI_Flair` 平铺，很淡）——大地图那条横幅的"布纹"观感。</summary>
        static void Pattern(Rect r, float alpha)
        {
            Sprite sp = VanillaSprites.Get(VanillaSprites.Flair);
            if (sp == null) return;

            Texture tex = sp.texture;
            if (tex == null) return;

            Rect tr = sp.textureRect;
            if (tr.width <= 0f || tr.height <= 0f) return;

            Rect area = new Rect(r.x + 6f, r.y + 6f, r.width - 12f, r.height - 12f);
            if (area.width <= 8f || area.height <= 8f) return;

            Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            const float Tile = 96f;

            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            for (float y = area.y; y < area.yMax; y += Tile)
            {
                for (float x = area.x; x < area.xMax; x += Tile)
                {
                    float w = Mathf.Min(Tile, area.xMax - x);
                    float h = Mathf.Min(Tile, area.yMax - y);
                    GUI.DrawTextureWithTexCoords(new Rect(x, y, w, h), tex, new Rect(uv.x, uv.y, uv.width * (w / Tile), uv.height * (h / Tile)));
                }
            }
            GUI.color = old;
        }

        /// <summary>一行 / 一个按钮的底：**黄键**（悬停更亮、当前项用原版虚线焦点框）。</summary>
        internal static void Bg(Rect r, bool hover, bool selected)
        {
            if (selected) { Slice(r, BtnOnColor, false, true, _rowOn, BtnRadius, VanillaSprites.Dashed, VanillaSprites.Edge); return; }
            if (hover) { Slice(r, BtnHover, true, false, _rowHover, BtnRadius, VanillaSprites.Edge, VanillaSprites.Fill); return; }
            Slice(r, BtnColor, false, false, _row, BtnRadius, VanillaSprites.Fill, VanillaSprites.Edge);
        }

        /// <summary>头像格底（同黄键底，用原版面板板件）。</summary>
        internal static void SlotBg(Rect r, bool hover, bool selected)
        {
            if (selected) { Slice(r, BtnOnColor, false, true, _rowOn, BtnRadius, VanillaSprites.Dashed, VanillaSprites.Panel); return; }
            if (hover) { Slice(r, BtnHover, true, false, _rowHover, BtnRadius, VanillaSprites.Edge, VanillaSprites.Panel); return; }
            Slice(r, BtnColor, false, false, _row, BtnRadius, VanillaSprites.Fill, VanillaSprites.Panel);
        }

        static void Slice(Rect r, Color fallback, bool hover, bool selected, Texture2D own, int radius, string vanilla, string alt)
        {
            Sprite sp = VanillaSprites.Get(vanilla);
            if (sp == null) sp = VanillaSprites.Get(alt);
            Color tint = fallback;

            if (sp != null)
            {
                tint = VanillaSprites.Tint(sp, fallback);
            }
            else
            {
                Color c;
                sp = VanillaSprites.VanillaButton(out c);
                if (sp != null) tint = c;
            }

            if (sp != null)
            {
                VanillaSprites.DrawSliced(r, sp, Mood(TintBy(tint, fallback), hover, selected));
                return;
            }
            VanillaSprites.DrawSliced(r, own, new Rect(0f, 0f, 1f, 1f), radius, Mood(fallback, hover, selected));
        }

        /// <summary>借来的原版贴图偏白：用我们的棕色去"染"它（越白越接近目标色；原版自己的 tint 只作亮度调制）。</summary>
        static Color TintBy(Color got, Color want)
        {
            return new Color(want.r * (0.75f + got.r * 0.25f),
                             want.g * (0.75f + got.g * 0.25f),
                             want.b * (0.75f + got.b * 0.25f),
                             Mathf.Max(got.a, want.a));
        }

        /// <summary>悬停更亮、选中偏金（借来的原版色与自绘色走同一套）。</summary>
        static Color Mood(Color c, bool hover, bool selected)
        {
            if (hover) c = Light(c, 1.25f);
            if (selected) c = Color.Lerp(c, Gold, 0.35f);
            c.a = Mathf.Max(c.a, 0.85f);
            return c;
        }

        // ---------- 文字：原版那种"亮字 + 深描边" ----------

        internal static void DrawTitle(Rect r, string s) { Draw(r, s, Title, _titleOl); }
        internal static void DrawSection(Rect r, string s) { Draw(r, s, Section, _sectionOl); }
        internal static void DrawHint(Rect r, string s) { Draw(r, s, Hint, _hintOl); }
        internal static void DrawLine(Rect r, string s) { Draw(r, s, Line, _lineOl); }
        internal static void DrawValue(Rect r, string s) { Draw(r, s, Value, _valueOl); }
        internal static void DrawRowName(Rect r, string s) { Draw(r, s, RowName, _rowNameOl); }
        internal static void DrawRow(Rect r, string s, bool selected) { Draw(r, s, selected ? RowOn : Row, selected ? _rowOnOl : _rowOl); }
        internal static void DrawBtn(Rect r, string s, bool on) { Draw(r, s, on ? BtnOn : Btn, on ? _btnOnOl : _btnOl); }

        static void Draw(Rect r, string s, GUIStyle style, GUIStyle outline)
        {
            if (string.IsNullOrEmpty(s) || style == null) return;

            if (outline != null)
            {
                GUI.Label(new Rect(r.x - 1f, r.y, r.width, r.height), s, outline);      // 四向描边（原版 UI 的文字是描边的）
                GUI.Label(new Rect(r.x + 1f, r.y, r.width, r.height), s, outline);
                GUI.Label(new Rect(r.x, r.y - 1f, r.width, r.height), s, outline);
                GUI.Label(new Rect(r.x, r.y + 1f, r.width, r.height), s, outline);
            }

            GUI.Label(r, s, style);
        }

        /// <summary>文字宽度（仅 OnGUI 内可调用）。</summary>
        internal static float Measure(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text) || style == null) return 0f;
            return style.CalcSize(new GUIContent(text)).x;
        }

        /// <summary>插件卸载时释放运行时贴图。</summary>
        internal static void Destroy()
        {
            Release(_panel); Release(_bar); Release(_row); Release(_rowHover); Release(_rowOn);
            _panel = null; _bar = null; _row = null; _rowHover = null; _rowOn = null;
            Title = null; Section = null; Hint = null; Line = null; Value = null; RowName = null;
            Row = null; RowOn = null; Btn = null; BtnOn = null; HitLeft = null; HitCenter = null;
            _titleOl = null; _sectionOl = null; _hintOl = null; _lineOl = null; _valueOl = null;
            _rowNameOl = null; _rowOl = null; _rowOnOl = null; _btnOl = null; _btnOnOl = null;
            _size = -1;
            _font = null;
        }

        static void Release(Texture2D t)
        {
            if (t != null) UnityEngine.Object.Destroy(t);
        }

        static GUIStyle Label(Font font, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            GUIStyle s = new GUIStyle();
            s.font = font;
            s.fontSize = size;
            s.fontStyle = style;
            s.alignment = anchor;
            s.normal.textColor = color;
            s.hover.textColor = color;
            s.active.textColor = color;
            s.richText = false;
            s.wordWrap = false;
            s.clipping = TextClipping.Clip;
            return s;
        }

        /// <summary>生成圆角矩形贴图（借不到原版贴图时的回退）：`radius` 同时用作九宫格边距，带 1px 抗锯齿。</summary>
        static Texture2D Round(int size, int radius, Color fill, Color border, float borderWidth)
        {
            Texture2D t = new Texture2D(size, size, TextureFormat.ARGB32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.hideFlags = HideFlags.HideAndDontSave;

            Color[] px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Dist(x + 0.5f, y + 0.5f, size, radius);
                    Color c = (borderWidth > 0f && d >= -borderWidth) ? border : fill;
                    c.a *= Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = c;
                }
            }

            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static float Dist(float x, float y, float size, float radius)
        {
            float cx = Mathf.Clamp(x, radius, size - radius);
            float cy = Mathf.Clamp(y, radius, size - radius);
            float dx = x - cx;
            float dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy) - radius;
        }
    }
}
