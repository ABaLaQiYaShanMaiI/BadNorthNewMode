using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>非原生（本 mod 投放）单位标记 + 注册表：框选与成组的唯一数据源。</summary>
    internal sealed class ForeignUnit : MonoBehaviour
    {
        internal Agent agent;
        internal string unitType;      // VikingReference.name，成组键
        internal string displayName;   // 简中名（提示用）

        static readonly List<ForeignUnit> _all = new List<ForeignUnit>();
        internal static List<ForeignUnit> All { get { return _all; } }

        internal static void Attach(Agent agent, string type)
        {
            if (agent == null) return;

            ForeignUnit f = agent.GetComponent<ForeignUnit>();
            if (f == null) f = agent.gameObject.AddComponent<ForeignUnit>();

            f.agent = agent;
            f.unitType = type;
            f.displayName = string.IsNullOrEmpty(type) ? "非原生单位" : UnitNames.Of(type);
            if (!_all.Contains(f)) _all.Add(f);
        }

        /// <summary>剔除已销毁条目（框选 / 组刷新前调用；Unity 的 null 判定能识别已销毁对象）。</summary>
        internal static void Prune()
        {
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                ForeignUnit f = _all[i];
                if (f == null || f.agent == null) _all.RemoveAt(i);
            }
        }

        void OnDestroy()
        {
            _all.Remove(this);
        }
    }
}
