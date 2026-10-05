using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>一次投放的目标要素（预览与实投共用）。</summary>
    internal struct DropTarget
    {
        internal Beaches.Beach.Pos beach;             // 首选滩头（预览标记落点）
        internal List<Beaches.Beach.Pos> candidates;  // 全部候选（由近到远），TryPlace 逐个尝试
        internal VikingReference vikingRef;
        internal Longship shipPrefab;
        internal int squadSize;
        internal Vector3 dir;
        internal float shoreDist;
        internal bool relaxed;        // 用了"最低不重叠间距"（附近找不到更宽的位置）
        internal bool fallback;       // 改用了全岛最近的可投放滩头
    }

    /// <summary>投到哪里：滩头筛选、落差校验、占用判定、进近廊道预检（纯查询，不建对象）。</summary>
    internal static class DropPlanner
    {
        static Island _cacheIsland;
        static List<Beaches.Beach.Pos> _cacheBeaches;

        /// <summary>本关岸线采样点（生成后不变，缓存以避免预览每帧重建）。</summary>
        internal static List<Beaches.Beach.Pos> BeachPositions(Island island)
        {
            if (!object.ReferenceEquals(_cacheIsland, island) || _cacheBeaches == null)
            {
                _cacheIsland = island;
                List<Beaches.Beach.Pos> list = (island.beaches != null) ? island.beaches.GetBeachPositions(0.1f) : null;
                _cacheBeaches = list ?? new List<Beaches.Beach.Pos>();
            }
            return _cacheBeaches;
        }

        /// <summary>把点击的陆地解析成投放目标：点击处与落点都要与海面齐平，且距离 ≤ MaxShoreDistance。</summary>
        internal static bool TryResolve(Island island, Vector3 landPoint, out DropTarget target, out string reason)
        {
            target = default(DropTarget);
            reason = null;

            Raid raid = island.raid;
            if (raid == null || raid.landingContainer == null) { reason = Loc.T("Raid / landingContainer 未就绪"); return false; }

            List<VikingReference> pool = (island.levelNode != null) ? island.levelNode.enemies : null;
            List<Longship> ships = (island.levelNode != null) ? island.levelNode.possibleShips : null;
            if (pool == null || pool.Count == 0) { reason = Loc.T("敌人生成池为空"); return false; }
            if (ships == null || ships.Count == 0) { reason = Loc.T("possibleShips 为空"); return false; }
            if (island.beaches == null) { reason = Loc.T("Beaches 未就绪"); return false; }

            VikingReference vikingRef = UnitCatalog.PickEnemy(island, pool, Util.V(ModConfig.EnemyName, null));
            if (vikingRef == null || vikingRef.agent == null) { reason = Loc.T("没有可用的 VikingReference"); return false; }

            float seaY = Util.V(ModConfig.WaterLevelY, 0f);
            float maxH = Util.V(ModConfig.MaxLandHeight, 0.5f);
            if (!CheckClickHeight(landPoint, seaY, maxH, out reason)) return false;

            int squadSize = UnitCatalog.EffectiveSquadSize(island, ships, vikingRef, Util.V(ModConfig.SquadSize, 0));
            Longship ship = UnitCatalog.PickShipForCount(island, vikingRef, squadSize);   // 人数 → 自动配"装得下的最小船"
            if (ship == null) { reason = Loc.T("没有可用长船"); return false; }

            float maxShore = Mathf.Max(0.5f, Util.V(ModConfig.MaxShoreDistance, 3f));
            List<Beaches.Beach.Pos> cand;
            List<float> candSq;
            if (!CollectCandidates(island, landPoint, ship, seaY, maxH, maxShore, out cand, out candSq, out reason)) return false;

            // 两级间距：首选"基础 + 船长"；附近找不到就放宽到"只要不重叠"（原版本就是朝向盒不相交，见 §4 坑表）
            float baseSpacing = Mathf.Max(0f, Util.V(ModConfig.MinLandingSpacing, 1f));
            float prefer = baseSpacing + ship.length;
            float minimal = Mathf.Max(0.35f, ship.radius * 2f);

            Beaches.Beach.Pos pick;
            int pickIdx;
            float pickSq;
            string occWho;
            float occDist;
            int tier;
            bool ok = TryPick(island, raid, cand, candSq, ship, prefer, minimal,
                out pick, out pickIdx, out pickSq, out occWho, out occDist, out tier);

            // 兜底：附近实在没有 → 全岛找最近的可投放滩头（会在提示里写明实际距离）
            bool fallback = false;
            if (!ok && Util.V(ModConfig.LandingFallbackAnywhere, true))
            {
                List<Beaches.Beach.Pos> far;
                List<float> farSq;
                string farReason;
                if (CollectCandidates(island, landPoint, ship, seaY, maxH, float.MaxValue, out far, out farSq, out farReason))
                {
                    ok = TryPick(island, raid, far, farSq, ship, prefer, minimal,
                        out pick, out pickIdx, out pickSq, out occWho, out occDist, out tier);
                    if (ok) { cand = far; candSq = farSq; fallback = true; }
                }
            }

            if (!ok)
            {
                reason = (occWho == null)
                    ? Loc.T("附近与全岛的滩头进近廊道都被地形/建筑挡住了——换个位置点")
                    : Loc.F("附近的滩头都被船占着（最近一艘 {0} 离 {1:F1}m，本船需要 ≥{2:F1}m），全岛也没有空位",
                        occWho, Mathf.Sqrt(occDist), minimal);
                return false;
            }

            // 选中的滩头排到首位（预览与试投放都先试它）
            List<Beaches.Beach.Pos> ordered = new List<Beaches.Beach.Pos>();
            List<float> orderedSq = new List<float>();
            ordered.Add(pick);
            orderedSq.Add(pickSq);
            for (int i = 0; i < cand.Count; i++)
            {
                if (i == pickIdx) continue;
                ordered.Add(cand[i]);
                orderedSq.Add(candSq[i]);
            }

            if (Util.V(ModConfig.VerboseLog, false))
                Util.Log(Loc.F("[NewMode] 候选 {0} 个，选中第 {1} 个（阈值 {2:F1}m{3}{4}）",
                    cand.Count, pickIdx + 1, (tier == 0) ? prefer : minimal,
                    (tier == 1) ? Loc.T("，已放宽间距") : "", fallback ? Loc.T("，已改用全岛最近空滩头") : ""));

            target.beach = pick;
            target.candidates = ordered;
            target.vikingRef = vikingRef;
            target.shipPrefab = ship;
            target.squadSize = squadSize;
            target.relaxed = (tier == 1);
            target.fallback = fallback;
            target.dir = pick.dir + pick.navPos.pos.normalized * 0.3f;   // 照抄原版 Raid 的朝向公式
            target.shoreDist = Mathf.Sqrt(pickSq);
            return true;
        }

        /// <summary>落差校验①：点击到的地块本身必须与海面齐平。</summary>
        static bool CheckClickHeight(Vector3 landPoint, float seaY, float maxH, out string reason)
        {
            float h = Mathf.Abs(landPoint.y - seaY);
            if (h <= maxH) { reason = null; return true; }
            reason = Loc.F("这里是高地/悬崖（海拔 {0:F2}m，上限 {1:F2}m）——请点与海面齐平的滩头", h, maxH);
            return false;
        }

        /// <summary>候选滩头：岸线余量足够（同原版）+ 与海面齐平 + 离点击处不超过 maxShore，按距离由近到远（maxShore = float.MaxValue 表示全岛）。</summary>
        static bool CollectCandidates(Island island, Vector3 landPoint, Longship ship, float seaY, float maxH, float maxShore,
            out List<Beaches.Beach.Pos> cand, out List<float> candSq, out string reason)
        {
            reason = null;
            cand = new List<Beaches.Beach.Pos>();
            candSq = new List<float>();

            List<Beaches.Beach.Pos> positions = BeachPositions(island);
            if (positions.Count == 0) { reason = Loc.T("本关没有可用滩头"); return false; }

            float radius = ship.radius;
            float maxSq = (maxShore >= 1e6f) ? float.MaxValue : maxShore * maxShore;
            float nearSq = float.MaxValue;

            for (int i = 0; i < positions.Count; i++)
            {
                Beaches.Beach.Pos p = positions[i];
                if (p.distToEdge <= radius) continue;                          // 岸线余量不足，原版同样不用
                if (Mathf.Abs(p.navPos.pos.y - seaY) > maxH) continue;         // 落差校验②：落点滩头也必须在海平面

                Vector3 flat = landPoint - p.navPos.pos;
                flat.y = 0f;
                float sq = flat.sqrMagnitude;
                if (sq < nearSq) nearSq = sq;
                if (sq > maxSq) continue;
                InsertSorted(cand, candSq, p, sq);                             // 近的排前面
            }

            if (cand.Count > 0) return true;
            reason = Loc.F("附近没有与海面齐平的滩头（最近 {0:F1}m，上限 {1:F1}m；或岸线余量不足）",
                Mathf.Sqrt(nearSq), maxShore);
            return false;
        }

        /// <summary>候选里挑第一个"廊道通 + 没被占"的滩头：先按首选间距（基础+船长），再放宽到最低不重叠间距。</summary>
        static bool TryPick(Island island, Raid raid, List<Beaches.Beach.Pos> cand, List<float> candSq, Longship ship,
            float prefer, float minimal, out Beaches.Beach.Pos pick, out int pickIdx, out float pickSq,
            out string occWho, out float occDist, out int tier)
        {
            pick = default(Beaches.Beach.Pos);
            pickIdx = -1;
            pickSq = 0f;
            occWho = null;
            occDist = float.MaxValue;
            tier = -1;

            for (int t = 0; t < 2; t++)
            {
                float spacing = (t == 0) ? prefer : minimal;

                for (int i = 0; i < cand.Count; i++)
                {
                    Beaches.Beach.Pos c = cand[i];
                    Vector3 d = c.dir + c.navPos.pos.normalized * 0.3f;
                    if (!CorridorClear(island, c, d, ship)) continue;         // 进近廊道被 Modules 挡住 → 换下一个

                    float dist;
                    string who;
                    if (IsOccupied(island, raid, c.navPos.pos, spacing, out dist, out who))
                    {
                        if (dist < occDist) { occDist = dist; occWho = who; } // 记下最近的占用，供失败提示用
                        continue;
                    }

                    pick = c;
                    pickIdx = i;
                    pickSq = candSq[i];
                    tier = t;
                    return true;
                }
            }
            return false;
        }

        /// <summary>按 key 升序插入（候选滩头数量少，插入排序足够）。</summary>
        static void InsertSorted(List<Beaches.Beach.Pos> list, List<float> keys, Beaches.Beach.Pos value, float key)
        {
            int i = list.Count;
            while (i > 0 && keys[i - 1] > key) i--;
            list.Insert(i, value);
            keys.Insert(i, key);
        }

        /// <summary>复刻 TryPlace 的最后一步：进近廊道不得被 Modules 挡住（起点算法同 ShipTravel）。</summary>
        static bool CorridorClear(Island island, Beaches.Beach.Pos p, Vector3 dir, Longship ship)
        {
            float num = 50f;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.000001f) return false;
            dir = dir.normalized;

            Vector3 endPos = p.navPos.pos;
            endPos.y = 0f;
            Vector3 startPos = endPos + dir * num;                  // ShipTravel: startPos = endPos - direction*50，而 direction = -dir
            Ray ray = new Ray(startPos, -dir);
            CapsuleCollider fog = (island.fog != null) ? island.fog.capsuleCollider : null;
            RaycastHit hit;
            if (fog != null && fog.Raycast(ray, out hit, num)) startPos = hit.point;
            else startPos = endPos + dir * 6f;

            float distance = Vector3.Distance(startPos, endPos);
            float radius = ship.radius;
            Ray back = new Ray(endPos + dir * distance, -dir);
            return !Physics.SphereCast(back, radius, distance - radius * 2f, LayerMaster.moduleMask);
        }

        /// <summary>全岛已放置的 Landing（含自家船 → 自家船也参与占位互斥，见 §4 坑表"叠船"）。</summary>
        internal static List<Landing> CollectPlaced(Raid raid)
        {
            List<Landing> list = new List<Landing>();
            if (raid == null || raid.landingContainer == null) return list;

            Landing[] all = raid.landingContainer.GetComponentsInChildren<Landing>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Landing l = all[i];
                if (l != null && l.placed) list.Add(l);
            }
            return list;
        }

        static Island _occIsland;
        static bool _occDirty = true;
        static readonly List<Vector3> _occPos = new List<Vector3>();
        static readonly List<string> _occName = new List<string>();

        /// <summary>已放置船只的位置缓存（悬停预览每帧都会判定占用）；投放成功 / 清场 / 换岛时失效。</summary>
        internal static void InvalidateOccupancy()
        {
            _occDirty = true;
        }

        static void RebuildOccupancy(Island island, Raid raid)
        {
            _occPos.Clear();
            _occName.Clear();

            List<Landing> placed = CollectPlaced(raid);
            for (int i = 0; i < placed.Count; i++)
            {
                Landing l = placed[i];
                if (l == null) continue;
                _occPos.Add(l.navPos.pos);
                _occName.Add((l.spawnedShip != null) ? l.spawnedShip.name : Loc.T("已放置的船"));
            }
            _occIsland = island;
            _occDirty = false;
        }

        /// <summary>该位置是否已被某艘船占用（XZ 距离 &lt; spacing）；返回最近的船名与距离，供提示/预览用。</summary>
        static bool IsOccupied(Island island, Raid raid, Vector3 pos, float spacing, out float dist, out string who)
        {
            dist = float.MaxValue;
            who = null;

            if (_occDirty || !object.ReferenceEquals(_occIsland, island)) RebuildOccupancy(island, raid);

            for (int i = 0; i < _occPos.Count; i++)
            {
                Vector3 a = _occPos[i];
                a.y = pos.y;
                float d = Vector3.Distance(a, pos);
                if (d < dist) { dist = d; who = _occName[i]; }
            }
            return dist < spacing;
        }
    }
}
