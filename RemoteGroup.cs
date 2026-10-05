using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>遥控组容器：**按兵种各成一个 squad**（框里混几种就分几队），只换 brain.order、不改阵营（见 PROJECT_SPEC §5）。</summary>
    internal static class RemoteGroup
    {
        /// <summary>一个兵种的受控小队。</summary>
        internal sealed class Group
        {
            internal string type;
            internal string display;
            internal readonly List<GroupOrder> orders = new List<GroupOrder>();
            internal NavSpot target;
            internal bool selected;        // 最近一次框选是否选中它 → 决定移动命令发给谁
        }

        static readonly List<Group> _groups = new List<Group>();
        static Island _island;

        internal static bool Any { get { return _groups.Count > 0; } }
        internal static int GroupCount { get { return _groups.Count; } }
        internal static Group At(int i) { return (i >= 0 && i < _groups.Count) ? _groups[i] : null; }

        internal static int TotalCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _groups.Count; i++) n += _groups[i].orders.Count;
                return n;
            }
        }

        /// <summary>HUD 用：把各队写成一串。</summary>
        internal static string DescribeAll()
        {
            if (_groups.Count == 0) return Loc.T("无");
            string s = null;
            for (int i = 0; i < _groups.Count; i++)
            {
                Group g = _groups[i];
                s = (s == null) ? "" : (s + Loc.T("、"));
                s += g.display + "×" + g.orders.Count;
            }
            return s;
        }

        static int MaxPerGroup()
        {
            int cap = Util.V(ModConfig.RemoteSoftCap, 40);
            return (cap <= 0) ? int.MaxValue : cap;      // 0 = 不限
        }

        static Group FindGroup(string type)
        {
            for (int i = 0; i < _groups.Count; i++)
                if (string.Equals(_groups[i].type, type, System.StringComparison.OrdinalIgnoreCase)) return _groups[i];
            return null;
        }

        /// <summary>框选结果 → 按兵种分队（同类并入已有队；每队上限 RemoteSoftCap，超出的保持原逻辑）。</summary>
        internal static bool Capture(List<ForeignUnit> picked, Vector2 boxCenter, out string message)
        {
            message = null;
            if (picked == null || picked.Count == 0) { message = Loc.T("没框到非原生单位"); return false; }

            // ① 按兵种分桶（框里混了多种 → 各自组队）
            List<string> types = new List<string>();
            Dictionary<string, List<ForeignUnit>> buckets =
                new Dictionary<string, List<ForeignUnit>>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < picked.Count; i++)
            {
                ForeignUnit f = picked[i];
                if (f == null || f.agent == null) continue;

                List<ForeignUnit> bucket;
                if (!buckets.TryGetValue(f.unitType, out bucket))
                {
                    bucket = new List<ForeignUnit>();
                    buckets.Add(f.unitType, bucket);
                    types.Add(f.unitType);
                }
                bucket.Add(f);
            }
            if (types.Count == 0) { message = Loc.T("没框到可用的非原生单位"); return false; }

            int cap = MaxPerGroup();
            int skipped = 0;
            string joined = null;        // 本次动作**实际涉及**的小队（不能写 DescribeAll，会把别的队也列进去）

            for (int ti = 0; ti < types.Count; ti++)
            {
                string type = types[ti];
                List<ForeignUnit> bucket = buckets[type];

                // 近的优先（配合上限：超出上限的零散单位保持原逻辑）
                bucket.Sort(delegate (ForeignUnit x, ForeignUnit y)
                {
                    return MarqueeSelect.ScreenDist2(x.agent, boxCenter)
                        .CompareTo(MarqueeSelect.ScreenDist2(y.agent, boxCenter));
                });

                Group g = FindGroup(type);
                if (g == null)
                {
                    g = new Group();
                    g.type = type;
                    g.display = bucket[0].displayName;
                    _groups.Add(g);
                }
                g.selected = true;

                for (int i = 0; i < bucket.Count; i++)
                {
                    if (g.orders.Count >= cap) { skipped += bucket.Count - i; break; }
                    Add(g, bucket[i].agent);
                }

                joined = (joined == null) ? "" : (joined + Loc.T("、"));
                joined += g.display + "×" + g.orders.Count;
            }

            MarkSelection(types);
            CaptureIsland();
            for (int i = 0; i < _groups.Count; i++) Reslot(_groups[i]);

            message = Loc.F("已接管 {0}{1}（左键点地块前进；再点/再框同兵种可并入）", joined,
                (skipped > 0) ? Loc.F("；另有 {0} 个超过每队上限 {1}，保持原逻辑", skipped, cap) : "");
            return true;
        }

        /// <summary>本次没框到的队 → 不再"选中"（移动命令不发它）。</summary>
        static void MarkSelection(List<string> types)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                Group g = _groups[i];
                bool inBox = false;
                for (int t = 0; t < types.Count; t++)
                {
                    if (string.Equals(g.type, types[t], System.StringComparison.OrdinalIgnoreCase)) { inBox = true; break; }
                }
                g.selected = inBox;
            }
        }

        static void CaptureIsland()
        {
            if (_island != null || _groups.Count == 0) return;

            Group g = _groups[0];
            if (g.orders.Count == 0 || g.orders[0] == null || g.orders[0].agent == null) return;
            _island = g.orders[0].agent.faction.island;
        }

        static bool Add(Group g, Agent a)
        {
            if (a == null || a.brain == null) return false;

            GroupOrder o = a.GetComponent<GroupOrder>();
            if (o == null) o = a.gameObject.AddComponent<GroupOrder>();
            if (g.orders.Contains(o)) return false;

            o.Init(a.brain.order, a.brain.orderMono);    // 记下接管前的 order（清场时还原，避免残留失效引用）
            o.SetTarget(g.target);
            o.SuppressHouseBurning();                    // 受控期间不许烧房子（orderDist 语义冲突 → 会隔岛扔火炬）

            a.brain.order = o;                           // WantsControl()=true → PickNewOrder 不会再换掉它
            a.brain.orderMono = o;
            g.orders.Add(o);
            return true;
        }

        /// <summary>切换"当前遥控小队"（对应原版 `SelectNextSquad` / `SelectPreviousSquad`）：把 selected 标记移到下/上一队。</summary>
        /// <summary>左键点地块：命令"本次选中的小队"前进（最近一次没框到任何队时，命令全部队）。</summary>
        internal static bool MoveTo(NavSpot target, out string message)
        {
            if (target == null) { message = Loc.T("那里不是可站立的陆地地块"); return false; }
            if (_groups.Count == 0) { message = Loc.T("还没有遥控小队：左键从单位上拖动即可框选"); return false; }

            List<Group> targets = new List<Group>();
            for (int i = 0; i < _groups.Count; i++)
                if (_groups[i].selected) targets.Add(_groups[i]);
            if (targets.Count == 0) targets.AddRange(_groups);

            string who = null;
            for (int i = 0; i < targets.Count; i++)
            {
                Group g = targets[i];

                // 第一队去点击格，其余队就地分到**相邻格**（多兵种/人多时不挤同一格；相邻格不够就都挤点击格）
                NavSpot spot = (i == 0) ? target : NeighbourSpot(target, i);
                if (spot == null) spot = target;

                g.target = spot;
                for (int k = 0; k < g.orders.Count; k++)
                    if (g.orders[k] != null) g.orders[k].SetTarget(spot);
                Reslot(g);
                who = (who == null) ? "" : (who + Loc.T("、"));
                who += g.display + "×" + g.orders.Count;
            }

            FabricWrapper.PostEvent("UI/InGame/UnitMove");     // 借用原版的移动反馈音
            message = Loc.F("遥控小队前进：{0}", who);
            return true;
        }

        /// <summary>取点击格的"第 index 个非空相邻格"（`NavSpot.neighbours[8]`），用于把多支小队就地铺开。</summary>
        static NavSpot NeighbourSpot(NavSpot center, int index)
        {
            if (center == null || center.neighbours == null) return null;

            int found = 0;
            for (int i = 0; i < center.neighbours.Length; i++)
            {
                NavSpot s = center.neighbours[i];
                if (s == null) continue;
                found++;
                if (found == index) return s;
            }
            return null;
        }

        /// <summary>每帧维护：剔除阵亡成员、被抢 order 时重新接管、换岛 / 结算时清场。</summary>
        internal static void Tick()
        {
            if (_groups.Count == 0) return;

            Island cur = null;
            for (int gi = _groups.Count - 1; gi >= 0; gi--)
            {
                Group g = _groups[gi];
                bool rosterChanged = false;

                for (int i = g.orders.Count - 1; i >= 0; i--)
                {
                    GroupOrder o = g.orders[i];
                    Agent a = (o != null) ? o.agent : null;

                    if (a == null || !a.spawned.active || !a.aliveState.active)     // 阵亡 / 被销毁
                    {
                        if (o != null) UnityEngine.Object.Destroy(o);
                        g.orders.RemoveAt(i);
                        rosterChanged = true;
                        continue;
                    }

                    if (a.brain == null) continue;
                    if (!object.ReferenceEquals(a.brain.order, o))                  // 被别的逻辑抢走 → 重新接管
                    {
                        a.brain.order = o;
                        a.brain.orderMono = o;
                    }
                    cur = a.faction.island;
                }

                if (g.orders.Count == 0) { _groups.RemoveAt(gi); continue; }
                if (rosterChanged) Reslot(g);
            }

            if (_groups.Count == 0) { _island = null; IngameMenu.Say(Loc.T("遥控小队已全部阵亡 / 消失")); return; }
            if (cur != null && !object.ReferenceEquals(cur, _island)) { ClearAll(Loc.T("换岛：已清空遥控小队")); return; }
            if (cur != null && cur.state != Island.State.Playing) { ClearAll(Loc.T("战局结束：已清空遥控小队")); return; }
        }

        /// <summary>清场（F2 / 换岛 / 结算）：还原接管前的 order 并销毁组件。T9：不提供"战斗中释放回原 AI"。</summary>
        internal static void Clear()
        {
            ClearAll(null);
        }

        static void ClearAll(string reason)
        {
            for (int gi = 0; gi < _groups.Count; gi++)
            {
                Group g = _groups[gi];
                for (int i = 0; i < g.orders.Count; i++)
                {
                    GroupOrder o = g.orders[i];
                    if (o == null) continue;

                    Agent a = o.agent;
                    if (a != null && a.brain != null && object.ReferenceEquals(a.brain.order, o))
                    {
                        a.brain.order = o.PrevOrder;
                        a.brain.orderMono = o.PrevMono;
                    }
                    o.RestoreHouseBurning();          // 交还"能烧房子"的行为
                    UnityEngine.Object.Destroy(o);
                }
            }
            _groups.Clear();
            _island = null;
            if (!string.IsNullOrEmpty(reason)) IngameMenu.Say(reason);
        }

        /// <summary>按目标格重排槽位（原版 SquadFormation，槽距 = radius*2.01）。</summary>
        static void Reslot(Group g)
        {
            bool hasTarget = g.target != null && g.orders.Count > 0;
            SquadFormation formation = default(SquadFormation);
            if (hasTarget) formation = new SquadFormation(g.orders.Count, g.target.meshBounds, g.target.lookDir);

            for (int i = 0; i < g.orders.Count; i++)
            {
                GroupOrder o = g.orders[i];
                if (o == null) continue;

                Agent a = o.agent;
                if (a == null || !hasTarget) { o.SetSlot(Vector3.zero, false); continue; }

                Vector3 offset = formation.Get(i) * (a.radius * 2f * 1.01f);
                NavPos slot = g.target.navPos;
                slot.Move(offset);                                  // 失败也无妨：Move 会贴到可达位置
                o.SetSlot(slot.wPos, true);
            }
        }
    }
}
