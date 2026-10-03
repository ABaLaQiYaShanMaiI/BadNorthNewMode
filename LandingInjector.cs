using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>
    /// 只用原版对象树 + 原版协程投放敌舰：Wave → ShipGroup → Landing → ShipLoad，
    /// 再走 Spawn() / BeginWave()，因此"驶来 → 靠岸 → 下船 → 转入战斗"全部由原版逻辑承担。
    /// </summary>
    internal static class LandingInjector
    {
        /// <summary>投放一艘敌舰。失败时 info 里是原因；失败会自动清理已建对象。</summary>
        internal static bool TrySpawn(Island island, Vector3 waterPoint, out string info)
        {
            info = null;
            Raid raid = island.raid;
            if (raid == null || raid.landingContainer == null) { info = "Raid / landingContainer 未就绪"; return false; }

            List<VikingReference> pool = (island.levelNode != null) ? island.levelNode.enemies : null;
            List<Longship> ships = (island.levelNode != null) ? island.levelNode.possibleShips : null;
            if (pool == null || pool.Count == 0) { info = "敌人生成池为空"; return false; }
            if (ships == null || ships.Count == 0) { info = "possibleShips 为空"; return false; }
            if (island.beaches == null) { info = "Beaches 未就绪"; return false; }

            VikingReference vikingRef = PickEnemy(pool, ModConfig.EnemyName.Value);
            if (vikingRef == null || vikingRef.agent == null) { info = "没有可用的 VikingReference"; return false; }

            // ---- 原版对象树，顺序照抄 Raid.IIslandFirstEnter ----
            GameObject waveGo = new GameObject("ModWave");
            Wave wave = waveGo.AddComponent<Wave>();
            wave.raid = raid;
            wave.transform.SetParent(raid.landingContainer, false); // 不进 raid.waves → 原版波次计时不受影响

            GameObject groupGo = new GameObject("Group");
            ShipGroup group = groupGo.AddComponent<ShipGroup>();
            wave.AddShipGroup(group);                               // 回填 group.wave + SetParent

            GameObject landingGo = new GameObject("Landing");
            Landing landing = landingGo.AddComponent<Landing>();
            group.AddLanding(landing);                              // 回填 landing.shipGroup + SetParent
            landing.Init(island);                                   // island + landings 层 + timeOffset

            GameObject loadGo = new GameObject("Load");
            ShipLoad load = loadGo.AddComponent<ShipLoad>();
            load.vikingRef = vikingRef;
            load.count = ClampSquadSize(ships, vikingRef, ModConfig.SquadSize.Value);
            landing.AddShipLoad(load);                              // 回填 load.landing + SetParent

            landing.shipPrefab = PickShip(ships, landing);          // 必须在 AddShipLoad 之后（要用 agentArea）

            // ---- 选滩头 ----
            Beaches.Beach.Pos beach;
            float shoreDist;
            if (!TryPickBeach(island, landing, waterPoint, out beach, out shoreDist))
            {
                UnityEngine.Object.Destroy(waveGo);
                info = string.Format("附近没有可用滩头（最近岸线 {0:F1}m / 上限 {1:F1}m，或方向校验未过）",
                    shoreDist, ModConfig.MaxShoreDistance.Value);
                return false;
            }

            Vector3 dir = waterPoint - beach.navPos.pos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.000001f) dir = beach.dir; else dir.Normalize();

            if (ModConfig.VerboseLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo(string.Format("[NewMode] 水面 {0} → 滩头 {1} 距离 {2:F2}m dir {3}",
                    Fmt(waterPoint), Fmt(beach.navPos.pos), shoreDist, Fmt(dir)));

            if (!landing.TryPlace(beach.navPos, dir, ModConfig.ShipSpeedMultiplier.Value, CollectPlaced(raid)))
            {
                UnityEngine.Object.Destroy(waveGo);
                info = "该滩头被占用或被阻挡（原版 TryPlace 返回 false）";
                return false;
            }

            // ---- 原版发射流程：预生成 → 发射协程（Launch + 靠岸到达回调 + 音乐）----
            wave.RefreshLandings();                                 // 同 Raid.IIslandPlay
            landing.Spawn();                                        // Longship + 舱内敌人（原版）
            wave.approachAudioId = vikingRef.approachAudioId;       // 同 Raid.cs 给波次赋音频的做法
            wave.arriveAudioId = vikingRef.arriveAudioId;
            raid.StartCoroutine(wave.BeginWave());                  // 原版协程：Launch() → 船开 → 下船

            info = string.Format("已投放 {0} ×{1}（船 {2}，离岸 {3:F1}m）",
                vikingRef.name, load.count, landing.shipPrefab.name, shoreDist);
            return true;
        }

        /// <summary>收集全岛已放置的 Landing —— 原版 TryPlace 靠它做占位互斥，运行时需自建。</summary>
        static List<Landing> CollectPlaced(Raid raid)
        {
            List<Landing> list = new List<Landing>();
            if (raid.waves == null) return list;
            for (int i = 0; i < raid.waves.Count; i++)
            {
                Wave wave = raid.waves[i];
                if (wave == null || wave.shipGroups == null) continue;
                for (int j = 0; j < wave.shipGroups.Count; j++)
                {
                    ShipGroup group = wave.shipGroups[j];
                    if (group == null || group.landings == null) continue;
                    for (int k = 0; k < group.landings.Count; k++)
                    {
                        Landing l = group.landings[k];
                        if (l != null && l.placed) list.Add(l);
                    }
                }
            }
            return list;
        }

        /// <summary>原版岸线采样点里挑一个：方向朝海、岸线余量够、且离点击处最近。</summary>
        static bool TryPickBeach(Island island, Landing landing, Vector3 waterPoint,
            out Beaches.Beach.Pos beach, out float bestDist)
        {
            beach = default(Beaches.Beach.Pos);
            bestDist = 0f;
            List<Beaches.Beach.Pos> positions = island.beaches.GetBeachPositions(0.1f);
            if (positions == null || positions.Count == 0) return false;

            float radius = landing.shipPrefab.radius;
            float minDot = ModConfig.MinOutwardDot.Value;
            float bestOkSq = float.MaxValue;
            float bestAnySq = float.MaxValue;
            bool found = false;
            for (int i = 0; i < positions.Count; i++)
            {
                Beaches.Beach.Pos p = positions[i];
                if (p.distToEdge <= radius) continue;               // 同原版：岸线余量不足的滩头不用

                Vector3 flat = waterPoint - p.navPos.pos;
                flat.y = 0f;
                float sq = flat.sqrMagnitude;
                if (sq < bestAnySq) bestAnySq = sq;

                // 方向校验：点击方向须与滩头朝海外法线大致同向，否则是"点在岛上/点进海湾里"
                if (minDot > 0f && sq > 0.000001f)
                {
                    Vector3 outward = p.dir;
                    outward.y = 0f;
                    if (outward.sqrMagnitude > 0.000001f &&
                        Vector3.Dot(flat.normalized, outward.normalized) < minDot) continue;
                }

                if (sq < bestOkSq) { bestOkSq = sq; found = true; beach = p; }
            }

            bestDist = Mathf.Sqrt(found ? bestOkSq : bestAnySq);
            if (!found) return false;
            return bestDist <= ModConfig.MaxShoreDistance.Value;
        }

        /// <summary>按 area 选船：照抄原版（首个 area 够用的船，否则用最后一艘）。</summary>
        static Longship PickShip(List<Longship> ships, Landing landing)
        {
            for (int i = 0; i < ships.Count; i++)
            {
                Longship s = ships[i];
                if (s != null && s.area >= landing.agentArea) return s;
            }
            for (int i = ships.Count - 1; i >= 0; i--)
                if (ships[i] != null) return ships[i];
            return null;
        }

        /// <summary>
        /// 按名字取敌人：先查本关生成池，再退回全局引用字典（保证"始终同一种小兵"不被随机化），
        /// 都取不到才随机并打警告。
        /// </summary>
        static VikingReference PickEnemy(List<VikingReference> pool, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    VikingReference v = pool[i];
                    if (v != null && string.Equals(v.name, name, System.StringComparison.OrdinalIgnoreCase)) return v;
                }

                UnityEngine.Object obj;
                if (LevelStateObjectReferences.dict.TryGetValue(name, out obj))
                {
                    VikingReference v = obj as VikingReference;
                    if (v != null && v.agent != null)
                    {
                        if (Plugin.Log != null)
                            Plugin.Log.LogInfo("[NewMode] \"" + name + "\" 不在本关生成池，改用全局引用字典里的同一单位。");
                        return v;
                    }
                }

                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("[NewMode] cfg EnemyName=\"" + name + "\" 既不在生成池也不在引用字典，退回随机。");
            }
            return pool[Random.Range(0, pool.Count)];
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

        static string Fmt(Vector3 v)
        {
            return string.Format("({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
        }
    }
}
