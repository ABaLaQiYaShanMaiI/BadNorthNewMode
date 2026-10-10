using UnityEngine;

namespace BadNorthNewMode
{
    /// <summary>菜单外观：**借原版贴图**（面板底 `UISprite` / 按键 `UI_Buttons_*`）+ 官网配色（沙底 / 蓝灰 / 淡黄键 / 黑字）+ 原版艺术字；借不到就回退自绘。见 §9。</summary>
    internal static class MenuSkin
    {
        internal static GUIStyle Title, Section, Hint, Line, Row, RowOn, Btn, BtnOn;
        internal static GUIStyle HitLeft, HitCenter;                       // 只吃点击、不画字的按钮
        internal static GUIStyle Num, NumOn, NumLeft;                     // 数字专用（Times New Roman，居中/左对齐）
        internal static GUIStyle SlotName, SlotNum;                       // 头像条上的名称 / ×N（无内边距 → 绝不裁字）

        // 配色：照"仿坏北官网配色"（PDF 里量出来的：蓝灰 #89A1AD 封面 + 沙色 #D5D0C8 正文底 + 黑字 + 白卡片）
        internal static Color PanelColor = new Color(0.835f, 0.816f, 0.784f, 0.96f);   // 沙 #D5D0C8
        internal static Color BarColor = new Color(0.537f, 0.631f, 0.678f, 0.98f);     // 蓝灰 #89A1AD
        internal static Color BtnColor = new Color(0.898f, 0.788f, 0.557f, 1f);        // 按键黄（已淡化）#E5C98E
        internal static Color BtnHover = new Color(0.941f, 0.839f, 0.624f, 1f);        // 悬停略亮 #F0D69F
        internal static Color BtnOnColor = new Color(0.824f, 0.702f, 0.459f, 1f);      // 开关"开"= 略深的黄 #D2B375
        internal static Color Gold = new Color(0.306f, 0.455f, 0.533f, 1f);            // 面板上的强调（深蓝灰 #4E7488）
        internal static Color TextColor = new Color(0.102f, 0.102f, 0.102f, 1f);       // 黑字 #1A1A1A
        internal static Color DimColor = new Color(0.353f, 0.353f, 0.353f, 1f);        // 灰 #5A5A5A
        internal static Color BtnText = new Color(0.102f, 0.102f, 0.102f, 1f);         // 黄键上的黑字

        const int PanelRadius = 8;
        const int BtnRadius = 6;

        static Texture2D _panel, _bar, _row, _rowHover, _rowOn, _photo;
        static Font _numFont;
        static int _numFontSize = -1;
        static int _size = -1;
        static Font _font;
        static string _appliedColor;
        static string _appliedButton;

        internal static void Ensure()
        {
            int size = Mathf.Clamp(Util.V(ModConfig.UiFontSize, 12), 9, 20);
            Font font = VanillaUI.GameFont();
            string color = Util.V(ModConfig.UiPanelColor, null);
            string button = Util.V(ModConfig.UiButtonColor, null);

            if (Title != null && _size == size && object.ReferenceEquals(_font, font) &&
                string.Equals(_appliedColor, color, System.StringComparison.Ordinal) &&
                string.Equals(_appliedButton, button, System.StringComparison.Ordinal))
                return;

            Build(size, font, color, button);
        }

        static void Build(int size, Font font, string color, string button)
        {
            _size = size;
            _font = font;
            _appliedColor = color;
            _appliedButton = button;

            ApplyConfig(color, button);

            _panel = Round(28, PanelRadius, PanelColor, Light(PanelColor, 0.7f), 1f);
            _bar = Round(28, PanelRadius, BarColor, Light(BarColor, 0.7f), 1f);
            _row = Round(18, BtnRadius, BtnColor, Light(BtnColor, 0.8f), 1f);
            _rowHover = Round(18, BtnRadius, BtnHover, Light(BtnHover, 0.85f), 1f);
            _rowOn = Round(18, BtnRadius, BtnOnColor, Light(BtnOnColor, 0.8f), 1f);
            _photo = Round(18, BtnRadius, new Color(0.16f, 0.15f, 0.13f, 0.92f), new Color(0.36f, 0.33f, 0.28f, 1f), 1f);

            Title = Label(font, size + 1, FontStyle.Bold, TextColor, TextAnchor.MiddleLeft);
            Section = Label(font, size - 1, FontStyle.Bold, Gold, TextAnchor.MiddleLeft);
            Hint = Label(font, size - 1, FontStyle.Normal, DimColor, TextAnchor.UpperLeft);
            Line = Label(font, size, FontStyle.Normal, TextColor, TextAnchor.MiddleLeft);

            Row = Padded(Label(font, size, FontStyle.Normal, BtnText, TextAnchor.MiddleLeft));
            RowOn = Padded(Label(font, size, FontStyle.Bold, BtnText, TextAnchor.MiddleLeft));
            Btn = Padded(Label(font, size, FontStyle.Normal, BtnText, TextAnchor.MiddleCenter));
            BtnOn = Padded(Label(font, size, FontStyle.Bold, BtnText, TextAnchor.MiddleCenter));
            HitLeft = Padded(Label(font, size, FontStyle.Normal, new Color(0f, 0f, 0f, 0f), TextAnchor.MiddleLeft));
            HitCenter = Padded(Label(font, size, FontStyle.Normal, new Color(0f, 0f, 0f, 0f), TextAnchor.MiddleCenter));

            Font num = NumberFont(size, font);    // 数字改用 Times New Roman（原版数字辨识度差，作者要求）
            Num = Padded(Label(num, size, FontStyle.Bold, Gold, TextAnchor.MiddleCenter));
            NumOn = Padded(Label(num, size, FontStyle.Bold, BtnText, TextAnchor.MiddleCenter));
            NumLeft = Padded(Label(num, size, FontStyle.Bold, BtnText, TextAnchor.MiddleLeft));
            SlotName = Label(font, size + 1, FontStyle.Bold, BtnText, TextAnchor.MiddleLeft);
            SlotNum = Label(num, size + 1, FontStyle.Bold, BtnText, TextAnchor.MiddleRight);
        }

