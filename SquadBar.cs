using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthNewMode
{
    /// <summary>屏幕底部的**小队头像条**：把场上可选的小队排成一行，点一下直接选中整队（v1.6.1 新功能）。</summary>
    internal static class SquadBar
    {
        sealed class Slot
        {
            internal string type;
            internal readonly List<ForeignUnit> members = new List<ForeignUnit>();   // 同兵种的全部可选单位（跨船/跨次投放都算一队）
            internal Agent rep;        // 代表：状态查询 / 头像用
            internal Sprite portrait;  // 原版兵种头像（VikingReference.sprite2）
            internal string countText; // "×N"（在 Scan 里生成一次，避免每帧拼字符串）
            internal bool controlled;  // 这一类里是否已有受控小队（在 Scan 里算一次，避免每帧 O(N) 扫描）
        }

        const float SlotH = 60f;
        const float IconSize = 46f;
        const float Pad = 10f;
        const float Gap = 8f;
        const float Bottom = 12f;

        static readonly List<Slot> _slots = new List<Slot>();
        static readonly List<float> _widths = new List<float>();   // 上一帧的布局（点击判定与绘制共用）
        static Rect _rect;
        static bool _laidOut;
        static float _nextScan;
        static float _avoidY = float.MaxValue;     // 原版技能条上沿（0.2s 缓存的）
        static float _nextAvoidScan;

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

        /// <summary>按**兵种**分组（与遥控组同一套口径：同兵种的两船兵 = 一格）；人多的排前面。</summary>
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

                Slot slot = Find(f.unitType);
                if (slot == null)
                {
                    slot = new Slot();
                    slot.type = f.unitType;
                    _slots.Add(slot);
                }

                slot.members.Add(f);
                if (slot.rep == null)
                {
                    slot.rep = a;
                    slot.portrait = UnitPortraits.Of(a, f.unitType);   // 头像只在这里取（0.3s 一次）
                }
            }

            SortByCount();
            int max = Mathf.Max(1, Util.V(ModConfig.SquadBarMax, 8));
            if (_slots.Count > max) _slots.RemoveRange(max, _slots.Count - max);

            for (int i = 0; i < _slots.Count; i++)       // 每 0.3s 才算一次的"贵"信息（人数文本 / 是否受控）
            {
                Slot s = _slots[i];
                s.countText = "×" + s.members.Count;
                s.controlled = AnyControlled(s);
            }
        }

        static Slot Find(string type)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                if (string.Equals(s.type, type, System.StringComparison.OrdinalIgnoreCase)) return s;
            }
            return null;
        }

        static void SortByCount()
        {
            for (int i = 1; i < _slots.Count; i++)
            {
                Slot v = _slots[i];
                int j = i - 1;
                while (j >= 0 && _slots[j].members.Count < v.members.Count) { _slots[j + 1] = _slots[j]; j--; }
                _slots[j + 1] = v;
            }
        }

        internal static void Draw()
        {
            if (!Visible || VanillaUI.ModalShowing) return;

            MenuSkin.Ensure();
            _laidOut = false;

            // 量宽度：每格 = 头像 + 兵种名 + ×人数（名字与数字都实测 → 绝不裁字）
            _widths.Clear();
            float total = Pad * 2f;
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot s = _slots[i];
                float w = Pad + IconSize + 8f + MenuSkin.Measure(Label(s), MenuSkin.SlotName)
                        + 8f + MenuSkin.Measure(s.countText, MenuSkin.SlotNum) + Pad;
                if (total + w + (_widths.Count > 0 ? Gap : 0f) > Screen.width - 16f) break;

                _widths.Add(w);
                total += w + (_widths.Count > 1 ? Gap : 0f);
            }
            if (_widths.Count == 0) return;

            _rect = new Rect(AlignX(total), BarY(), total, SlotH);
            _laidOut = true;
            MenuSkin.PanelBg(_rect);

            float cx = _rect.x + Pad;
            bool anyHover = false;
            for (int i = 0; i < _widths.Count; i++)
            {
                Slot slot = _slots[i];
                Rect r = new Rect(cx, _rect.y + 5f, _widths[i], SlotH - 10f);
                bool selected = MarqueeSelect.IsSelected(slot.rep);
                bool hover = Hover(r);
                anyHover |= hover;
                MenuSkin.SlotBg(r, hover, selected);

                Rect icon = new Rect(r.x + Pad, r.y, IconSize, r.height);
                MenuSkin.PhotoBg(icon);
                if (slot.portrait != null)
                {
                    VanillaSprites.DrawPortrait(new Rect(icon.x + 2f, icon.y + 2f, icon.width - 4f, icon.height - 4f), slot.portrait, Color.white);
                }
                else
                {
                    Color tint = slot.controlled ? new Color(0.52f, 0.30f, 0.02f, 1f) : new Color(0.16f, 0.13f, 0.09f, 0.9f);
                    Sprite fallback = VanillaSprites.Get(VanillaSprites.IconInfantry);
                    if (fallback == null) fallback = VanillaSprites.Get(VanillaSprites.IconSwords);
                    VanillaSprites.DrawIcon(new Rect(icon.x + 4f, icon.y + 4f, icon.width - 8f, icon.height - 8f), fallback, tint);
                }

                float numW = MenuSkin.Measure(slot.countText, MenuSkin.SlotNum);
                float nameX = icon.xMax + 8f;
                MenuSkin.DrawSlotName(new Rect(nameX, r.y, r.xMax - Pad - numW - 6f - nameX, r.height), Label(slot));
                MenuSkin.DrawSlotNum(new Rect(r.xMax - Pad - numW, r.y, numW, r.height), slot.countText);

                cx += _widths[i] + Gap;
            }

            if (anyHover)
                MenuSkin.DrawHint(new Rect(_rect.x, _rect.y - 20f, _rect.width, 18f), Loc.T("点头像 = 选中整队；Shift 并入"));
        }

        /// <summary>横向：`[UI] SquadBarAlign` = Left / Center（默认）/ Right。</summary>
        static float AlignX(float total)
        {
            string a = Util.V(ModConfig.SquadBarAlign, null);
            float x = (Screen.width - total) * 0.5f;                       // 默认居中
            if (!string.IsNullOrEmpty(a))
            {
                if (a.StartsWith("L", System.StringComparison.OrdinalIgnoreCase)) x = 12f;
                else if (a.StartsWith("R", System.StringComparison.OrdinalIgnoreCase)) x = Screen.width - total - 12f;
            }
            return Mathf.Clamp(x, 4f, Mathf.Max(4f, Screen.width - total - 4f));
        }

        /// <summary>纵向：`[UI] SquadBarBottom &lt; 0` = 自动（贴底，但**原版"选中我方小队"的技能条出现时自动抬到它上面**）；&gt;= 0 = 固定离底像素。</summary>
        static float BarY()
        {
            int cfg = Util.V(ModConfig.SquadBarBottom, -1);
            float y = (cfg >= 0) ? (Screen.height - SlotH - cfg) : (Screen.height - SlotH - Bottom);

            if (cfg < 0)
            {
                float avoid = VanillaBottomTop();
                if (avoid < float.MaxValue) y = Mathf.Min(y, avoid - SlotH - 6f);
            }

            return Mathf.Clamp(y, 8f, Mathf.Max(8f, Screen.height - SlotH - 8f));
        }

        /// <summary>读原版技能条的屏幕矩形（选中我方小队时才激活）；读不到返回 MaxValue = 不避让。**每 0.2s 才查一次**：这函数要遍历全部已加载对象，不能每帧调。</summary>
        static float VanillaBottomTop()
        {
            if (Time.unscaledTime < _nextAvoidScan) return _avoidY;
            _nextAvoidScan = Time.unscaledTime + 0.2f;
            _avoidY = float.MaxValue;

            try
            {
                Object[] all = Resources.FindObjectsOfTypeAll(typeof(ActiveAbilityButtonContainer));
                for (int i = 0; i < all.Length; i++)
                {
                    ActiveAbilityButtonContainer c = all[i] as ActiveAbilityButtonContainer;
                    if (c == null || !c.gameObject.activeInHierarchy) continue;

                    RectTransform rt = c.transform as RectTransform;
                    if (rt == null) continue;

                    float h = rt.rect.height * rt.lossyScale.y;
                    if (h < 4f) continue;

                    _avoidY = Screen.height - (rt.position.y + h * 0.5f);      // 换成 IMGUI 的 y（向下）
                    break;
                }
            }
            catch { }

            return _avoidY;
        }

        static string Label(Slot s)
        {
            return UnitNames.Of(s.type);
        }

        /// <summary>这一类兵里是否已有受控小队（头像格标色用）。</summary>
        static bool AnyControlled(Slot s)
        {
            for (int i = 0; i < s.members.Count; i++)
            {
                ForeignUnit f = s.members[i];
                if (f != null && RemoteGroup.IsControlled(f.agent)) return true;
            }
            return false;
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
                if (slot == null || slot.members.Count == 0) continue;
                if (!SlotRect(i).Contains(gui)) continue;

                if (IngameMenu.IsOpen) IngameMenu.Close();     // 点头像 = 要指挥：先收起投放菜单
                VanillaUI.Click();
                MarqueeSelect.SelectMany(slot.members, MarqueeSelect.ShiftHeld());
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
