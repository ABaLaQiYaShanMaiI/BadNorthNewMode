using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>一次投放的目标要素（鼠标预览与实投共用同一套解析结果）。</summary>
    internal struct DropTarget
    {
        internal Beaches.Beach.Pos beach;                    // 首选滩头（预览标记落点）
        internal List<Beaches.Beach.Pos> candidates;         // 全部候选（按离点击处由近到远），TryPlace 逐个尝试
        internal VikingReference vikingRef;
        internal Longship shipPrefab;
        internal int squadSize;
        internal Vector3 dir;
        internal float shoreDist;
    }

    /// <summary>
    /// 只用原版对象树 + 原版协程投放敌舰：Wave → ShipGroup → Landing → ShipLoad
    /// → TryPlace → Spawn → BeginWave，因此"驶来 → 靠岸 → 下船 → 转入战斗"全由原版承担。
    /// 输入是"点击到的陆地地块"：只接受与海面齐平的沙滩，悬崖/高台一律拒绝。
    /// </summary>
    internal static class LandingInjector
    {
        static Island _cacheIsland;
        static List<Beaches.Beach.Pos> _cacheBeaches;

        /// <summary>本关岸线采样点（生成后不变，缓存以免鼠标预览时每帧重建列表）。</summary>
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

        /// <summary>
        /// 把"点击到的陆地地块"解析成可投放目标：
        /// ① 点击处海拔须与海面齐平（挡掉悬崖/高台）；② 取最近的可登陆岸线点；
        /// ③ 落点滩头自身也须与海面齐平；④ 水平距离不超过 MaxShoreDistance。
        /// </summary>
        internal static bool TryResolve(Island island, Vector3 landPoint, out DropTarget target, out string reason)
        {
            target = default(DropTarget);
            reason = null;

            Raid raid = island.raid;
            if (raid == null || raid.landingContainer == null) { reason = "Raid / landingContainer 未就绪"; return false; }

            List<VikingReference> pool = (island.levelNode != null) ? island.levelNode.enemies : null;
            List<Longship> ships = (island.levelNode != null) ? island.levelNode.possibleShips : null;
            if (pool == null || pool.Count == 0) { reason = "敌人生成池为空"; return false; }
            if (ships == null || ships.Count == 0) { reason = "possibleShips 为空"; return false; }
            if (island.beaches == null) { reason = "Beaches 未就绪"; return false; }

            VikingReference vikingRef = PickEnemy(pool, ModConfig.EnemyName.Value);
            if (vikingRef == null || vikingRef.agent == null) { reason = "没有可用的 VikingReference"; return false; }

            // ---- 落差校验①：点击到的地块本身必须与海面齐平 ----
            float seaY = ModConfig.WaterLevelY.Value;
            float maxH = ModConfig.MaxLandHeight.Value;
            float clickHeight = Mathf.Abs(landPoint.y - seaY);
            if (clickHeight > maxH)
            {
                reason = string.Format("这里是高地/悬崖（海拔 {0:F2}m，上限 {1:F2}m）——请点与海面齐平的滩头",
                    clickHeight, maxH);
                return false;
            }

            int squadSize = ClampSquadSize(ships, vikingRef, ModConfig.SquadSize.Value);
            Longship ship = PickShipForLoad(ships, vikingRef, squadSize);
            if (ship == null) { reason = "没有可用长船"; return false; }

            // ---- 候选滩头：岸线余量足够（同原版）+ 与海面齐平 + 离点击处不超过上限，按距离由近到远 ----
            List<Beaches.Beach.Pos> positions = BeachPositions(island);
            if (positions.Count == 0) { reason = "本关没有可用滩头"; return false; }

            float radius = ship.radius;
            float maxShore = ModConfig.MaxShoreDistance.Value;
            float maxSq = maxShore * maxShore;

            List<Beaches.Beach.Pos> cand = new List<Beaches.Beach.Pos>();
            List<float> candSq = new List<float>();
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

            if (cand.Count == 0)
            {
                reason = string.Format("附近没有与海面齐平的滩头（最近 {0:F1}m，上限 {1:F1}m；或岸线余量不足）",
                    Mathf.Sqrt(nearSq), maxShore);
                return false;
            }

            // 优先"进近廊道没被 Modules（建筑/岩石）挡住"的候选——这正是原版 TryPlace 返回 false 的主因；
            // 若全被挡，就退回最近的候选让原版 TryPlace 自己裁决。
            int pick = 0;
            bool pickClear = false;
            for (int i = 0; i < cand.Count; i++)
            {
                if (CorridorClear(island, cand[i], cand[i].dir + cand[i].navPos.pos.normalized * 0.3f, ship))
                {
                    pick = i; pickClear = true; break;
                }
            }
            if (pick != 0)                                                     // 把首选换到 0 号位，便于 TryPlace 直接顺序尝试
            {
                Beaches.Beach.Pos tmp = cand[0]; float tmpSq = candSq[0];
                cand[0] = cand[pick]; candSq[0] = candSq[pick];
                cand[pick] = tmp; candSq[pick] = tmpSq;
            }

            if (ModConfig.VerboseLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo(string.Format("[NewMode] 候选滩头 {0} 个，首选廊道通畅={1}", cand.Count, pickClear));

            target.beach = cand[0];
            target.candidates = cand;
            target.vikingRef = vikingRef;
            target.shipPrefab = ship;
            target.squadSize = squadSize;
            target.dir = target.beach.dir + target.beach.navPos.pos.normalized * 0.3f;   // 照抄原版 Raid 的朝向公式
            target.shoreDist = Mathf.Sqrt(candSq[0]);
            return true;
        }

        /// <summary>按解析结果投放一艘敌舰；失败时 info 是原因，并自动清理已建对象。</summary>
        internal static bool TrySpawn(Island island, Vector3 landPoint, out string info)
        {
            DropTarget t;
            string reason;
            if (!TryResolve(island, landPoint, out t, out reason)) { info = reason; return false; }

            if (ModConfig.VerboseLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo(string.Format(
                    "[NewMode] 点击陆地 {0}（海拔 {1:F2}m）→ 滩头 {2} 距离 {3:F2}m 船 {4}",
                    Fmt(landPoint), landPoint.y - ModConfig.WaterLevelY.Value,
                    Fmt(t.beach.navPos.pos), t.shoreDist, t.shipPrefab.name));

            Raid raid = island.raid;

            // ---- 原版对象树，顺序照抄 Raid.IIslandFirstEnter ----
            GameObject waveGo = new GameObject("ModWave");
            Wave wave = waveGo.AddComponent<Wave>();
            wave.raid = raid;
            wave.transform.SetParent(raid.landingContainer, false);   // 不进 raid.waves → 原版波次计时不受影响
            SpawnLedger.Track(waveGo, island);                        // 登记：离开战局/换岛时统一销毁（防幽灵船）

            GameObject groupGo = new GameObject("Group");
            ShipGroup group = groupGo.AddComponent<ShipGroup>();
            wave.AddShipGroup(group);                                 // 回填 group.wave + SetParent

            GameObject landingGo = new GameObject("Landing");
            Landing landing = landingGo.AddComponent<Landing>();
            group.AddLanding(landing);                                // 回填 landing.shipGroup + SetParent
            landing.Init(island);                                     // island + landings 层 + timeOffset

            GameObject loadGo = new GameObject("Load");
            ShipLoad load = loadGo.AddComponent<ShipLoad>();
            load.vikingRef = t.vikingRef;
            load.count = t.squadSize;
            landing.AddShipLoad(load);                                // 回填 load.landing + SetParent

            landing.shipPrefab = t.shipPrefab;

            // ---- 逐个尝试候选滩头（原版 Raid 也是遍历候选，只不过用的是随机序）：TryPlace 失败原因主要是
            //      “进近廊道被 Modules 挡住”或“与已有 Landing 占位重叠”。 ----
            List<Landing> placed = CollectPlaced(raid);
            Beaches.Beach.Pos used = t.beach;
            bool placedOk = false;
            int tried = 0;
            if (t.candidates != null)
            {
                for (int i = 0; i < t.candidates.Count; i++)
                {
                    Beaches.Beach.Pos c = t.candidates[i];
                    Vector3 d = c.dir + c.navPos.pos.normalized * 0.3f;
                    tried++;
                    if (landing.TryPlace(c.navPos, d, ModConfig.ShipSpeedMultiplier.Value, placed))
                    {
                        placedOk = true;
                        used = c;
                        break;
                    }
                }
            }

            if (!placedOk)
            {
                UnityEngine.Object.Destroy(waveGo);
                info = string.Format("附近 {0} 个滩头都被地形/建筑挡住或被占用，换个位置点", tried);
                return false;
            }

            // ---- 原版发射流程：预生成 → 发射协程（Launch + 靠岸到达回调 + 音乐）----
            wave.RefreshLandings();                                   // 同 Raid.IIslandPlay
            landing.Spawn();                                          // Longship + 舱内敌人（原版）
            AttachPirateOrder(landing.spawnedShip);                    // 关键：把 order 交还 Pirate（否则战局中途投放会被 KillAllEnemies 抢走 → 不下船）
            wave.approachAudioId = t.vikingRef.approachAudioId;        // 同 Raid.cs 给波次赋音频的做法
            wave.arriveAudioId = t.vikingRef.arriveAudioId;
            raid.StartCoroutine(wave.BeginWave());                     // 原版协程：Launch() → 船开 → 下船
            DisembarkWatchdog.Get().Watch(landing);                    // 到岸后若卡住不下船 → 诊断 + 兜底（见 DisembarkWatchdog.cs）

            if (ModConfig.VerboseLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo(string.Format("[NewMode] 采用第 {0} 个候选滩头 {1}（距点击处 {2:F2}m）",
                    tried, Fmt(used.navPos.pos), Vector3.Distance(used.navPos.pos, t.beach.navPos.pos)));

            info = string.Format("已投放 {0} ×{1}（船 {2}，滩头离点击处 {3:F1}m）",
                t.vikingRef.name, load.count, landing.shipPrefab.name, t.shoreDist);
            return true;
        }

        /// <summary>按 key 升序插入（候选滩头数量少，插入排序足够）。</summary>
        static void InsertSorted(List<Beaches.Beach.Pos> list, List<float> keys, Beaches.Beach.Pos value, float key)
        {
            int i = list.Count;
            while (i > 0 && keys[i - 1] > key) i--;
            list.Insert(i, value);
            keys.Insert(i, key);
        }

        /// <summary>
        /// 复刻原版 Landing.TryPlace 的最后一步检查：从滩头朝海外 50 单位的进近廊道里不允许被
        /// Modules（建筑/岩石/悬崖模块）挡住。起点算法与 Landing.ShipTravel 一致（先用 island.fog.capsuleCollider 收窄）。
        /// </summary>
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

        /// <summary>
        /// 让每个敌人由 Pirate 接管 order（等价于原版 Brain.Setup 的 PickNewOrder 选中 Pirate 的效果）。
        /// 为什么需要：原版船与敌人在**关卡生成期**就备好，那时我方尚未部署，
        /// `KillAllEnemies.WantsControl() = agent.faction.enemy.agents.Count > 0` 为假，于是 PickNewOrder 选中 Pirate；
        /// 而本 mod 是战局中途投放——我方已在场 → KillAllEnemies（在 orderList 里更靠前）抢先，orderDist 变成流场哨兵值，
        /// `Pirate.MaybeAct` 的 `orderDist &lt; 0.01` 永不成立 → 不下船（实测 orderDist=1000000）。
        /// 交还 Pirate 后完全走原版节奏：航行中 dist=0 但未 landed 不下船；靠岸后走到船头 orderDist→0 才跳下。
        /// 下船时 Pirate 的 longship 置空 → WantsControl 变假 → PickNewOrder 自动切回 KillAllEnemies。
        /// </summary>
        internal static int AttachPirateOrder(Longship ship)
        {
            if (ship == null || ship.agents == null) return 0;
            int n = 0;
            for (int i = 0; i < ship.agents.Count; i++)
            {
                Agent a = ship.agents[i];
                if (a == null || a.brain == null) continue;

                Pirate p = a.GetComponent<Pirate>();
                if (p == null) continue;
                if (!a.brain.actions.Contains(p)) a.brain.actions.Add(p);
                if (!object.ReferenceEquals(a.brain.order, p))
                {
                    a.brain.order = p;
                    a.brain.orderMono = p;
                    n++;
                }
            }
            if (ModConfig.VerboseLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo("[NewMode] 已把 " + n + " 个敌人的 order 交还 Pirate（恢复原版下船节奏）");
            return n;
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

        /// <summary>按 area 选船：照抄原版（首个 area 够用的船，否则用最后一艘）。</summary>
        static Longship PickShipForLoad(List<Longship> ships, VikingReference vikingRef, int count)
        {
            float need = vikingRef.agent.area * count;
            for (int i = 0; i < ships.Count; i++)
            {
                Longship s = ships[i];
                if (s != null && s.area >= need) return s;
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
