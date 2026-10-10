using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>非原生单位标记 + 注册表（框选/成组唯一数据源）；可选（含船上）与可下令（已下船）分开，v1.5.6。</summary>
    internal sealed class ForeignUnit : MonoBehaviour
    {
        internal Agent agent;
        internal string unitType;      // VikingReference.name，成组键
        internal bool native;          // true = 原版上岛的敌人（`[Native] RemoteNativeUnits` 开启后才登记）

        static readonly List<ForeignUnit> _all = new List<ForeignUnit>();
        internal static List<ForeignUnit> All { get { return _all; } }

        internal static void Attach(Agent agent, string type)
        {
            Attach(agent, type, false);
        }

        internal static void Attach(Agent agent, string type, bool native)
        {
            if (agent == null) return;

            ForeignUnit f = agent.GetComponent<ForeignUnit>();
            if (f == null) f = agent.gameObject.AddComponent<ForeignUnit>();

            f.agent = agent;
            f.unitType = type;
            f.native = native;
            if (!_all.Contains(f)) _all.Add(f);
        }

        /// <summary>可被选中：还活着、已生成——**含仍在船上**的（v1.5.6 起可以在船上就选好）。</summary>
        internal static bool Selectable(Agent a)
        {
            return a != null && a.spawned.active && a.aliveState.active;
        }

        /// <summary>可被下令：还必须已下船（`navPos.island`）。船上时 order 归 `Pirate`，抢走会卡住下船 → 只记意图。</summary>
        internal static bool Commandable(Agent a)
        {
            return Selectable(a) && a.navPos.island;
        }

        internal static void Prune()          // 剔除已销毁条目（框选 / 组刷新前调用；Unity 的 null 判定能识别已销毁对象）
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                ForeignUnit f = _all[i];
                if (f == null || f.agent == null) _all.RemoveAt(i);
            }
        }

        internal static int SelectableCount(bool native)
        {
            int n = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                ForeignUnit f = _all[i];
                if (f == null || f.native != native) continue;
                if (Selectable(f.agent)) n++;
            }
            return n;
        }

        internal static int UsableCount()
        {
            int n = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                ForeignUnit f = _all[i];
                if (f != null && Selectable(f.agent)) n++;
            }
            return n;
        }

        /// <summary>清场用：只销毁**我们投放的**单位（原生单位交给原版）；见 §4 坑表。</summary>
        internal static int DestroyAll()
        {
            int n = 0;
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                ForeignUnit f = _all[i];
                if (f == null) { _all.RemoveAt(i); continue; }
                if (f.native) continue;

                Agent a = f.agent;
                if (a != null) { UnityEngine.Object.Destroy(a.gameObject); n++; }
                _all.RemoveAt(i);
            }
            return n;
        }

        internal static void ForgetNative()   // 换岛 / 离开战局时撤掉原生单位的登记（不销毁对象）
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                ForeignUnit f = _all[i];
                if (f != null && f.native) _all.RemoveAt(i);
            }
        }

        void OnDestroy()
        {
            _all.Remove(this);
        }
    }
}

