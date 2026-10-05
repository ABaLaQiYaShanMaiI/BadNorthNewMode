using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>兵种 / 船型 / 装载人数的选择与查询（只读数据，不碰场景对象）。</summary>
    internal static class UnitCatalog
    {
        /// <summary>菜单兵种列表 = 本关 enemies ∪ 全局字典 Viking_*（跨关兵种靠 PickEnemy 回退，见 §5）。</summary>
        internal static List<VikingReference> AvailableUnits(Island island)
        {
            List<VikingReference> result = new List<VikingReference>();
            List<string> names = new List<string>();

            List<VikingReference> pool = (island != null && island.levelNode != null) ? island.levelNode.enemies : null;
            if (pool != null)
            {
                for (int i = 0; i < pool.Count; i++) AddUnit(result, names, pool[i]);
            }

            Dictionary<string, UnityEngine.Object> dict = LevelStateObjectReferences.dict;
            if (dict != null)
            {
                foreach (KeyValuePair<string, UnityEngine.Object> kv in dict)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    if (kv.Key.IndexOf("Viking_", System.StringComparison.Ordinal) != 0) continue;
                    AddUnit(result, names, kv.Value as VikingReference);
                }
            }

            SortByDifficulty(result);
            return result;
        }

        static void AddUnit(List<VikingReference> list, List<string> names, VikingReference vr)
        {
            if (vr == null || vr.agent == null) return;
            string n = vr.name;
            if (string.IsNullOrEmpty(n) || names.Contains(n)) return;
            names.Add(n);
            list.Add(vr);
        }

        /// <summary>按难度递增排序：主键用原版难度权重 bounty（只用于排序、不显示），再以单体面积、内部名兜底。</summary>
        static void SortByDifficulty(List<VikingReference> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                VikingReference v = list[i];
                int j = i - 1;
                while (j >= 0 && CompareDifficulty(v, list[j]) < 0) { list[j + 1] = list[j]; j--; }
                list[j + 1] = v;
            }
        }

        static int CompareDifficulty(VikingReference a, VikingReference b)
        {
            if (a == null || b == null) return 0;
            if (a.bounty != b.bounty) return (a.bounty < b.bounty) ? -1 : 1;

            float aa = (a.agent != null) ? a.agent.area : 0f;
            float ba = (b.agent != null) ? b.agent.area : 0f;
            if (Mathf.Abs(aa - ba) > 0.0001f) return (aa < ba) ? -1 : 1;

            return string.CompareOrdinal(a.name, b.name);
        }

        /// <summary>按人数选船：取"装得下该人数的最小船"；都装不下则用最大的船（人数随后会被裁到船容量）。</summary>
        internal static Longship PickShipForCount(Island island, VikingReference vr, int count)
        {
            List<Longship> ships = (island != null && island.levelNode != null) ? island.levelNode.possibleShips : null;
            if (ships == null || ships.Count == 0) return null;

            float need = ((vr != null && vr.agent != null) ? vr.agent.area : 1f) * Mathf.Max(1, count);
            Longship best = null;
            Longship biggest = null;
            for (int i = 0; i < ships.Count; i++)
            {
                Longship s = ships[i];
                if (s == null || s.col == null) continue;
                if (biggest == null || s.area > biggest.area) biggest = s;
                if (s.area >= need && (best == null || s.area < best.area)) best = s;
            }
            return (best != null) ? best : biggest;
        }

        /// <summary>默认装载数：优先 UnitNames 的梯度表（弱兵多、巨人 1 个），未收录才回退原版公式（最小船容量 ÷ 单体面积）。</summary>
        internal static int DefaultSquadSize(Island island, VikingReference vr)
        {
            if (vr == null || vr.agent == null) return 1;

            int explicitCount = UnitNames.DefaultCount(vr.name);
            if (explicitCount > 0) return explicitCount;

            if (island == null || island.levelNode == null) return 1;

            List<Longship> ships = island.levelNode.possibleShips;
            float minArea = 0f;
            for (int i = 0; i < ships.Count; i++)
            {
                Longship s = ships[i];
                if (s == null || s.area <= 0f) continue;
                if (minArea <= 0f || s.area < minArea) minArea = s.area;
            }

            float unitArea = vr.agent.area;
            if (minArea <= 0f || unitArea <= 0.0001f) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(minArea / unitArea));
        }

        /// <summary>实际装载数：cfg &gt;0 用它，否则用兵种默认值；两者都受容量上限裁剪。</summary>
        internal static int EffectiveSquadSize(Island island, List<Longship> ships, VikingReference vr, int cfgSize)
        {
            int want = (cfgSize > 0) ? cfgSize : DefaultSquadSize(island, vr);
            return ClampSquadSize(ships, vr, want);
        }

        /// <summary>人数上限 = 最大船容量 / 单人 area（与原版 SetLoadCount 同一套算法）。</summary>
        static int ClampSquadSize(List<Longship> ships, VikingReference vikingRef, int want)
        {
            float maxArea = 0f;
            for (int i = 0; i < ships.Count; i++)
                if (ships[i] != null && ships[i].area > maxArea) maxArea = ships[i].area;

            float area = (vikingRef.agent != null) ? vikingRef.agent.area : 1f;
            int cap = (area > 0.0001f) ? Mathf.Max(1, Mathf.RoundToInt(maxArea / area)) : 1;
            return Mathf.Clamp(want, 1, cap);
        }

        static Island _pickIsland;
        static string _pickName;
        static VikingReference _pickUnit;

        /// <summary>按名字取敌人（本关池 → 全局字典 → 随机）。结果按 (岛, 兵种名) 缓存：悬停预览每帧都会调用，避免重复查找与刷屏。</summary>
        internal static VikingReference PickEnemy(Island island, List<VikingReference> pool, string name)
        {
            if (string.IsNullOrEmpty(name)) return pool[Random.Range(0, pool.Count)];

            if (object.ReferenceEquals(_pickIsland, island) &&
                string.Equals(_pickName, name, System.StringComparison.Ordinal) &&
                _pickUnit != null)
            {
                return _pickUnit;
            }

            VikingReference found = null;
            for (int i = 0; i < pool.Count; i++)
            {
                VikingReference v = pool[i];
                if (v != null && string.Equals(v.name, name, System.StringComparison.OrdinalIgnoreCase)) { found = v; break; }
            }

            if (found == null && Util.V(ModConfig.AllowCrossIslandUnits, true))
            {
                UnityEngine.Object obj = null;
                if (LevelStateObjectReferences.dict.TryGetValue(name, out obj))
                {
                    VikingReference v = obj as VikingReference;
                    if (v != null && v.agent != null)
                    {
                        found = v;
                        Util.LogOnce("fallback:" + name,
                            Loc.F("[NewMode] \"{0}\" 不在本关生成池，改用全局引用字典里的同一单位。", name));
                    }
                }
            }

            if (found == null)
            {
                Util.LogOnce("missing:" + name,
                    Loc.F("[NewMode] cfg EnemyName=\"{0}\" 既不在生成池也不在引用字典，退回随机。", name));
                found = pool[Random.Range(0, pool.Count)];
            }

            _pickIsland = island;
            _pickName = name;
            _pickUnit = found;
            return found;
        }
    }
}
