using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>局内 HUD + 投放菜单（纯 IMGUI，零资源）。菜单即"投放模式"，由 Plugin 负责输入，本类只管显示与选择。</summary>
    internal static class IngameMenu
    {
        internal static bool IsOpen;
        internal static string Hover = "";

        /// <summary>菜单屏幕矩形（IMGUI 坐标：左上原点）——v1.5.6 的点击拦截面（ClickShield）按它摆位。</summary>
        internal static Rect MenuRect { get { return _rect; } }

        static string _hud = "";
        static float _hudUntil;
        static Rect _rect;
        static Island _island;
        static List<VikingReference> _units;

        internal static void Toggle()
        {
            IsOpen = !IsOpen;
            Hover = "";
            PlacementMarker.Get().Hide();
            if (!IsOpen) ClickShield.Hide();
            Say(IsOpen ? Loc.T("投放菜单：左键点兵种选择，再点滩头陆地投放（F1 关闭 / F2 清场 / F3 强制胜利）") : Loc.T("已关闭投放菜单"));
            Util.Log("[NewMode] " + _hud);
        }

        internal static void Close()
        {
            IsOpen = false;
            Hover = "";
            PlacementMarker.Get().Hide();
            ClickShield.Hide();
        }

        internal static void Say(string msg)
        {
            _hud = msg;
            _hudUntil = Time.time + 5f;      // 用 Time.time：暂停时不推进（与玩法计时一致）
        }

        /// <summary>菜单是否遮挡该屏幕坐标（IMGUI 的 y 轴自上而下，需翻转）。</summary>
        internal static bool Contains(Vector2 screenPos)
        {
            if (!IsOpen) return false;
            return _rect.Contains(new Vector2(screenPos.x, Screen.height - screenPos.y));
        }

        internal static void Draw()
        {
            DrawHud();
            if (IsOpen) DrawMenu();
        }

        static void DrawHud()
        {
            if (!Util.V(ModConfig.ShowHud, true)) return;

            int foreign = ForeignUnit.Count(false);
            int nativeCount = ForeignUnit.Count(true);
            int selected = (MarqueeSelect.Pending != null) ? MarqueeSelect.Pending.Count : 0;
            bool active = (foreign > 0) || (nativeCount > 0) || RemoteGroup.Any;   // 有单位/遥控小队 → 常显状态
            if (!IsOpen && !active && !MarqueeSelect.Dragging && Time.time > _hudUntil) return;

            string text = IsOpen
                ? Loc.T("BadNorthNewMode · 投放菜单（左键点兵种 → 再点滩头陆地投放；右键或 Esc 关闭）")
                : ("BadNorthNewMode v" + Plugin.VERSION);
            if (IsOpen && !string.IsNullOrEmpty(Hover)) text += "\n" + Hover;

            if (RemoteGroup.Any)
                text += Loc.F("\n遥控小队：{0}（共 {1}）",
                    RemoteGroup.DescribeAll(), RemoteGroup.TotalCount);

            if (RemoteGroup.HasRally)
                text += Loc.F("\n待登陆集结：{0} 人 / {1} 处（落地后自动前往）",
                    RemoteGroup.RallyMemberCount(), RemoteGroup.RallyCount);

            if (foreign > 0)
                text += Loc.F("\n非原生单位 {0}（可选 {1}{2}）", foreign, ForeignUnit.SelectableCount(false),
                    (selected > 0) ? (Loc.T(", 已选中 ") + selected) : "");

            if (nativeCount > 0)
                text += Loc.F("\n原生单位 {0}（可选 {1}，可遥控）", nativeCount, ForeignUnit.SelectableCount(true));

            if (MarqueeSelect.Dragging)
                text += Loc.F("\n框选中…（按兵种自动分队，每队上限 {0}）", Util.V(ModConfig.RemoteSoftCap, 40));

            if (!string.IsNullOrEmpty(_hud)) text += "\n" + _hud;

            int lines = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
            float h = 12f + lines * 18f;

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(8f, 8f, 820f, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16f, 10f, 820f, h - 4f), text);
        }

        /// <summary>兵种菜单：语言行 + 兵种列表 + 数量按钮（左键点击即写回 cfg，立即生效）。</summary>
        static void DrawMenu()
        {
            int n = (_units != null) ? _units.Count : 0;
            float rowH = 26f;
            float headH = 72f;                                   // 语言行 + 标题 + 当前
            float rows = Mathf.Max(1, n);
            float countH = 48f;                                  // "数量" + 按钮行
            _rect = new Rect(8f, 82f, 420f, headH + rows * rowH + countH + 74f);

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(_rect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float x = _rect.x + 10f;
            float w = _rect.width - 20f;

            // ---- 语言切换（按钮各自用本语言书写，不依赖当前语言；写回 cfg，下一帧生效）----
            GUI.Label(new Rect(x, _rect.y + 6f, 70f, 20f), Loc.T("语言"));
            bool isZh = !Loc.IsEnglish;
            DrawLangButton(new Rect(x + 72f, _rect.y + 4f, 54f, 22f), "中文", isZh, "zh");
            DrawLangButton(new Rect(x + 130f, _rect.y + 4f, 76f, 22f), "English", !isZh, "en");
            GUI.Label(new Rect(x + 340f, _rect.y + 6f, 60f, 20f), "v" + Plugin.VERSION);   // 版本号：文件日志默认关闭后，这是反馈问题时唯一的可见来源

            GUI.Label(new Rect(x, _rect.y + 30f, w, 20f), Loc.T("兵种（左键点选；括号内为 cfg 内部名）"));

            string cur = Util.V(ModConfig.EnemyName, "");
            GUI.Label(new Rect(x, _rect.y + 50f, w, 20f),
                Loc.T("当前：") + (string.IsNullOrEmpty(cur) ? Loc.T("随机") : UnitNames.Of(cur) + Loc.T("（") + cur + Loc.T("）")));

            int curSize = Util.V(ModConfig.SquadSize, 0);

            if (n == 0)
            {
                GUI.Label(new Rect(x, _rect.y + headH + 2f, w, 20f), Loc.T("（进入战局后才会列出可用兵种）"));
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    VikingReference u = _units[i];
                    if (u == null) continue;

                    bool isSel = !string.IsNullOrEmpty(cur) &&
                                 string.Equals(u.name, cur, System.StringComparison.OrdinalIgnoreCase);
                    Rect r = new Rect(x, _rect.y + headH + i * rowH, w, rowH - 3f);

                    string label = Loc.F("{0}{1}. {2}（{3}）",
                        isSel ? "▶ " : "     ", i + 1, UnitNames.Of(u.name), u.name);

                    Color old = GUI.color;
                    if (isSel) GUI.color = new Color(0.45f, 1f, 1f, 1f);
                    if (GUI.Button(r, label)) SelectUnit(u);
                    GUI.color = old;
                }
            }

            // ---- 数量预设（点多少就装多少；0 不再出现在 UI，cfg 里的 0 仍走原版梯度表）----
            float cy = _rect.y + headH + rows * rowH + 2f;
            GUI.Label(new Rect(x, cy, w, 18f), Loc.T("数量（左键点击）"));

            int[] presets = { 1, 2, 3, 4, 6, 8, 10, 12 };
            float bw = 44f, gap = 4f;
            for (int i = 0; i < presets.Length; i++)
            {
                int v = presets[i];
                Rect br = new Rect(x + i * (bw + gap), cy + 20f, bw, 24f);
                Color old = GUI.color;
                if (curSize == v) GUI.color = new Color(0.45f, 1f, 1f, 1f);
                if (GUI.Button(br, v.ToString())) SelectCount(v);
                GUI.color = old;
            }

            // ---- 操作按键（关菜单后生效）：只留按键；为压窄菜单，按键分 3 行 ----
            float hy = cy + countH;
            GUI.Label(new Rect(x, hy, w, 18f), MarqueeSelect.SingleButtonMode()
                ? Loc.T("单键：点单位 = 选整队｜有选中时点地块 = 前进")
                : Loc.T("双键：左键点单位 = 选整队｜右键点地块 = 前进"));
            GUI.Label(new Rect(x, hy + 18f, w, 18f), Loc.T("Shift + 左键 = 并入｜R = 全选｜Alt + 拖动 = 框选"));
            GUI.Label(new Rect(x, hy + 36f, w, 18f), Loc.T("船上也能选（登陆后自动去集结点）｜F1 关闭 · F2 清场 · F3 强制胜利"));
        }

        /// <summary>语言按钮：当前语言高亮；点击写回 cfg。</summary>
        static void DrawLangButton(Rect r, string label, bool active, string mode)
        {
            Color old = GUI.color;
            if (active) GUI.color = new Color(0.45f, 1f, 1f, 1f);
            if (GUI.Button(r, label)) SelectLanguage(mode);
            GUI.color = old;
        }

        /// <summary>切换语言：写回 cfg（自动保存），下次启动/下一帧均生效。</summary>
        static void SelectLanguage(string mode)
        {
            if (!Loc.SetLanguage(mode)) return;

            Say(Loc.T("语言已切换（立即生效）"));
            Util.Log("[NewMode] Language = " + mode);
        }

        /// <summary>选中兵种：写回 cfg（自动保存），下次投放生效。</summary>
        static void SelectUnit(VikingReference unit)
        {
            if (unit == null || ModConfig.EnemyName == null) return;
            if (string.Equals(ModConfig.EnemyName.Value, unit.name, System.StringComparison.OrdinalIgnoreCase)) return;

            ModConfig.EnemyName.Value = unit.name;
            Say(Loc.F("已选择兵种：{0}", unit.name));
            Util.Log(Loc.F("[NewMode] 已选择兵种：{0}", unit.name));
        }

        /// <summary>设定装载数量：点多少就装多少（超出最大船容量才会被裁）。</summary>
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
