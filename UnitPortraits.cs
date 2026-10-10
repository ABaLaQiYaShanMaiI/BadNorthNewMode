using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>原版**兵种头像**：`VikingReference.sprite2`（= 结算击杀统计 / 大地图兵种预览用的那张），按兵种名缓存。见 §9。</summary>
    internal static class UnitPortraits
    {
        static readonly Dictionary<string, Sprite> _map = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
        static float _nextScan;

        /// <summary>取该单位的头像：先读它自己的 `VikingAgent.vikingReference.sprite2`（最准），再退回按兵种名的全局表。</summary>
        internal static Sprite Of(Agent agent, string unitType)
        {
            Sprite sp = FromAgent(agent);
            if (sp != null) return sp;
            return Of(unitType);
        }

        internal static Sprite Of(string unitType)
        {
            if (string.IsNullOrEmpty(unitType)) return null;

            Sprite sp;
            if (_map.TryGetValue(unitType, out sp) && sp != null) return sp;

            Refresh();
            return (_map.TryGetValue(unitType, out sp) && sp != null) ? sp : null;
        }

        static Sprite FromAgent(Agent agent)
        {
            if (agent == null) return null;

            try
            {
                VikingAgent va = agent.GetComponent<VikingAgent>();
                if (va == null || va.vikingReference == null) return null;
                return va.vikingReference.sprite2;
            }
            catch { return null; }
        }

        /// <summary>遍历原版引用表（`LevelStateObjectReferences`）把"兵种名 → 头像"补齐；找不到就下次再试。</summary>
        static void Refresh()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;

            try
            {
                IEnumerable<VikingReference> all = LevelStateObjectReferences.GetReferencedObjects<VikingReference>();
                if (all == null) return;

                foreach (VikingReference vr in all)
                {
                    if (vr == null || vr.sprite2 == null || string.IsNullOrEmpty(vr.name)) continue;
                    _map[vr.name] = vr.sprite2;
                }
            }
            catch { }
        }
    }
}