        static GUIStyle Padded(GUIStyle s)
        {
            s.padding = new RectOffset(9, 9, 2, 2);
            return s;
        }

        /// <summary>数字字体：Times New Roman（系统必有）；取不到就用界面字体。</summary>
        static Font NumberFont(int size, Font fallback)
        {
            if (_numFont != null && _numFontSize == size) return _numFont;

            _numFontSize = size;
            _numFont = null;

            try
            {
                string[] all = Font.GetOSInstalledFontNames();
                string[] want = { "Times New Roman", "TimesNewRoman", "Times", "Nimbus Roman", "Liberation Serif" };

                for (int n = 0; n < want.Length && _numFont == null; n++)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (!string.Equals(all[i], want[n], System.StringComparison.OrdinalIgnoreCase)) continue;
                        _numFont = Font.CreateDynamicFontFromOSFont(all[i], size);
                        break;
                    }
                }

                for (int i = 0; i < all.Length && _numFont == null; i++)       // 兜底：名字里带 Times 的
                {
                    if (all[i].IndexOf("Times", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    _numFont = Font.CreateDynamicFontFromOSFont(all[i], size);
                }
            }
            catch { _numFont = null; }

            if (_numFont == null)
            {
                Util.LogOnce("numfont", Loc.T("[NewMode] 找不到 Times New Roman → 数字沿用界面字体"));
                return fallback;
            }
            return _numFont;
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

        /// <summary>一行 / 一个按钮的底：**黄键**；悬停更亮；当前项 = 蓝灰 + 原版虚线焦点框。</summary>
        internal static void Bg(Rect r, bool hover, bool selected)
        {
            if (selected) { Slice(r, BarColor, false, true, _rowOn, BtnRadius, VanillaSprites.Dashed, VanillaSprites.Edge); return; }
            if (hover) { Slice(r, BtnHover, true, false, _rowHover, BtnRadius, VanillaSprites.Edge, VanillaSprites.Fill); return; }
            Slice(r, BtnColor, false, false, _row, BtnRadius, VanillaSprites.Fill, VanillaSprites.Edge);
        }

        /// <summary>头像格底（比按钮更暗，突出里面的头像）。</summary>
        internal static void PhotoBg(Rect r)
        {
            Slice(r, new Color(0.16f, 0.15f, 0.13f, 0.92f), false, false, _photo, BtnRadius, VanillaSprites.Panel, VanillaSprites.Fill);
        }

        /// <summary>头像格底（同黄键底，用原版面板板件）。</summary>
        internal static void SlotBg(Rect r, bool hover, bool selected)
        {
            if (selected) { Slice(r, BarColor, false, true, _rowOn, BtnRadius, VanillaSprites.Dashed, VanillaSprites.Panel); return; }
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

        internal static void DrawTitle(Rect r, string s) { Draw(r, s, Title); }
        internal static void DrawSection(Rect r, string s) { Draw(r, s, Section); }
        internal static void DrawHint(Rect r, string s) { Draw(r, s, Hint); }
        internal static void DrawLine(Rect r, string s) { Draw(r, s, Line); }
        internal static void DrawRow(Rect r, string s, bool selected) { Draw(r, s, selected ? RowOn : Row); }
        internal static void DrawBtn(Rect r, string s, bool on) { Draw(r, s, on ? BtnOn : Btn); }

        /// <summary>数字（Times New Roman）：`left` = 头像条那种左对齐暗字，否则居中。</summary>
        internal static void DrawNum(Rect r, string s, bool onButton, bool left)
        {
            Draw(r, s, left ? NumLeft : (onButton ? NumOn : Num));
        }

        /// <summary>数字（Times New Roman，右对齐）：头像条右上角的"×N"。</summary>
        internal static void DrawSlotNum(Rect r, string s)
        {
            Draw(r, s, SlotNum);
        }

        /// <summary>头像条上的兵种名（无内边距，按实测宽度摆放，绝不裁字）。</summary>
        internal static void DrawSlotName(Rect r, string s)
        {
            Draw(r, s, SlotName);
        }

        /// <summary>不再画描边（作者反馈：白边是看不清的主因，已整套去掉）。</summary>
        static void Draw(Rect r, string s, GUIStyle style)
        {
            if (string.IsNullOrEmpty(s) || style == null) return;
            GUI.Label(r, s, style);
        }

        static readonly GUIContent _gc = new GUIContent();     // 复用（Measure 每帧会调几十次，别每次 new）

        /// <summary>文字宽度（仅 OnGUI 内可调用）。</summary>
        internal static float Measure(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text) || style == null) return 0f;
            _gc.text = text;
            return style.CalcSize(_gc).x;
        }

        /// <summary>插件卸载时释放运行时贴图。</summary>
        internal static void Destroy()
        {
            Release(_panel); Release(_bar); Release(_row); Release(_rowHover); Release(_rowOn); Release(_photo);
            _panel = null; _bar = null; _row = null; _rowHover = null; _rowOn = null; _photo = null;
            Title = null; Section = null; Hint = null; Line = null;
            Row = null; RowOn = null; Btn = null; BtnOn = null; HitLeft = null; HitCenter = null;
            Num = null; NumOn = null; NumLeft = null; SlotName = null; SlotNum = null;
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
