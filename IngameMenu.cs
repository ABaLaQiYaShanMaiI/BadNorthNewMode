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
            Say(IsOpen ? "投放菜单：左键点兵种选择，再点滩头陆地投放（F1 关闭 / F2 强制清场）" : "已关闭投放菜单");
            Util.Log("[NewMode] " + _hud);
        }

        internal static void Close()
        {
            IsOpen = false;
            Hover = "";
            PlacementMarker.Get().Hide();
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

            int foreign = ForeignUnit.All.Count;
            int selected = (MarqueeSelect.Pending != null) ? MarqueeSelect.Pending.Count : 0;
            bool help = (foreign > 0) || RemoteGroup.Any;                  // 有非原生单位/遥控小队 → 常显操作提示
            if (!IsOpen && !help && !MarqueeSelect.Dragging && Time.time > _hudUntil) return;

            string text = IsOpen
                ? "BadNorthNewMode · 投放菜单（左键点兵种 → 再点滩头陆地投放；右键或 Esc 关闭）"
                : "BadNorthNewMode";
            if (IsOpen && !string.IsNullOrEmpty(Hover)) text += "\n" + Hover;

            if (RemoteGroup.Any)
                text += string.Format("\n遥控小队：{0}（共 {1}）｜左键点地块 = 全队前进",
                    RemoteGroup.DescribeAll(), RemoteGroup.TotalCount);

            if (foreign > 0)
                text += string.Format("\n非原生单位 {0}（可选 {1}{2}）", foreign, ForeignUnit.UsableCount(),
                    (selected > 0) ? (", 已选中 " + selected) : "");

            if (MarqueeSelect.Dragging)
                text += "\n框选中…（按兵种自动分队，每队上限 " + Util.V(ModConfig.RemoteSoftCap, 40) + "）";

            if (help)
                text += (IsOpen ? "\n[关菜单后] " : "\n[遥控] ") +
                        "左键点单位 = 选中｜Shift 点同类 = 合并｜R = 全选｜左键点地块 = 成队前进｜Alt+拖动 = 框选";

            if (!string.IsNullOrEmpty(_hud)) text += "\n" + _hud;

            int lines = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
            float h = 12f + lines * 18f;

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(8f, 8f, 820f, h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16f, 10f, 820f, h - 4f), text);
        }

        /// <summary>兵种菜单：上排选兵种、下排选数量，均为左键点击（写回 cfg，立即生效）。</summary>
        static void DrawMenu()
        {
            int n = (_units != null) ? _units.Count : 0;
            float rowH = 26f;
            float headH = 74f;                                   // 标题 + 当前 + 数量信息
            float rows = Mathf.Max(1, n);
            float countH = 50f;                                  // "数量（…）" + 按钮行
            _rect = new Rect(8f, 82f, 560f, headH + rows * rowH + countH + 104f);

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(_rect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float x = _rect.x + 10f;
            float w = _rect.width - 20f;

            GUI.Label(new Rect(x, _rect.y + 6f, w, 20f), "兵种选择（按难度递增，左键点选；括号内为 cfg 内部名）");

            string cur = Util.V(ModConfig.EnemyName, "");
            GUI.Label(new Rect(x, _rect.y + 26f, w, 20f),
                "当前：" + (string.IsNullOrEmpty(cur) ? "随机" : UnitNames.Of(cur) + "（" + cur + "）"));

            int curSize = Util.V(ModConfig.SquadSize, 0);
            VikingReference sel = SelectedUnit();
            string sizeInfo;
            if (sel != null && _island != null)
            {
                int def = UnitCatalog.DefaultSquadSize(_island, sel);
                int cap = UnitCatalog.MaxSquadSize(_island, sel);
                int now = (curSize > 0) ? curSize : def;
                Longship auto = UnitCatalog.PickShipForCount(_island, sel, now);
                sizeInfo = string.Format("数量：{0}（本兵种默认 {1}，上限 {2}）　船：{3}（自动匹配）",
                    (curSize > 0) ? curSize.ToString() : "默认 " + def, def, cap,
                    (auto != null) ? auto.name : "无");
            }
            else
            {
                sizeInfo = "数量：" + ((curSize > 0) ? curSize.ToString() : "默认（按兵种原版算法）");
            }
            GUI.Label(new Rect(x, _rect.y + 46f, w, 20f), sizeInfo);

            if (n == 0)
            {
                GUI.Label(new Rect(x, _rect.y + headH + 2f, w, 20f), "（进入战局后才会列出可用兵种）");
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

                    int def = (_island != null) ? UnitCatalog.DefaultSquadSize(_island, u) : 0;
                    string label = string.Format("{0}{1}. {2}（{3}）   默认 {4} 个",
                        isSel ? "▶ " : "     ", i + 1, UnitNames.Of(u.name), u.name, def);

                    Color old = GUI.color;
                    if (isSel) GUI.color = new Color(0.45f, 1f, 1f, 1f);
                    if (GUI.Button(r, label)) SelectUnit(u);
                    GUI.color = old;
                }
            }

            // ---- 数量预设（0 = 默认：按兵种梯度表）----
            float cy = _rect.y + headH + rows * rowH + 2f;
            GUI.Label(new Rect(x, cy, w, 18f), "数量（左键点击；默认 = 按该兵种原版算法）");

            int[] presets = { 0, 1, 2, 3, 4, 6, 8, 10, 12 };
            float bw = 44f, gap = 4f;
            for (int i = 0; i < presets.Length; i++)
            {
                int v = presets[i];
                Rect br = new Rect(x + i * (bw + gap), cy + 20f, bw, 24f);
                Color old = GUI.color;
                if (curSize == v) GUI.color = new Color(0.45f, 1f, 1f, 1f);
                if (GUI.Button(br, (v == 0) ? "默认" : v.ToString())) SelectCount(v);
                GUI.color = old;
            }

            // ---- 遥控操作说明（关菜单后生效；玩家不点 F1 也知道怎么用）----
            float hy = cy + countH;
            GUI.Label(new Rect(x, hy, w, 18f), "遥控操作（关闭菜单后生效）：");
            GUI.Label(new Rect(x, hy + 18f, w, 18f), "· 左键点\"非原生单位\" = 选中（再点同一个取消）；Shift + 左键点同类 = 合并成队");
            GUI.Label(new Rect(x, hy + 36f, w, 18f), "· R = 一键全选（单位跑远看不清时最省事）；左键点地块 = 选中的单位 / 已有小队一起前进");
            GUI.Label(new Rect(x, hy + 54f, w, 18f), "· Alt + 左键拖动 = 从任意位置框选（相机暂停）；不按 Alt 时只有从单位上起拖才框选");
            GUI.Label(new Rect(x, hy + 72f, w, 18f), "· 单位须已下船（HUD 的\"可选\"就是当前能选的数量）｜F1 关闭菜单 · F2 强制清场");
        }

        /// <summary>选中兵种：写回 cfg（自动保存），下次投放生效。</summary>
        static void SelectUnit(VikingReference unit)
        {
            if (unit == null || ModConfig.EnemyName == null) return;
            if (string.Equals(ModConfig.EnemyName.Value, unit.name, System.StringComparison.OrdinalIgnoreCase)) return;

            ModConfig.EnemyName.Value = unit.name;
            Say("已选择兵种：" + unit.name);
            Util.Log("[NewMode] 已选择兵种：" + unit.name);
        }

        /// <summary>设定装载数量：0 = 默认（按兵种梯度表），&gt;0 = 固定数量。</summary>
        static void SelectCount(int value)
        {
            if (ModConfig.SquadSize == null || ModConfig.SquadSize.Value == value) return;

            ModConfig.SquadSize.Value = value;
            Say(value > 0 ? ("已设定数量：" + value + "（超出船容量会自动裁剪）") : "数量：按兵种默认（原版算法）");
            Util.Log("[NewMode] 数量设定：" + ((value > 0) ? value.ToString() : "默认(原版算法)"));
        }

        /// <summary>当前 cfg 里选中的兵种对象（用于显示该兵种的默认/上限数量）。</summary>
        static VikingReference SelectedUnit()
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

        /// <summary>兵种列表随岛屿缓存（同岛复用；换岛重建）。</summary>
        internal static void EnsureUnits(Island island)
        {
            if (object.ReferenceEquals(_island, island) && _units != null) return;
            _island = island;
            _units = UnitCatalog.AvailableUnits(island);
            Util.Log("[NewMode] 菜单：本关可用兵种 " + _units.Count + " 种");
        }
    }
}
