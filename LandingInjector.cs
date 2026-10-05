using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>把目标投下去：建对象树 → TryPlace → Spawn → 装配 → 交编队发射。</summary>
    internal static class LandingInjector
    {
        /// <summary>按解析结果投放一艘敌舰；失败时 info 是原因，并自动清理已建对象。</summary>
        internal static bool TrySpawn(Island island, Vector3 landPoint, out string info)
        {
            DropTarget t;
            string reason;
            if (!DropPlanner.TryResolve(island, landPoint, out t, out reason)) { info = reason; return false; }

            if (Util.V(ModConfig.VerboseLog, false))
                Util.Log(Loc.F("[NewMode] 点击陆地 {0}（海拔 {1:F2}m）→ 滩头 {2} 距离 {3:F2}m 船 {4}",
                    Util.Fmt(landPoint), landPoint.y - Util.V(ModConfig.WaterLevelY, 0f),
                    Util.Fmt(t.beach.navPos.pos), t.shoreDist, t.shipPrefab.name));

            Raid raid = island.raid;

            // 船速：可选跟随关卡难度倍率（原版 TryPlace 的 speedMultiplier 语义）
            float speedMult = Util.V(ModConfig.ShipSpeedMultiplier, 1f);
            if (Util.V(ModConfig.FollowDifficultyShipSpeed, true) &&
                island.levelNode != null && island.levelNode.diffiucltySettings != null)
            {
                speedMult *= island.levelNode.diffiucltySettings.shipSpeedMultiplier;
            }

            // Wave/ShipGroup 交给编队发射器分配（窗口内多艘共用同一个 Wave → 只播一条接近音乐）
            Wave wave;
            bool createdNew;
            ShipGroup group = FlotillaLauncher.Reserve(island, raid, Util.V(ModConfig.FlotillaDelay, 1f),
                Util.V(ModConfig.FlotillaSpread, 2f), out wave, out createdNew);
            if (createdNew) SpawnLedger.Track(wave.gameObject, island);   // 不进 raid.waves → 需自己登记清场
            SpawnLedger.TrackSquad(group.squad, island);                   // 单位挂在 lazy squad 下（runContainer），单独登记

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
            List<Landing> placed = DropPlanner.CollectPlaced(raid);
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
                info = Loc.F("附近 {0} 个滩头都被地形/建筑挡住或被占用，换个位置点", tried);
                return false;
            }

            // 预生成（原版 Spawn）→ 装配（order→Pirate + 舰上威胁）→ 由编队统一发射
            landing.Spawn();
            AttachAgentBehaviours(landing);
            wave.approachAudioId = t.vikingRef.approachAudioId;        // 同 Raid.cs 给波次赋音频的做法
            wave.arriveAudioId = t.vikingRef.arriveAudioId;

            string crewIssue = CrewCheck(landing.spawnedShip, t.vikingRef.name);
            if (crewIssue != null) Util.Warn(Loc.F("[NewMode] 船员异常：{0}（疑似船体叠加，请反馈此日志）", crewIssue));

            DropPlanner.InvalidateOccupancy();
            DisembarkWatchdog.Get().Watch(landing);                    // 到岸后若卡住不下船 → 诊断 + 兜底
            if (Util.V(ModConfig.FlotillaDelay, 1f) <= 0f) FlotillaLauncher.FlushNow();

            if (Util.V(ModConfig.VerboseLog, false))
                Util.Log(Loc.F("[NewMode] 采用第 {0} 个候选滩头 {1}（距点击处 {2:F2}m）",
                    tried, Util.Fmt(used.navPos.pos), Vector3.Distance(used.navPos.pos, t.beach.navPos.pos)));

            info = Loc.F("已投放 {0} ×{1}（船 {2}，滩头离点击处 {3:F1}m{4}{5}）",
                t.vikingRef.name, load.count, landing.shipPrefab.name, t.shoreDist,
                t.relaxed ? Loc.T("，已放宽间距") : "",
                t.fallback ? Loc.T("，附近满员 → 改用最近空滩头") : "");
            return true;
        }

        /// <summary>投放后装配：order 交还 Pirate（中途投放会被 KillAllEnemies 抢走，见 §4）+ 挂 ShipboardThreat。</summary>
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

                // v1.4.0：非原生身份标记（遥控框选的唯一数据源）
                VikingAgent va = a.GetComponent<VikingAgent>();
                ForeignUnit.Attach(a, (va != null && va.vikingReference != null) ? va.vikingReference.name : a.name);
            }
            if (Util.V(ModConfig.VerboseLog, false))
                Util.Log(Loc.F("[NewMode] 已装配 {0} 个敌人（order→Pirate + 舰上威胁）", n));
            return n;
        }

        /// <summary>船员自检：本船应只有一种兵种；混入其他兵种说明发生叠加/串船（正常返回 null，零噪音）。</summary>
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
            return (bad == 0) ? null : Loc.F("应有 {0}，实际混入 {1} 个其他单位（如 {2}）", expected, bad, other);
        }
    }
}
