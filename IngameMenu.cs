using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>局内 HUD + 投放菜单（IMGUI 自绘，零资源）。菜单即"投放模式"：输入由 Plugin 负责，本类只管显示与选择。</summary>
    internal static class IngameMenu
    {
        internal static bool IsOpen;
        internal static string Hover = "";

        /// <summary>菜单屏幕矩形（IMGUI 坐标：左上原点）——ClickShield 按它摆位，拖动标题栏会一起改。</summary>
        internal static Rect MenuRect { get { return _rect; } }

        // ---- 布局（IMGUI 坐标：左上原点；高度由 MenuHeight 统一算，改这里必须同步改那里）----
        const float Pad = 10f;
        const float Gap = 6f;
        const float TitleH = 26f;
        const float RowH = 24f;
        const float RowGap = 3f;
        const float BtnH = 24f;
        const float LineH = 18f;
        const float InfoH = 18f;
        const float HelpH = 17f;
        const int CountMin = 1;
        const int CountMax = 24;

        /// <summary>超过这个长度就别发通知条了（它只有一行，长句会被裁）→ 留给 HUD。</summary>
        const int ToastMaxChars = 34;
        static readonly int[] Presets = { 1, 2, 3, 4, 6, 8, 10, 12 };

        static string _hud = "";
        static float _hudUntil;
        static bool _hudMirror;                       // 原版通知条没接住时，才在 HUD 里重复一遍
        static Rect _rect;
        static Rect _titleRect;
        static Island _island;
        static List<VikingReference> _units;
        static Vector2 _pos = new Vector2(8f, 8f);    // 面板左上角（拖动标题栏可改）
        static bool _dragging;
        static Vector2 _dragGrab;
        static Vector2 _hudPos = new Vector2(-1f, -1f);   // 提示框位置（-1 = 自动：菜单下方 / 左上角）
        static Rect _hudRect;
        static bool _hudDrawn;
        static bool _hudDrag;
        static Vector2 _hudGrab;
        static string _tip = "";                      // 兵种信息行（悬停优先，否则看已选兵种）

        internal static void Toggle()
        {
            IsOpen = !IsOpen;
            Hover = "";
            PlacementMarker.Get().Hide();
            if (!IsOpen) { ClickShield.Hide(); _dragging = false; }
            Say(IsOpen ? Loc.T("投放菜单已打开：点兵种 → 点滩头陆地投放") : Loc.T("已关闭投放菜单"));
            Util.Log("[NewMode] " + _hud);
        }

        internal static void Close()
        {
            IsOpen = false;
            Hover = "";
            PlacementMarker.Get().Hide();
            ClickShield.Hide();
            _dragging = false;
        }

        /// <summary>状态提示：**短消息**走原版通知条（v1.6.1，自带音效与淡入淡出）；太长（通知条一行放不下）或原版不可用就留在 HUD 里。</summary>
        internal static void Say(string msg)
        {
            _hud = msg;
            _hudUntil = Time.time + 5f;
            _hudMirror = string.IsNullOrEmpty(msg) || (msg.Length > ToastMaxChars) || !VanillaUI.Toast(msg, 4f);
        }

        /// <summary>菜单是否遮挡该屏幕坐标（IMGUI 的 y 轴自上而下，需翻转）。</summary>
        internal static bool Contains(Vector2 screenPos)
        {
            if (!IsOpen) return false;
            return _rect.Contains(Gui(screenPos));
        }

        /// <summary>提示框（HUD）矩形 —— v1.6.1 起也可拖动。</summary>
        internal static Rect HudRect { get { return _hudRect; } }

        /// <summary>上一帧提示框是否显示（ClickShield 与点击让位用）。</summary>
        internal static bool HudVisible { get { return _hudDrawn; } }

        internal static bool HudContains(Vector2 screenPos)
        {
            if (!_hudDrawn) return false;
            return _hudRect.Contains(Gui(screenPos));
        }

        /// <summary>菜单 **或** 提示框上（世界点击都要让位）。</summary>
        internal static bool AnyPanelContains(Vector2 screenPos)
        {
            return Contains(screenPos) || HudContains(screenPos);
        }

        /// <summary>屏幕坐标 → IMGUI 坐标。</summary>
        static Vector2 Gui(Vector2 screenPos)
        {
            return new Vector2(screenPos.x, Screen.height - screenPos.y);
        }

        /// <summary>拖动（Plugin.Update 每帧调用；菜单标题栏 + 提示框整块，都用上一帧的矩形）。</summary>
        internal static void HandleDrag()
        {
            Vector2 m = Mouse();

            if (!IsOpen || VanillaUI.ModalShowing)
            {
                _dragging = false;
            }
            else
            {
                if (Input.GetMouseButtonDown(0) && _titleRect.Contains(m))
                {
                    _dragging = true;
                    _dragGrab = m - new Vector2(_rect.x, _rect.y);
                }
                if (!Input.GetMouseButton(0)) _dragging = false;
                else if (_dragging) _pos = m - _dragGrab;
            }

            // 提示框：整块都能拖（作者要求，v1.6.1）
            if (VanillaUI.ModalShowing) { _hudDrag = false; return; }

            if (Input.GetMouseButtonDown(0) && HudVisible && _hudRect.Contains(m))
            {
                _hudDrag = true;
                _hudGrab = m - new Vector2(_hudRect.x, _hudRect.y);
            }
            if (!Input.GetMouseButton(0)) _hudDrag = false;
            else if (_hudDrag) _hudPos = m - _hudGrab;
        }

        /// <summary>鼠标位置（IMGUI 坐标）。</summary>
        static Vector2 Mouse()
        {
            return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        }

        /// <summary>当前 IMGUI 事件里鼠标是否落在该矩形内（只在 OnGUI 内可调用）。</summary>
        static bool IsHover(Rect r)
        {
            Event e = Event.current;
            return (e != null) && r.Contains(e.mousePosition);
        }

        internal static void Draw()
        {
            if (VanillaUI.ModalShowing) return;      // 原版确认框在前台：我们的 IMGUI 让位（这时输入也已停）
            MenuSkin.Ensure();
            DrawHud();
            if (IsOpen) DrawMenu();
        }

        static void DrawHud()
        {
            _hudDrawn = false;
            if (!Util.V(ModConfig.ShowHud, true)) return;

            int foreign = ForeignUnit.SelectableCount(false);
            int nativeCount = ForeignUnit.SelectableCount(true);
            int selected = (MarqueeSelect.Pending != null) ? MarqueeSelect.Pending.Count : 0;
            bool active = (foreign > 0) || (nativeCount > 0) || RemoteGroup.Any;
            if (!IsOpen && !active && !MarqueeSelect.Dragging && Time.time > _hudUntil) return;

            List<string> lines = new List<string>();
            lines.Add((IsOpen ? Loc.T("投放菜单") : "BadNorthNewMode") + " · v" + Plugin.VERSION);
            if (IsOpen && !string.IsNullOrEmpty(Hover)) lines.Add(Hover);
            if (RemoteGroup.Any) lines.Add(Loc.F("遥控小队：{0}（共 {1}）", RemoteGroup.DescribeAll(), RemoteGroup.TotalCount));
            if (RemoteGroup.HasRally) lines.Add(Loc.F("待登陆集结：{0} 人 / {1} 处（落地后自动前往）", RemoteGroup.RallyMemberCount(), RemoteGroup.RallyCount));
            if (foreign > 0) lines.Add(Loc.F("非原生单位 {0}{1}", foreign, (selected > 0) ? (Loc.T(", 已选中 ") + selected) : ""));
            if (nativeCount > 0) lines.Add(Loc.F("原生单位 {0}（可遥控）", nativeCount));
            if (LevelTools.CustomMode) lines.Add(Loc.T("无尽自定义模式：本关不会自然结束 —— 按 F3 强制胜利退出"));
            if (MarqueeSelect.Dragging) lines.Add(Loc.F("框选中…（按兵种自动分队，每队上限 {0}）", Util.V(ModConfig.RemoteSoftCap, 40)));
            if (_hudMirror && !string.IsNullOrEmpty(_hud) && Time.time <= _hudUntil) lines.Add(_hud);

            float w = 0f;
            for (int i = 0; i < lines.Count; i++)
                w = Mathf.Max(w, MenuSkin.Measure(lines[i], (i == 0) ? MenuSkin.Title : MenuSkin.Line));
            w = Mathf.Min(w + Pad * 2f + 10f, Screen.width - 16f);

            float h = Pad * 2f + lines.Count * LineH;
            float y = IsOpen ? (_rect.yMax + Gap) : 8f;     // 菜单开着 → HUD 排在面板下方，不再互相压住
            Vector2 pos = (_hudPos.x >= 0f) ? _hudPos : new Vector2(8f, y);   // 拖过就用玩家放的位置
            pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, Screen.width - w));
            pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, Screen.height - h));

            Rect r = new Rect(pos.x, pos.y, w, h);
            _hudRect = r;
            _hudDrawn = true;
            MenuSkin.PanelBg(r);

            Color old = GUI.color;
            GUI.color = new Color(0.45f, 1f, 1f, 0.85f);    // 左侧强调条
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 4f, 2f, r.height - 8f), Texture2D.whiteTexture);
            GUI.color = old;

            for (int i = 0; i < lines.Count; i++)
            {
                Rect lr = new Rect(r.x + Pad, r.y + Pad + i * LineH - 2f, r.width - Pad * 2f, LineH);
                if (i == 0) MenuSkin.DrawTitle(lr, lines[i]);
                else MenuSkin.DrawLine(lr, lines[i]);
            }
        }

        static void DrawMenu()
        {
            int n = (_units != null) ? _units.Count : 0;
            float w = MenuWidth(n);
            float h = MenuHeight(n);
            _pos.x = Mathf.Clamp(_pos.x, 0f, Mathf.Max(0f, Screen.width - w));
            _pos.y = Mathf.Clamp(_pos.y, 0f, Mathf.Max(0f, Screen.height - h));

            _rect = new Rect(_pos.x, _pos.y, w, h);
            _titleRect = new Rect(_rect.x, _rect.y, w, TitleH);
            MenuSkin.PanelBg(_rect);
            MenuSkin.BarBg(_titleRect);

            float x = _rect.x + Pad;
            float inner = w - Pad * 2f;
            float y = _rect.y + TitleH + Pad;

            DrawTitleBar(x, inner);

            // ---- 兵种 ----
            MenuSkin.DrawSection(new Rect(x, y, inner, LineH), Loc.T("兵种"));
            y += LineH;

            if (n == 0)
            {
                MenuSkin.DrawLine(new Rect(x, y, inner, RowH), Loc.T("（进入战局后才会列出可用兵种）"));
                _tip = "";
                y += RowH;
            }
            else
            {
                string cur = Util.V(ModConfig.EnemyName, "");
                VikingReference hover = null;
                for (int i = 0; i < n; i++)
                {
                    VikingReference u = _units[i];
                    if (u == null) continue;

                    bool sel = !string.IsNullOrEmpty(cur) && string.Equals(u.name, cur, System.StringComparison.OrdinalIgnoreCase);
                    Rect r = new Rect(x, y, inner, RowH - RowGap);
                    bool hov = IsHover(r);
                    MenuSkin.Bg(r, hov, sel);
                    string label = Loc.F("{0}{1}. {2}", sel ? "▶ " : "  ", i + 1, UnitNames.Of(u.name));
                    if (GUI.Button(r, GUIContent.none, MenuSkin.HitLeft))
                    {
                        VanillaUI.Click();
                        SelectUnit(u);
                    }
                    MenuSkin.DrawRow(r, label, sel);
                    MenuSkin.DrawRowName(new Rect(r.xMax - 200f, r.y, 196f, r.height), u.name);   // 行尾内部名
                    if (hov) hover = u;
                    y += RowH;
                }

                _tip = DescribeUnit((hover != null) ? hover : CurrentUnit());
            }

            y = DrawCountRow(x, inner, y);
            y = DrawTakeoverRow(x, inner, y);
            DrawActions(x, inner, y);
        }

        /// <summary>标题栏：标题 + 版本 + 语言 + 关闭（标题栏同时是拖动手柄）。</summary>
        static void DrawTitleBar(float x, float inner)
        {
            float by = _rect.y + 2f;
            float bh = TitleH - 4f;

            MenuSkin.DrawTitle(new Rect(x, by, inner - 220f, bh), Loc.T("投放菜单") + "  v" + Plugin.VERSION);

            bool zh = !Loc.IsEnglish;
            DrawLangButton(new Rect(_rect.xMax - Pad - 178f, by, 52f, bh), "中文", zh, "zh");
            DrawLangButton(new Rect(_rect.xMax - Pad - 122f, by, 64f, bh), "English", !zh, "en");

            Rect close = new Rect(_rect.xMax - Pad - 52f, by, 52f, bh);
            MenuSkin.Bg(close, IsHover(close), false);
            if (GUI.Button(close, GUIContent.none, MenuSkin.HitCenter))
            {
                VanillaUI.Click();
                Close();
                Say(Loc.T("已关闭投放菜单"));
            }
            MenuSkin.DrawBtn(close, Loc.T("关闭"), false);
        }

        /// <summary>数量行（− / + 与预设）+ 兵种信息行。</summary>
        static float DrawCountRow(float x, float inner, float y)
        {
            int curSize = Util.V(ModConfig.SquadSize, 0);
            int shown = (curSize > 0) ? curSize : AutoCount();
            float cx = x;

            MenuSkin.DrawSection(new Rect(cx, y, 44f, BtnH), Loc.T("数量"));
            cx += 46f;
            Rect minus = new Rect(cx, y, 28f, BtnH);
            MenuSkin.Bg(minus, IsHover(minus), false);
            if (GUI.Button(minus, GUIContent.none, MenuSkin.HitCenter)) { VanillaUI.Click(); StepCount(-1); }
            MenuSkin.DrawBtn(minus, "-", false);
            cx += 32f;
            MenuSkin.DrawValue(new Rect(cx, y, 42f, BtnH), (shown > 0) ? shown.ToString() : "-");
            cx += 46f;
            Rect plus = new Rect(cx, y, 28f, BtnH);
            MenuSkin.Bg(plus, IsHover(plus), false);
            if (GUI.Button(plus, GUIContent.none, MenuSkin.HitCenter)) { VanillaUI.Click(); StepCount(1); }
            MenuSkin.DrawBtn(plus, "+", false);
            cx += 36f;

            for (int i = 0; i < Presets.Length; i++)
            {
                int v = Presets[i];
                bool on = (curSize == v);
                Rect pr = new Rect(cx + i * 38f, y, 34f, BtnH);
                MenuSkin.Bg(pr, IsHover(pr), on);
                string text = v.ToString();
                if (GUI.Button(pr, GUIContent.none, MenuSkin.HitCenter))
                {
                    VanillaUI.Click();
                    SelectCount(v);
                }
                MenuSkin.DrawBtn(pr, text, on);
            }

            y += BtnH;
            MenuSkin.DrawHint(new Rect(x, y, inner, InfoH), _tip);
            return y + InfoH;
        }

        /// <summary>接管开关两枚 + 说明行。</summary>
        static float DrawTakeoverRow(float x, float inner, float y)
        {
            y += Pad;
            float tw = (inner - Gap) * 0.5f;
            DrawToggle(new Rect(x, y, tw, BtnH), ModConfig.BlockVanillaWaves, Loc.T("原版波次：拦下"), Loc.T("原版波次：正常"));
            DrawToggle(new Rect(x + tw + Gap, y, tw, BtnH), ModConfig.ControlNativeUnits, Loc.T("原生单位：可遥控"), Loc.T("原生单位：不可"));

            y += BtnH;
            MenuSkin.DrawHint(new Rect(x, y, inner, LineH), Loc.T("拦下 = 本关无原版敌人（无尽，F3 退出）；可遥控 = 原生敌人也能指挥"));
            return y + LineH;
        }

        /// <summary>三个常用操作（v1.6.1 起清场/强制胜利也能从菜单点，不必记快捷键）。</summary>
        static void DrawActions(float x, float inner, float y)
        {
            y += Pad;
            float aw = (inner - Gap * 2f) / 3f;

            Rect rel = new Rect(x, y, aw, BtnH);
            MenuSkin.Bg(rel, IsHover(rel), false);
            if (GUI.Button(rel, GUIContent.none, MenuSkin.HitCenter)) { VanillaUI.Click(); Plugin.ReleaseRemoteControl(); }
            MenuSkin.DrawBtn(rel, Loc.T("一键释放遥控"), false);

            Rect clr = new Rect(x + aw + Gap, y, aw, BtnH);
            MenuSkin.Bg(clr, IsHover(clr), false);
            if (GUI.Button(clr, GUIContent.none, MenuSkin.HitCenter)) { VanillaUI.Click(); SpawnLedger.RequestCleanup(); }
            MenuSkin.DrawBtn(clr, Loc.T("清场（F2）"), false);

            Rect win = new Rect(x + (aw + Gap) * 2f, y, aw, BtnH);
            MenuSkin.Bg(win, IsHover(win), false);
            if (GUI.Button(win, GUIContent.none, MenuSkin.HitCenter)) { VanillaUI.Click(); LevelTools.RequestForceWin(); }
            MenuSkin.DrawBtn(win, Loc.T("强制胜利（F3）"), false);
            y += BtnH + Pad;

            string[] help = HelpTexts();
            for (int i = 0; i < help.Length; i++)
                MenuSkin.DrawHint(new Rect(x, y + i * HelpH, inner, HelpH), help[i]);
        }

        static string[] HelpTexts()
        {
            return new string[]
            {
                MarqueeSelect.SingleButtonMode()
                    ? Loc.T("单键：点单位 = 选中｜双击 = 整队｜有选中时点地块 = 前进")
                    : Loc.T("双键：左键点单位 = 选中｜双击 = 整队｜右键点地块 = 前进"),
                Loc.T("Shift + 点 = 并入｜R = 全选｜Alt + 拖动 = 框选"),
                Loc.T("拖动 = 平移相机｜船上也能选（落地自动去集结点）｜F2 清场 · F3 强制胜利"),
            };
        }

        /// <summary>面板高度（必须与 DrawMenu 的推进顺序一一对应）。</summary>
        static float MenuHeight(int n)
        {
            float rows = Mathf.Max(1, n) * RowH;
            return TitleH + Pad + LineH + rows
                 + Pad + BtnH + InfoH
                 + Pad + BtnH + LineH
                 + Pad + BtnH
                 + Pad + HelpTexts().Length * HelpH
                 + Pad + 2f;
        }

        /// <summary>面板宽度：按当前语言与兵种名实测后夹到屏幕内（中英都不写死宽度）。</summary>
        static float MenuWidth(int n)
        {
            float w = 524f;                                  // 数量行（8 个预设）的下限
            w = Mathf.Max(w, MenuSkin.Measure(Loc.T("投放菜单") + "  v" + Plugin.VERSION, MenuSkin.Title) + 240f);
            w = Mathf.Max(w, MenuSkin.Measure(Loc.T("一键释放遥控"), MenuSkin.Btn) * 3f + 60f);
            w = Mathf.Max(w, MenuSkin.Measure(Loc.T("拦下 = 本关无原版敌人（无尽，F3 退出）；可遥控 = 原生敌人也能指挥"), MenuSkin.Hint));

            string[] help = HelpTexts();
            for (int i = 0; i < help.Length; i++) w = Mathf.Max(w, MenuSkin.Measure(help[i], MenuSkin.Hint));

            string cur = Util.V(ModConfig.EnemyName, "");
            for (int i = 0; i < n; i++)
            {
                VikingReference u = _units[i];
                if (u == null) continue;

                bool sel = !string.IsNullOrEmpty(cur) && string.Equals(u.name, cur, System.StringComparison.OrdinalIgnoreCase);
                float row = MenuSkin.Measure(Loc.F("{0}{1}. {2}", sel ? "▶ " : "  ", i + 1, UnitNames.Of(u.name)), MenuSkin.Row)
                          + MenuSkin.Measure(u.name, MenuSkin.RowName) + 40f;
                w = Mathf.Max(w, row);
            }

            return Mathf.Clamp(w + Pad * 2f + 8f, 420f, Mathf.Min(860f, Screen.width * 0.72f));
        }

        /// <summary>信息行：默认人数 / 上限 / 该人数配哪艘船（悬停优先，没悬停就看已选兵种）。</summary>
        static string DescribeUnit(VikingReference u)
        {
            if (u == null) return "";

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (_island != null) ? _island : ((gm != null) ? gm.island : null);

            int def = UnitNames.DefaultCount(u.name);
            if (def <= 0) def = UnitCatalog.DefaultSquadSize(island, u);

            int want = Util.V(ModConfig.SquadSize, 0);
            if (want <= 0) want = def;

            Longship ship = UnitCatalog.PickShipForCount(island, u, Mathf.Max(1, want));
            return Loc.F("默认 {0} 人 · 上限 {1} 人 · 船：{2}",
                def, UnitCatalog.MaxSquadSize(island, u), (ship != null) ? ship.name : Loc.T("无"));
        }

        static VikingReference CurrentUnit()
        {
            if (_units == null) return null;

            string cur = Util.V(ModConfig.EnemyName, "");
            if (string.IsNullOrEmpty(cur)) return null;

            for (int i = 0; i < _units.Count; i++)
            {
                VikingReference u = _units[i];
                if (u != null && string.Equals(u.name, cur, System.StringComparison.OrdinalIgnoreCase)) return u;
            }
            return null;
        }

        /// <summary>cfg 里数量为 0（按兵种梯度）时，该兵种实际会装多少人。</summary>
        static int AutoCount()
        {
            VikingReference u = CurrentUnit();
            if (u == null) return 0;

            int def = UnitNames.DefaultCount(u.name);
            if (def > 0) return def;
            return UnitCatalog.DefaultSquadSize(_island, u);
        }

        /// <summary>数量微调：没手动设过（cfg = 0）时从该兵种的默认数起步。</summary>
        static void StepCount(int delta)
        {
            int cur = Util.V(ModConfig.SquadSize, 0);
            if (cur <= 0) { cur = AutoCount(); if (cur <= 0) cur = CountMin; }
            SelectCount(Mathf.Clamp(cur + delta, CountMin, CountMax));
        }

        static void DrawToggle(Rect r, ConfigEntry<bool> entry, string onLabel, string offLabel)
        {
            if (entry == null) return;

            bool on = entry.Value;
            MenuSkin.Bg(r, IsHover(r), on);
            if (GUI.Button(r, GUIContent.none, MenuSkin.HitCenter))
            {
                VanillaUI.Click();
                entry.Value = !on;

                string now = on ? offLabel : onLabel;
                Say(Loc.F("已切换：{0}", now));
                Util.Log("[NewMode] " + now);
            }
            MenuSkin.DrawBtn(r, on ? onLabel : offLabel, on);
        }

        static void DrawLangButton(Rect r, string label, bool active, string mode)
        {
            MenuSkin.Bg(r, IsHover(r), active);
            if (GUI.Button(r, GUIContent.none, MenuSkin.HitCenter))
            {
                VanillaUI.Click();
                SelectLanguage(mode);
            }
            MenuSkin.DrawBtn(r, label, active);
        }

        /// <summary>切换语言：写回 cfg（自动保存），下次启动/下一帧均生效。</summary>
        static void SelectLanguage(string mode)
        {
            if (!Loc.SetLanguage(mode)) return;

            Say(Loc.T("语言已切换（立即生效）"));
            Util.Log("[NewMode] Language = " + mode);
        }

        static void SelectUnit(VikingReference unit)
        {
            if (unit == null || ModConfig.EnemyName == null) return;
            if (string.Equals(ModConfig.EnemyName.Value, unit.name, System.StringComparison.OrdinalIgnoreCase)) return;

            ModConfig.EnemyName.Value = unit.name;
            Say(Loc.F("已选择兵种：{0}", unit.name));
            Util.Log(Loc.F("[NewMode] 已选择兵种：{0}", unit.name));
        }

        static void SelectCount(int value)
        {
            if (ModConfig.SquadSize == null || ModConfig.SquadSize.Value == value) return;

            ModConfig.SquadSize.Value = value;
            Say(Loc.F("已设定数量：{0}（超出船容量会自动裁剪）", value));
            Util.Log(Loc.F("[NewMode] 数量设定：{0}", value));
        }

        /// <summary>兵种列表随岛屿缓存（同岛复用；换岛重建）。</summary>
        internal static void EnsureUnits(Island island)
        {
            if (object.ReferenceEquals(_island, island) && _units != null) return;
            _island = island;
            _units = UnitCatalog.AvailableUnits(island);
            Util.Log(Loc.F("[NewMode] 菜单：本关可用兵种 {0} 种", _units.Count));
        }
    }
}
