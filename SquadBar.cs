using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>屏幕底部的**小队头像条**：把场上可选的小队排成一行，点一下直接选中整队（v1.6.1 新功能）。</summary>
    internal static class SquadBar
    {
        sealed class Slot
        {
            internal Squad squad;      // 引擎小队（一次投放 / 一次登陆 = 一队）；可能为 null
            internal string type;
            internal string icon;
            internal int count;
            internal Agent rep;        // 代表：状态查询用（选中 / 受控）
            internal ForeignUnit fu;   // 代表：点击时交给 MarqueeSelect.SelectUnitAt
        }

        const float SlotH = 40f;
        const float IconSize = 32f;
        const float Pad = 6f;
        const float Gap = 6f;
        const float Bottom = 12f;

        static readonly List<Slot> _slots = new List<Slot>();
        static readonly List<float> _widths = new List<float>();   // 上一帧的布局（点击判定与绘制共用）
        static Rect _rect;
        static float _firstX;
        static bool _laidOut;
        static float _nextScan;

        internal static bool Visible { get { return _slots.Count > 0; } }

        /// <summary>头像条矩形（IMGUI 坐标；ClickShield 按它摆位，用上一帧的值）。</summary>
        internal static Rect BarRect { get { return _rect; } }

        /// <summary>该屏幕坐标是否落在头像条上（世界点击要让位）。</summary>
        internal static bool Contains(Vector2 screenPos)
        {
            if (!Visible) return false;
            return _rect.Contains(new Vector2(screenPos.x, Screen.height - screenPos.y));
        }

        internal static void Tick(IslandGameplayManager gm)
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.3f;
                Scan(gm);
            }
            HandleClick();
        }

        /// <summary>按**引擎小队**分组（一次投放 / 一次登陆 = 一队），人多的排前面。</summary>
        static void Scan(IslandGameplayManager gm)
        {
            _slots.Clear();
            if (!Util.V(ModConfig.ShowSquadBar, true)) return;

            string why;
            if (!Plugin.InBattle(gm, out why)) return;

            List<ForeignUnit> all = ForeignUnit.All;
            for (int i = 0; i < all.Count; i++)
            {
                ForeignUnit f = all[i];
                Agent a = (f != null) ? f.agent : null;
                if (a == null || !ForeignUnit.Selectable(a)) continue;

                Slot slot = Find(a.squad, f.unitType);
                if (slot == null)
                {
                    slot = new Slot();
                    slot.squad = a.squad;
                    slot.type = f.unitType;
                    slot.icon = IconFor(f.unitType);
                    slot.rep = a;
                    slot.fu = f;
                    _slots.Add(slot);
                }
                slot.count++;
            }

            SortByCount();
            int max = Mathf.Max(1, Util.V(ModConfig.SquadBarMax, 8));
            if (_slots.Count > max) _slots.RemoveRange(max, _slots.Count - max);
        }

        static Slot Find(Squad squad, string type)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                bool hit = (squad != null)
                    ? object.ReferenceEquals(s.squad, squad)
                    : string.Equals(s.type, type, System.StringComparison.OrdinalIgnoreCase);
                if (hit) return s;
            }
            return null;
        }

        static void SortByCount()
        {
            for (int i = 1; i < _slots.Count; i++)
            {
                Slot v = _slots[i];
                int j = i - 1;
                while (j >= 0 && _slots[j].count < v.count) { _slots[j + 1] = _slots[j]; j--; }
                _slots[j + 1] = v;
            }
        }

        /// <summary>兵种 → 借哪个原版图标（借不到就是空框，不影响功能）。</summary>
        static string IconFor(string type)
        {
            if (string.IsNullOrEmpty(type)) return VanillaSprites.IconMove;
            if (type.IndexOf("Archer", System.StringComparison.OrdinalIgnoreCase) >= 0) return VanillaSprites.IconArchers;
            if (type.IndexOf("Twohanded", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("AxeThrower", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf("Tank", System.StringComparison.OrdinalIgnoreCase) >= 0) return VanillaSprites.IconPikemen;
            return VanillaSprites.IconInfantry;
        }

        internal static void Draw()
        {
            if (!Visible || VanillaUI.ModalShowing) return;

            MenuSkin.Ensure();
            _laidOut = false;

            // 量宽度（文字都是运行时值，中英自适应），放不下就少显示几格
            _widths.Clear();
            float total = Pad * 2f;
            for (int i = 0; i < _slots.Count; i++)
            {
                float w = IconSize + 6f + MenuSkin.Measure(Label(_slots[i]), MenuSkin.Line) + Pad * 2f;
                if (total + w + (_widths.Count > 0 ? Gap : 0f) > Screen.width - 16f) break;

                _widths.Add(w);
                total += w + (_widths.Count > 1 ? Gap : 0f);
            }
            if (_widths.Count == 0) return;

            _firstX = (Screen.width - total) * 0.5f;
            _rect = new Rect(_firstX, Screen.height - SlotH - Bottom, total, SlotH);
            _laidOut = true;
            MenuSkin.PanelBg(_rect);

            float cx = _rect.x + Pad;
            bool anyHover = false;
            for (int i = 0; i < _widths.Count; i++)
            {
                Slot slot = _slots[i];
                Rect r = new Rect(cx, _rect.y + 4f, _widths[i], SlotH - 8f);
                bool selected = MarqueeSelect.IsSelected(slot.rep);
                bool hover = Hover(r);
                anyHover |= hover;
                MenuSkin.SlotBg(r, hover, selected);

                Color tint = RemoteGroup.IsControlled(slot.rep) ? new Color(0.52f, 0.30f, 0.02f, 1f) : new Color(0.16f, 0.13f, 0.09f, 0.9f);
                Sprite icon = VanillaSprites.Get(slot.icon);
                if (icon == null) icon = VanillaSprites.Get(VanillaSprites.IconSwords);
                if (icon == null) icon = VanillaSprites.Get(VanillaSprites.IconMove);
                VanillaSprites.DrawIcon(new Rect(r.x + Pad, r.y + (r.height - IconSize) * 0.5f, IconSize, IconSize), icon, tint);

                MenuSkin.DrawRow(new Rect(r.x + Pad + IconSize + 6f, r.y, r.width - IconSize - Pad * 2f - 6f, r.height),
                    Label(slot), selected);

                cx += _widths[i] + Gap;
            }

            if (anyHover)
                MenuSkin.DrawHint(new Rect(_rect.x, _rect.y - 20f, _rect.width, 18f), Loc.T("点击头像 = 选中整队（Shift 并入）"));
        }

        static string Label(Slot s)
        {
            return UnitNames.Of(s.type) + " ×" + s.count;
        }

        static bool Hover(Rect r)
        {
            Event e = Event.current;
            return (e != null) && r.Contains(e.mousePosition);
        }

        /// <summary>条上的点击：左键 = 选中整队（按住 Shift = 并入 / 移出）；在投放模式里点它会先收起菜单。</summary>
        static void HandleClick()
        {
            if (!Visible || !_laidOut || VanillaUI.ModalShowing) return;
            if (!Input.GetMouseButtonDown(0)) return;

            Vector2 pos = Input.mousePosition;
            if (!Contains(pos)) return;

            Vector2 gui = new Vector2(pos.x, Screen.height - pos.y);
            for (int i = 0; i < _widths.Count; i++)
            {
                Slot slot = _slots[i];
                if (slot == null || slot.fu == null) continue;
                if (!SlotRect(i).Contains(gui)) continue;

                if (IngameMenu.IsOpen) IngameMenu.Close();     // 点头像 = 要指挥：先收起投放菜单
                VanillaUI.Click();
                MarqueeSelect.SelectUnitAt(slot.fu, MarqueeSelect.ShiftHeld(), true);
                return;
            }
        }

        /// <summary>第 i 格（用上一帧的布局；越界返回空矩形）。</summary>
        static Rect SlotRect(int index)
        {
            if (!_laidOut || index < 0 || index >= _widths.Count) return new Rect(0f, 0f, 0f, 0f);

            float x = _rect.x + Pad;
            for (int i = 0; i < index; i++) x += _widths[i] + Gap;
            return new Rect(x, _rect.y + 4f, _widths[index], SlotH - 8f);
        }
    }
}
