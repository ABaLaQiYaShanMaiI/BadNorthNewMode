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

        // ---- 布局（IMGUI 坐标：左上原点；v1.7.4 起面板高度以"上一帧实测"为准，见 _measuredH）----
        const float Pad = 10f;       // 左右外边距（内容贴住边框，别再加大）
        const float VPad = 13f;      // v1.7.4：行与行之间的纵向间距（原 10 → 13，治"纵向挤"）
        const float HintGap = 6f;    // v1.7.4：说明行与上方按钮行的间距（治"说明贴着按键"）
        const float Gap = 6f;
        const float TitleH = 28f;
        const float RowH = 27f;
        const float RowGap = 4f;     // v1.7.4：3 → 4（兵种格子之间松一点）
        const float BtnH = 26f;
        const float LineH = 21f;     // v1.7.4：19 → 21（文字行留白，治"拥挤"）
        const float InfoH = 21f;     // 同上（兵种信息行）
        const float HelpH = 20f;     // 同上（按键说明行）
        const float ColGap = 8f;
        const float IdxW = 22f;      // 序号宽
        const int CountMin = 1;
        const int CountMax = 24;

        // v1.7.4 修订：矮屏自适应用（菜单离顶部的默认边距 / 给底部原版 UI 预留的像素）
        const float TopMargin = 8f;
        const float BottomReserve = 150f;
        static int _menuUnits;       // 上一次绘制的兵种数（`TightScreen` 估高度用）

        /// <summary>超过这个长度就别发通知条了（它只有一行，长句会被裁）→ 留给 HUD。</summary>
        const int ToastMaxChars = 34;
        static readonly int[] Presets = { 1, 2, 3, 4, 6, 8, 10, 12 };

        static string _hud = "";
        static readonly List<string> _hudLines = new List<string>();
        static float _hudUntil;
        // v1.7.3（P1）：HUD 仍是每帧画，但**文本按 0.2s 节流重建**（计数扫描 + 拼串都在重建里做）。
        // 需要"立刻反映"的路径（提示/选中/开关菜单/鼠标悬停行）走 `_hudLinesValid = false` 立即失效。
        const float HudInterval = 0.2f;
        static bool _hudLinesValid;
        static float _hudRebuildAt;
        static bool _hudActive;                       // 上次重建时"有可选单位 / 有遥控小队"（决定 HUD 常显）
        static string _hudHoverSeen;                  // 上次重建时的悬停行（鼠标一动就立刻跟着变）
        static bool _hudMirror;                       // 原版通知条没接住时，才在 HUD 里重复一遍
        static Rect _rect;
        static Rect _titleRect;
        static Island _island;
        static List<VikingReference> _units;
        static Vector2 _pos = new Vector2(8f, 8f);    // 面板左上角（拖动标题栏可改）
        static bool _dragging;
        static Vector2 _dragGrab;
        static float _measuredH = -1f;                // v1.7.4：上一帧"实测内容高"（≤0 = 还没量过 → 用 MenuHeight 兜底）
        static bool _collapsed;                       // v1.7.4：折叠成只剩标题栏（想看清岛屿时用；会话内有效）
        static Vector2 _hudPos = new Vector2(-1f, -1f);   // 提示框位置（-1 = 自动：菜单下方 / 左上角）
        static Rect _hudRect;
        static bool _hudDrawn;
        static bool _hudDrag;
        static Vector2 _hudGrab;
        static string _tip = "";                      // 兵种信息行（悬停优先，否则看已选兵种）

        internal static void Toggle()
        {
            // v1.7.1：让路时不开菜单（联机中投放会冻结整局；整合包终岛无尽在接管波次）。
            if (!IsOpen)
            {
                string why;
                if (Compat.DropBlocked(Singleton<IslandGameplayManager>.instance, out why))
                {
                    Say(why);
                    Util.Warn(Loc.F("[NewMode][兼容] 投放已让路：{0}", why));
                    return;
                }
            }

            IsOpen = !IsOpen;
            Hover = "";
            PlacementMarker.Get().Hide();
            if (!IsOpen) { ClickShield.Hide(); _dragging = false; }
            Say(IsOpen ? Loc.T("投放模式：点兵种 → 点滩头陆地") : Loc.T("投放模式已关"));
            Util.Log("[NewMode] " + _hud);
        }

        internal static void Close()
        {
            IsOpen = false;
            Hover = "";
            PlacementMarker.Get().Hide();
            ClickShield.Hide();
            _dragging = false;
            _hudLinesValid = false;                  // v1.7.3：标题行要立刻变回"BadNorthNewMode"
        }

        /// <summary>状态提示：**短消息**走原版通知条（自带音效与淡入淡出）；太长（通知条一行放不下）或原版不可用就留在 HUD 里。</summary>
        internal static void Say(string msg)
        {
            _hud = msg;
            _hudUntil = Time.time + 5f;
            _hudMirror = string.IsNullOrEmpty(msg) || (msg.Length > ToastMaxChars) || !VanillaUI.Toast(msg, 4f);
            _hudLinesValid = false;                  // v1.7.3：提示变了 → 下一帧立刻重建 HUD 文本
        }

        /// <summary>菜单是否遮挡该屏幕坐标（IMGUI 的 y 轴自上而下，需翻转）。</summary>
        internal static bool Contains(Vector2 screenPos)
        {
            if (!IsOpen) return false;
            return _rect.Contains(Gui(screenPos));
        }

        /// <summary>提示框（HUD）矩形 —— 也可拖动。</summary>
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

            // 提示框：整块都能拖（作者要求）
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

            string compat = Compat.StatusLine();          // v1.7.3：它自带 0.5s 缓存 → 每帧调它不再拼串
            // v1.7.3（P1）：文本按 0.2s 节流重建 —— HUD 每帧重画，但计数/提示远没这么快变；
            // 悬停行例外（鼠标一动就要跟着变），其余"要立刻反映"的路径都会走 `Say()` 把缓存标脏。
            if (!_hudLinesValid || Time.unscaledTime >= _hudRebuildAt ||
                (IsOpen && !string.Equals(Hover, _hudHoverSeen, System.StringComparison.Ordinal)))
            {
                _hudRebuildAt = Time.unscaledTime + HudInterval;
                _hudLinesValid = true;
                BuildHudLines(compat);
            }

            // v1.7.4：菜单开着时状态行并入面板底部（见 DrawStatusRow）→ 不再另画浮窗（少一块挡岛的面板）。
            // 注意：这里的"重建文本"必须发生在早退之前，否则面板里的状态行会停在旧文本上。
            if (IsOpen) { _hudRect = new Rect(0f, 0f, 0f, 0f); return; }

            if (!_hudActive && !MarqueeSelect.Dragging && Time.time > _hudUntil && string.IsNullOrEmpty(compat)) return;

            List<string> lines = _hudLines;      // 复用同一个 List（HUD 每帧重画，不必每帧 new）
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

        /// <summary>重建 HUD 文本（v1.7.3 / P1：计数扫描与全部字符串拼接都收在这里，由 `DrawHud` 节流调用）。</summary>
        static void BuildHudLines(string compat)
        {
            int foreign, nativeCount;
            ForeignUnit.SelectableCounts(out foreign, out nativeCount);      // 一次遍历数两类（原本是两次）
            int selected = (MarqueeSelect.Pending != null) ? MarqueeSelect.Pending.Count : 0;
            _hudActive = (foreign > 0) || (nativeCount > 0) || RemoteGroup.Any;
            _hudHoverSeen = IsOpen ? Hover : null;

            List<string> lines = _hudLines;
            lines.Clear();
            lines.Add((IsOpen ? Loc.T("投放菜单") : "BadNorthNewMode") + " · v" + Plugin.VERSION);
            if (!string.IsNullOrEmpty(compat)) lines.Add(compat);
            if (IsOpen && !string.IsNullOrEmpty(Hover)) lines.Add(Hover);
            if (RemoteGroup.Any) lines.Add(Loc.F("遥控：{0}｜共 {1}", RemoteGroup.DescribeAll(), RemoteGroup.TotalCount));
            if (RemoteGroup.HasRally) lines.Add(Loc.F("待登陆集结：{0} 人 / {1} 处", RemoteGroup.RallyMemberCount(), RemoteGroup.RallyCount));
            if (foreign > 0) lines.Add(Loc.F("非原生单位 {0}{1}", foreign, (selected > 0) ? (Loc.T(", 已选中 ") + selected) : ""));
            if (nativeCount > 0) lines.Add(Loc.F("原生单位 {0}｜可遥控", nativeCount));
            if (LevelTools.CustomMode) lines.Add(Loc.T("无尽模式：本关不会自然结束，按 F3 退出"));
            if (MarqueeSelect.Dragging) lines.Add(Loc.F("框选中…（每队上限 {0}）", Util.V(ModConfig.RemoteSoftCap, 40)));
            if (_hudMirror && !string.IsNullOrEmpty(_hud) && Time.time <= _hudUntil) lines.Add(_hud);
        }

        static void DrawMenu()
        {
            int n = (_units != null) ? _units.Count : 0;
            _menuUnits = n;                              // v1.7.4 修订：给 TightScreen 估高度用
            float w = MenuWidth(n);
            // v1.7.4：高度**不再信任手写公式** —— 用上一帧"实测内容高"，首帧才用 MenuHeight 兜底。
            // 实机日志证明两者会脱钩（曾多算 77px：面板底部一大块空白，既难看又挡岛）。
            float h = _collapsed ? (TitleH + 4f)
                                 : ((_measuredH > 0f) ? _measuredH : MenuHeight(n));
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

            if (_collapsed) { _measuredH = TitleH + 4f; return; }      // 折叠：只剩标题栏（能看清岛屿）

            // ---- 兵种（>4 种时排两列：省竖向空间，尽量不挡岛）----
            MenuSkin.DrawSection(new Rect(x, y, inner, LineH), Loc.T("兵种"));
            y += LineH;

            if (n == 0)
            {
                MenuSkin.DrawLine(new Rect(x, y, inner, RowH), Loc.T("（进战局后才有兵种）"));
                _tip = "";
                y += RowH;
            }
            else
            {
                int cc = (n > 4) ? 2 : 1;
                int rows = (n + cc - 1) / cc;
                float cellW = (inner - ColGap * (cc - 1)) / cc;
                string cur = Util.V(ModConfig.EnemyName, "");
                VikingReference hover = null;
                float top = y;

                for (int c = 0; c < cc; c++)
                {
                    for (int r = 0; r < rows; r++)
                    {
                        int i = c * rows + r;
                        if (i >= n) continue;

                        VikingReference u = _units[i];
                        if (u == null) continue;

                        bool sel = !string.IsNullOrEmpty(cur) && string.Equals(u.name, cur, System.StringComparison.OrdinalIgnoreCase);
                        Rect cell = new Rect(x + c * (cellW + ColGap), top + r * RowH, cellW, RowH - RowGap);
                        bool hov = IsHover(cell);
                        MenuSkin.Bg(cell, hov, sel);

                        float iconH = cell.height - 4f;
                        Rect icon = new Rect(cell.x + 3f, cell.y + 2f, iconH, iconH);
                        VanillaSprites.DrawIcon(icon, UnitPortraits.Of(u.name), Color.white);        // 原版兵种头像
                        MenuSkin.DrawNum(new Rect(icon.xMax + 3f, cell.y, IdxW, cell.height), (i + 1) + ".", false, true);

                        if (GUI.Button(cell, GUIContent.none, MenuSkin.HitLeft))
                        {
                            VanillaUI.Click();
                            SelectUnit(u);
                        }

                        float tx = icon.xMax + 3f + IdxW;
                        MenuSkin.DrawRow(new Rect(tx, cell.y, cell.xMax - tx - 2f, cell.height), UnitNames.Of(u.name), sel);
                        if (hov) hover = u;
                    }
                }

                y = top + rows * RowH;
                _tip = DescribeUnit((hover != null) ? hover : CurrentUnit());
            }

            y = DrawCountRow(x, inner, y);
            y = DrawTakeoverRow(x, inner, y);
            // v1.7.4 修订 2：**必须接住返回值**。丢掉它 → 本行与下一行画在**同一个 y**，
            // 后画的按钮行正好盖住"靠岸速度"4 格（点那个位置会误触发清场/强制胜利）。
            // `tools\check-rules.ps1` 规则⑥ 已把这种写法拦在构建期（见 PROJECT_SPEC.md §11.10）。
            y = DrawShipSpeedRow(x, inner, y);             // v1.7.1：靠岸速度 4 档（拖拽）
            y = DrawActions(x, inner, y);
            y = DrawStatusRow(x, inner, y);                // v1.7.4：HUD 状态行并入面板（不再另画浮窗）

            // v1.7.4：实测内容高 → 下一帧的面板高度（自纠，永不脱钩）；两者不一致时打一次日志留证据。
            float measured = y - _rect.y;
            CheckMenuHeight(measured, h);
            _measuredH = measured;
        }

        /// <summary>菜单开着时，原本独立浮窗的 HUD 状态行**并入面板底部**（v1.7.4）：屏幕上少一块挡岛的面板。
        /// 矮屏（`TightScreen`）时整块省掉，优先保证档位条与操作按钮不被底部 UI 压住。</summary>
        static float DrawStatusRow(float x, float inner, float y)
        {
            if (!Util.V(ModConfig.ShowHud, true)) return y;
            if (TightScreen()) return y;

            List<string> lines = _hudLines;
            if (lines == null || lines.Count <= 1) return y;          // 0 = 标题行（面板标题已有版本号）

            y += Gap;
            for (int i = 1; i < lines.Count; i++)
            {
                MenuSkin.DrawHint(new Rect(x, y, inner, LineH), lines[i]);
                y += LineH;
            }
            return y;
        }

        /// <summary>标题栏：标题 + 收起 / 展开 + 语言 + 关闭（标题栏同时是拖动手柄）。</summary>
        static void DrawTitleBar(float x, float inner)
        {
            float by = _rect.y + 2f;
            float bh = TitleH - 4f;

            MenuSkin.DrawTitle(new Rect(x, by, inner - 292f, bh), Loc.T("投放菜单") + "  v" + Plugin.VERSION);

            Rect fold = new Rect(_rect.xMax - Pad - 236f, by, 58f, bh);        // v1.7.4：一键收起（想看清岛屿时）
            MenuSkin.Bg(fold, IsHover(fold), false);
            if (GUI.Button(fold, GUIContent.none, MenuSkin.HitCenter))
            {
                VanillaUI.Click();
                _collapsed = !_collapsed;
                _measuredH = -1f;                       // 展开后重新量一次高度（折叠期间不写 _measuredH）
            }
            MenuSkin.DrawBtn(fold, _collapsed ? Loc.T("展开") : Loc.T("收起"), false);

            bool zh = !Loc.IsEnglish;
            DrawLangButton(new Rect(_rect.xMax - Pad - 178f, by, 52f, bh), "中文", zh, "zh");
            DrawLangButton(new Rect(_rect.xMax - Pad - 122f, by, 64f, bh), "English", !zh, "en");

            Rect close = new Rect(_rect.xMax - Pad - 52f, by, 52f, bh);
            MenuSkin.Bg(close, IsHover(close), false);
            if (GUI.Button(close, GUIContent.none, MenuSkin.HitCenter))
            {
                VanillaUI.Click();
                Close();
                Say(Loc.T("投放模式已关"));
            }
            MenuSkin.DrawBtn(close, Loc.T("关闭"), false);
        }

        /// <summary>数量行（− / + 与预设）+ 兵种信息行。</summary>
        static float DrawCountRow(float x, float inner, float y)
        {
            y += VPad;                                   // v1.7.4：与上方兵种网格拉开（原来贴在一起）
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
            MenuSkin.DrawNum(new Rect(cx, y, 42f, BtnH), (shown > 0) ? shown.ToString() : "-", false, false);
            cx += 46f;
            Rect plus = new Rect(cx, y, 28f, BtnH);
            MenuSkin.Bg(plus, IsHover(plus), false);
            if (GUI.Button(plus, GUIContent.none, MenuSkin.HitCenter)) { VanillaUI.Click(); StepCount(1); }
            MenuSkin.DrawBtn(plus, "+", false);
            cx += 36f;

            // v1.7.4：预设按钮**拉伸铺满到右边界**（原来是固定 38px 步长 → 右侧留 ~80px 空白，看着"离边框很远"）
            float avail = (x + inner) - cx;
            float step = avail / Presets.Length;
            float pw = Mathf.Max(24f, step - 4f);
            for (int i = 0; i < Presets.Length; i++)
            {
                int v = Presets[i];
                bool on = (curSize == v);
                Rect pr = new Rect(cx + i * step, y, pw, BtnH);
                MenuSkin.Bg(pr, IsHover(pr), on);
                string text = v.ToString();
                if (GUI.Button(pr, GUIContent.none, MenuSkin.HitCenter))
                {
                    VanillaUI.Click();
                    SelectCount(v);
                }
                MenuSkin.DrawNum(pr, text, true, false);
            }

            y += BtnH + HintGap;                         // v1.7.4：说明行与按钮行拉开
            MenuSkin.DrawHint(new Rect(x, y, inner, InfoH), _tip);
            return y + InfoH;
        }

        /// <summary>接管开关两枚（v1.7.4 起无说明行）。</summary>
        static float DrawTakeoverRow(float x, float inner, float y)
        {
            y += VPad;
            float tw = (inner - Gap) * 0.5f;

            // v1.7.1：整合包 / 联机正在接管波次时，"拦下原版波次"在**菜单侧**就直接拒绝
            //（否则会与对方的波次调度叠加成"波次永远不来"，见兼容方案 §3.3 C4-B）。
            string refuseWhy = null;
            bool waveAllowed = !Compat.WaveBlockForbidden(out refuseWhy);
            DrawToggle(new Rect(x, y, tw, BtnH), ModConfig.BlockVanillaWaves,
                Loc.T("原版波次：拦下"), Loc.T("原版波次：正常"), waveAllowed, refuseWhy);
            DrawToggle(new Rect(x + tw + Gap, y, tw, BtnH), ModConfig.ControlNativeUnits, Loc.T("原生单位：可遥控"), Loc.T("原生单位：不可"));

            y += BtnH;
            return y;                                   // v1.7.4：删掉"拦下 = …"那句冗余说明（按钮文字已自解释）
        }

        /// <summary>三个常用操作（清场/强制胜利也能从菜单点，不必记快捷键）。</summary>
        static float DrawActions(float x, float inner, float y)
        {
            y += VPad;
            float aw = (inner - Gap * 2f) / 3f;

            Rect rel = new Rect(x, y, aw, BtnH);
            MenuSkin.Bg(rel, IsHover(rel), false);
            if (GUI.Button(rel, GUIContent.none, MenuSkin.HitCenter)) { VanillaUI.Click(); Plugin.ReleaseRemoteControl(); }
            MenuSkin.DrawBtn(rel, Loc.T("释放遥控"), false);

            Rect clr = new Rect(x + aw + Gap, y, aw, BtnH);
            MenuSkin.Bg(clr, IsHover(clr), false);
            if (GUI.Button(clr, GUIContent.none, MenuSkin.HitCenter)) { VanillaUI.Click(); SpawnLedger.RequestCleanup(); }
            MenuSkin.DrawBtn(clr, Loc.T("清场（F2）"), false);

            Rect win = new Rect(x + (aw + Gap) * 2f, y, aw, BtnH);
            MenuSkin.Bg(win, IsHover(win), false);
            if (GUI.Button(win, GUIContent.none, MenuSkin.HitCenter)) { VanillaUI.Click(); LevelTools.RequestForceWin(); }
            MenuSkin.DrawBtn(win, Loc.T("强制胜利（F3）"), false);
            y += BtnH + VPad;

            if (!ShowHelpCfg() || TightScreen()) return y;   // cfg 关掉，或矮屏自适应要省掉说明块
            y += HintGap;                               // v1.7.4：说明块与按钮行拉开
            string[] help = HelpTexts();
            for (int i = 0; i < help.Length; i++)
                MenuSkin.DrawHint(new Rect(x, y + i * HelpH, inner, HelpH), help[i]);

            return y + help.Length * HelpH;      // 把实际底边交回 DrawMenu（面板高度按它自纠）
        }

        /// <summary>按键说明块的 cfg 开关（`[UI] ShowHelp`）。**只读 cfg** —— 面板高度与宽度估算用它，
        /// 避免与 `TightScreen()` 互相依赖（否则会递归）。</summary>
        static bool ShowHelpCfg()
        {
            return Util.V(ModConfig.ShowHelp, true);
        }

        /// <summary>矮屏自适应（v1.7.4 修订）：菜单若会压到底部原版 UI 区（技能条 / 提示条），本轮就省掉"按键说明块 + 状态行"，
        /// 免得菜单最后几行（靠岸速度档位条、清场/强制胜利）被底部 UI 或头像条压住。
        /// 判定只用屏幕高度与**预估**高度（与收缩结果无关）→ 不会来回抖。</summary>
        static bool TightScreen()
        {
            float limit = SquadBar.VanillaUiTop();
            if (limit >= float.MaxValue) limit = Screen.height - BottomReserve;
            return (TopMargin + MenuHeight(_menuUnits) + 8f) > limit;
        }

        static bool _heightWarned;

        /// <summary>自检（v1.7.3 / P2 起；v1.7.4 起面板高度改为"实测自纠"）：本帧用来画面板的高度 `used` 与实测内容高 `measured`
        /// 不一致时打一次日志（历史上手写公式曾多算 77px）。因为下一帧会用 `measured`，这不会导致持续偏差，只作为"布局改动"的证据。</summary>
        static void CheckMenuHeight(float measured, float used)
        {
            if (_heightWarned) return;
            if (Mathf.Abs(measured - used) <= 1.5f) return;

            _heightWarned = true;
            Util.Log(Loc.F("[NewMode] 面板高度自纠：本帧用 {1:F1}，实测内容 {0:F1}（差 {2:F1}px）—— 下一帧起改按实测高度绘制",
                measured, used, measured - used));
        }

        static string[] _help;
        static string _helpKey;

        /// <summary>2 行按键说明（按语言与单/双键缓存；每帧会被量宽/绘制各调一次，别每次 new 数组）。
        /// v1.7.4：3 行合并成 2 行（原第 2/3 行说的是同一件事：修饰键与拖动）。</summary>
        static string[] HelpTexts()
        {
            string key = (Loc.IsEnglish ? "en" : "zh") + (MarqueeSelect.SingleButtonMode() ? "1" : "2");
            if (_help != null && string.Equals(_helpKey, key, System.StringComparison.Ordinal)) return _help;

            _helpKey = key;
            _help = new string[]
            {
                MarqueeSelect.SingleButtonMode()
                    ? Loc.T("单键：点单位 = 选中｜双击 = 整队｜有选中时点地块 = 前进")
                    : Loc.T("双键：左键点单位 = 选中｜双击 = 整队｜右键点地块 = 前进"),
                Loc.T("Shift + 点 = 并入｜R = 全选｜Alt + 拖动 = 框选｜拖动 = 平移相机"),
            };
            return _help;
        }

        // ---- 靠岸速度 4 档（v1.7.1）：按下即选、横扫即换、松手落盘 ----
        static bool _gearDrag;
        static bool _gearMoved;

        /// <summary>4 档拖动条：四等分，每格写着档位；按住拖动横扫即换档。联机客机/反射不可用时置灰只显示。
        /// v1.7.4：说明行改成**先画轨道、说明放轨道下面**（原来说明在上、轨道贴着说明，看着像"说明被挤在中间"）。</summary>
        static float DrawShipSpeedRow(float x, float inner, float y)
        {
            y += VPad;

            bool client = Compat.SessionClient;                     // 联机客机：船由主机权威 → 本地改无意义
            bool blocked = client || ShipSpeedRuntime.Disabled;

            Rect track = new Rect(x, y, inner, BtnH);
            int cur = ShipSpeedRuntime.GearIndex;
            float segW = inner / ShipSpeedRuntime.Gears.Length;

            for (int i = 0; i < ShipSpeedRuntime.Gears.Length; i++)
            {
                Rect seg = new Rect(track.x + i * segW, track.y, segW - 1f, track.height);
                MenuSkin.Bg(seg, !blocked && IsHover(seg), i == cur);
                MenuSkin.DrawBtn(seg, GearLabel(i), i == cur);
            }

            if (!blocked) HandleGearDrag(track, segW);

            y += BtnH + HintGap;                                    // 说明行与轨道拉开
            MenuSkin.DrawHint(new Rect(x, y, inner, HelpH),
                client ? Loc.T("联机客机：靠岸速度由主机决定")
                       : Loc.T("靠岸速度（×1 = 原版速度；按住拖动切档）"));

            return y + HelpH;
        }

        static void HandleGearDrag(Rect track, float segW)
        {
            Event e = Event.current;
            if (e == null) return;

            Vector2 m = e.mousePosition;                            // IMGUI 坐标（与面板同一坐标系）
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (!track.Contains(m)) return;
                    _gearDrag = true;
                    _gearMoved = false;
                    ApplyGear(m.x, track, segW);
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (!_gearDrag) return;
                    ApplyGear(m.x, track, segW);
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (!_gearDrag) return;
                    _gearDrag = false;
                    if (_gearMoved)                                     // 只在真的换过档时给一次声音 + 日志
                    {
                        VanillaUI.Click();
                        Util.Log("[NewMode] " + ShipSpeedRuntime.Describe());
                    }
                    e.Use();
                    break;
            }
        }

        static void ApplyGear(float mouseX, Rect track, float segW)
        {
            int idx = Mathf.Clamp(Mathf.FloorToInt((mouseX - track.x) / Mathf.Max(1f, segW)),
                                  0, ShipSpeedRuntime.Gears.Length - 1);
            if (idx == ShipSpeedRuntime.GearIndex) return;               // 同档不重复套用/不反复落盘

            _gearMoved = true;
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (_island != null) ? _island : ((gm != null) ? gm.island : null);
            ShipSpeedRuntime.SetGearIndex(idx, island);
        }

        /// <summary>档位标签（v1.7.4）：第 1 档就是**原版速度**，明确标出来，免得玩家以为是"我们改过的 ×1"。</summary>
        static string GearLabel(int i)
        {
            if (i <= 0) return Loc.T("×1（原版）");
            return "×" + ShipSpeedRuntime.Gears[i].ToString("0.##");
        }

        /// <summary>面板高度（v1.7.4：**只作首帧兜底** —— 之后用上一帧实测内容高，见 `_measuredH` 与 `CheckMenuHeight`）。
        /// 保留这个粗算式是为了"第一帧别太小"；它不再需要与 DrawMenu 逐项精确对齐（那正是 77px 偏差的来源）。</summary>
        static float MenuHeight(int n)
        {
            int cc = (n > 4) ? 2 : 1;
            int rows = (n <= 0) ? 1 : (n + cc - 1) / cc;
            return TitleH + Pad + LineH + rows * RowH        // 标题 + 兵种小节
                 + VPad + BtnH + HintGap + InfoH             // 数量行 + 兵种信息行
                 + VPad + BtnH                               // 接管开关（v1.7.4 起无说明行）
                 + VPad + BtnH + HintGap + HelpH             // 靠岸速度（轨道 + 说明）
                 + VPad + BtnH + VPad                        // 三个操作
                 + (ShowHelpCfg() ? HintGap + HelpTexts().Length * HelpH : 0f)
                 + 4f;
        }

        /// <summary>面板宽度：按当前语言与兵种名实测后夹到屏幕内（中英都不写死宽度）。</summary>
        static float MenuWidth(int n)
        {
            int cc = (n > 4) ? 2 : 1;
            float w = 560f;                                  // 数量行（8 个预设）+ 两列兵种的下限
            w = Mathf.Max(w, MenuSkin.Measure(Loc.T("投放菜单") + "  v" + Plugin.VERSION, MenuSkin.Title) + 292f);   // 标题栏：收起 + 中文 + English + 关闭
            w = Mathf.Max(w, MenuSkin.Measure(Loc.T("释放遥控"), MenuSkin.Btn) * 3f + 60f);
            w = Mathf.Max(w, MenuSkin.Measure(Loc.T("靠岸速度（×1 = 原版速度；按住拖动切档）"), MenuSkin.Hint));
            w = Mathf.Max(w, MenuSkin.Measure(GearLabel(0), MenuSkin.NumOn) * ShipSpeedRuntime.Gears.Length + 24f);   // 4 档标签绝不裁字

            if (ShowHelpCfg())
            {
                string[] help = HelpTexts();
                for (int i = 0; i < help.Length; i++) w = Mathf.Max(w, MenuSkin.Measure(help[i], MenuSkin.Hint));
            }

            float cell = 0f;
            for (int i = 0; i < n; i++)
            {
                VikingReference u = _units[i];
                if (u == null) continue;

                float need = 3f + 22f + 4f + IdxW + MenuSkin.Measure(UnitNames.Of(u.name), MenuSkin.Line) + 10f;
                if (need > cell) cell = need;
            }
            if (cell > 0f) w = Mathf.Max(w, cell * cc + ColGap * (cc - 1) + Pad * 2f + 8f);

            return Mathf.Clamp(w, 420f, Mathf.Min(860f, Screen.width * 0.72f));
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
            return Loc.F("默认 {0} 人 · 上限 {1} 人 · 船：{2}", def, UnitCatalog.MaxSquadSize(island, u), (ship != null) ? ship.name : Loc.T("无"))
                 + "　" + u.name + (UnitNames.IsKnownVanilla(u.name) ? "" : Loc.T("（外生）"));
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
            DrawToggle(r, entry, onLabel, offLabel, true, null);
        }

        /// <summary>开关按钮。v1.7.1：`allowOn=false` 时"点它置为开"会被拒绝并说明原因（拦波次与整合包/联机互斥）。</summary>
        static void DrawToggle(Rect r, ConfigEntry<bool> entry, string onLabel, string offLabel, bool allowOn, string refuseWhy)
        {
            if (entry == null) return;

            bool on = entry.Value;
            MenuSkin.Bg(r, IsHover(r), on);
            if (GUI.Button(r, GUIContent.none, MenuSkin.HitCenter))
            {
                VanillaUI.Click();

                if (!on && !allowOn)
                {
                    string deny = Loc.F("不能开：{0}", refuseWhy);     // 原版通知条 + 日志各一份（cfg 值不变）
                    Say(deny);
                    Util.Warn("[NewMode][兼容] " + deny);
                }
                else
                {
                    entry.Value = !on;

                    string now = on ? offLabel : onLabel;
                    Say(Loc.F("已切换：{0}", now));
                    Util.Log("[NewMode] " + now);
                }
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

            Say(Loc.T("已切换语言"));
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
            Say(Loc.F("数量 {0}（超出船容量会裁剪）", value));
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
