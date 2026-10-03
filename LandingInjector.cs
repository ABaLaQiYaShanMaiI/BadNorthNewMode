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
    }

    /// <summary>用原版对象树与协程投放敌舰（Wave→ShipGroup→Landing→ShipLoad→TryPlace→Spawn→BeginWave），航线/靠岸/下船全由原版承担；输入是点击到的陆地滩头。</summary>
    internal static class LandingInjector
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

        /// <summary>把点击到的陆地地块解析成可投放目标：点击处与落点都要与海面齐平（挡悬崖/高台），且水平距离在 MaxShoreDistance 内。</summary>
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

            VikingReference vikingRef = PickEnemy(island, pool, ModConfig.EnemyName.Value);
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

            int squadSize = EffectiveSquadSize(island, ships, vikingRef, ModConfig.SquadSize.Value);
            Longship ship = PickShipForCount(island, vikingRef, squadSize);   // 人数 → 自动配"装得下的最小船"
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

            // 占用规则：最近候选滩头若已有船（原版或本 mod）→ 拒绝。要求间距随本船长度放大，避免大船挤占原版停靠点。
            Beaches.Beach.Pos nearest = cand[0];
            float baseSpacing = Mathf.Max(0.5f, ModConfig.MinLandingSpacing.Value);
            float spacing = baseSpacing + ship.length;
            float occDist;
            string occWho;
            if (IsOccupied(island, raid, nearest.navPos.pos, spacing, out occDist, out occWho))
            {
                reason = string.Format("该滩头已有船只（离 {0} {1:F1}m，需要 {2:F1}m = 基础 {3:F1} + 船长 {4:F1}）——换个滩头或减少人数",
                    occWho, occDist, spacing, baseSpacing, ship.length);
                return false;
            }

            // 优先"进近廊道没被 Modules 挡住"的候选（原版 TryPlace 失败的主因）；全被挡则退回最近候选由 TryPlace 裁决。
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

            // 船速：可选跟随关卡难度倍率（原版 TryPlace 的 speedMultiplier 语义）
            float speedMult = ModConfig.ShipSpeedMultiplier.Value;
            if (ModConfig.FollowDifficultyShipSpeed.Value && island.levelNode != null && island.levelNode.diffiucltySettings != null)
                speedMult *= island.levelNode.diffiucltySettings.shipSpeedMultiplier;

            // 原版对象树：Wave/ShipGroup 交给编队发射器分配（窗口内多艘共用同一个 Wave → 只播一条接近音乐）
            Wave wave;
            bool createdNew;
            ShipGroup group = FlotillaLauncher.Reserve(island, raid, ModConfig.FlotillaDelay.Value,
                ModConfig.FlotillaSpread.Value, out wave, out createdNew);
            if (createdNew) SpawnLedger.Track(wave.gameObject, island);   // 不进 raid.waves → 需自己登记清场

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

            // 逐个尝试候选滩头（原版 Raid 也用随机序遍历候选）；失败主因是廊道被 Modules 挡住或与已有 Landing 重叠。
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
                    if (landing.TryPlace(c.navPos, d, speedMult, placed))
                    {
                        placedOk = true;
                        used = c;
                        break;
                    }
                }
            }

            if (!placedOk)
            {
                UnityEngine.Object.Destroy(landingGo);
                info = string.Format("附近 {0} 个滩头都被地形/建筑挡住或被占用，换个位置点", tried);
                return false;
            }

            // 预生成（原版 Spawn）→ 装配（order→Pirate + 舰上威胁）→ 由编队统一发射
            landing.Spawn();
            AttachAgentBehaviours(landing);
            wave.approachAudioId = t.vikingRef.approachAudioId;        // 同 Raid.cs 给波次赋音频的做法
            wave.arriveAudioId = t.vikingRef.arriveAudioId;

            string crewIssue = CrewCheck(landing.spawnedShip, t.vikingRef.name);
            if (crewIssue != null && Plugin.Log != null)
                Plugin.Log.LogWarning("[NewMode] 船员异常：" + crewIssue + "（疑似船体叠加，请反馈此日志）");

            InvalidateOccupancy();
            DisembarkWatchdog.Get().Watch(landing);                    // 到岸后若卡住不下船 → 诊断 + 兜底
            if (ModConfig.FlotillaDelay.Value <= 0f) FlotillaLauncher.FlushNow();

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

        /// <summary>复刻原版 TryPlace 的最后一步：滩头朝海外 50 单位的进近廊道不得被 Modules 挡住（起点算法同 Landing.ShipTravel）。</summary>
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
        /// 投放后装配：① 把 order 交还 Pirate（战局中途投放会被 KillAllEnemies 抢走 → 不下船，见 PROJECT_SPEC §4）；
        /// ② 挂 ShipboardThreat（船上的敌人也算威胁，被我方索敌）。
        /// </summary>
        internal static int AttachAgentBehaviours(Landing landing)
        {
            Longship ship = (landing != null) ? landing.spawnedShip : null;
            if (ship == null || ship.agents == null) return 0;

            int n = 0;
            for (int i = 0; i < ship.agents.Count; i++)
            {
                Agent a = ship.agents[i];
                if (a == null || a.brain == null) continue;

                Pirate p = a.GetComponent<Pirate>();
                if (p != null)
                {
                    if (!a.brain.actions.Contains(p)) a.brain.actions.Add(p);
                    if (!object.ReferenceEquals(a.brain.order, p))
                    {
                        a.brain.order = p;
                        a.brain.orderMono = p;
                        n++;
                    }
                }

                if (a.GetComponent<ShipboardThreat>() == null)
                    a.gameObject.AddComponent<ShipboardThreat>().Init(landing, a);
            }
            if (ModConfig.VerboseLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo("[NewMode] 已装配 " + n + " 个敌人（order→Pirate + 舰上威胁）");
            return n;
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

        /// <summary>菜单用兵种列表 = 本关 enemies ∪ 全局字典里的 Viking_*（去重）；跨关兵种靠 PickEnemy 的字典回退保证可生成。</summary>
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

        /// <summary>该兵种在最大长船上的容量上限（人数可调到这么多，船会随人数自动换大）。</summary>
        internal static int MaxSquadSize(Island island, VikingReference vr)
        {
            if (island == null || island.levelNode == null || vr == null || vr.agent == null) return 1;

            List<Longship> ships = island.levelNode.possibleShips;
            float maxArea = 0f;
            for (int i = 0; i < ships.Count; i++)
            {
                Longship s = ships[i];
                if (s != null && s.area > maxArea) maxArea = s.area;
            }

            float unitArea = vr.agent.area;
            if (maxArea <= 0f || unitArea <= 0.0001f) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(maxArea / unitArea));
        }

        /// <summary>实际装载数：cfg &gt;0 用它，否则用兵种默认值；两者都受容量上限裁剪。</summary>
        internal static int EffectiveSquadSize(Island island, List<Longship> ships, VikingReference vr, int cfgSize)
        {
            int want = (cfgSize > 0) ? cfgSize : DefaultSquadSize(island, vr);
            return ClampSquadSize(ships, vr, want);
        }

        /// <summary>已放置船只的位置缓存（悬停预览每帧都会判定占用，避免每帧遍历全岛 Landing）；投放成功/清场/换岛时失效。</summary>
        static Island _occIsland;
        static bool _occDirty = true;
        static readonly List<Vector3> _occPos = new List<Vector3>();
        static readonly List<string> _occName = new List<string>();

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
                _occName.Add((l.spawnedShip != null) ? l.spawnedShip.name : "已放置的船");
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

        /// <summary>全岛已放置的 Landing（原版 TryPlace 靠它做占位互斥）。取 landingContainer 下所有子物体——这样**我们自己的船也会挡住自己**，避免连续投放在同一滩头叠船。</summary>
        static List<Landing> CollectPlaced(Raid raid)
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

        /// <summary>按名字取敌人（本关池 → 全局字典 → 随机）。结果按 (岛, 兵种名) 缓存：悬停预览每帧都会调用，避免重复查找与刷屏。</summary>
        static VikingReference PickEnemy(Island island, List<VikingReference> pool, string name)
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

            if (found == null)
            {
                bool allowCross = (ModConfig.AllowCrossIslandUnits == null) || ModConfig.AllowCrossIslandUnits.Value;
                UnityEngine.Object obj = null;
                if (allowCross && LevelStateObjectReferences.dict.TryGetValue(name, out obj))
                {
                    VikingReference v = obj as VikingReference;
                    if (v != null && v.agent != null)
                    {
                        found = v;
                        Plugin.LogOnce("fallback:" + name,
                            "[NewMode] \"" + name + "\" 不在本关生成池，改用全局引用字典里的同一单位。");
                    }
                }
            }

            if (found == null)
            {
                Plugin.LogOnce("missing:" + name,
                    "[NewMode] cfg EnemyName=\"" + name + "\" 既不在生成池也不在引用字典，退回随机。");
                found = pool[Random.Range(0, pool.Count)];
            }

            _pickIsland = island;
            _pickName = name;
            _pickUnit = found;
            return found;
        }

        static Island _pickIsland;
        static string _pickName;
        static VikingReference _pickUnit;

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

        /// <summary>船员自检：本船应只有一种兵种；混入其他兵种说明发生叠加/串船，打警告便于定位（正常情况返回 null，零噪音）。</summary>
        static string CrewCheck(Longship ship, string expected)
        {
            if (ship == null || ship.agents == null) return null;

            string other = null;
            int bad = 0;
            for (int i = 0; i < ship.agents.Count; i++)
            {
                Agent a = ship.agents[i];
                if (a == null) continue;

                VikingAgent va = a.GetComponent<VikingAgent>();
                string n = (va != null && va.vikingReference != null) ? va.vikingReference.name : "?";
                if (!string.Equals(n, expected, System.StringComparison.OrdinalIgnoreCase))
                {
                    bad++;
                    if (other == null) other = n;
                }
            }
            return (bad == 0) ? null : string.Format("应有 {0}，实际混入 {1} 个其他单位（如 {2}）", expected, bad, other);
        }

        static string Fmt(Vector3 v)
        {
            return string.Format("({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
        }
    }
}
