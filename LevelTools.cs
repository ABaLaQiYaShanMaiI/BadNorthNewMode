using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>接管本关的"生成与收尾"（v1.5.6）：拦下原版波次 + F3 强制胜利。见 PROJECT_SPEC §5。</summary>
    internal static class LevelTools
    {
        static Island _blocked;
        static readonly List<Landing> _blockedLandings = new List<Landing>();

        /// <summary>被拦下的原版 Landing（永不发射）→ 投放占位表要忽略，否则小岛被"看不见的船"占满。</summary>
        internal static bool IsBlocked(Landing landing)
        {
            return landing != null && _blockedLandings.Contains(landing);
        }

        internal static void Tick(IslandGameplayManager gm)
        {
            if (ModConfig.ForceWinHotkey != null && ModConfig.ForceWinHotkey.Value.IsDown()) TryForceWin(gm);
            if (Util.V(ModConfig.BlockVanillaWaves, false)) TryBlockWaves(gm);
        }

        /// <summary>拦下原版波次（见 §5）：waveStartTime 推到无穷 + 末波标记"已发射/已生成"。</summary>
        static void TryBlockWaves(IslandGameplayManager gm)
        {
            Island island = (gm != null) ? gm.island : null;
            if (island == null || !island.generated || island.state != Island.State.Playing) return;
            if (object.ReferenceEquals(_blocked, island)) return;

            Raid raid = island.raid;
            if (raid == null || raid.landingContainer == null) return;

            Wave[] waves = raid.landingContainer.GetComponentsInChildren<Wave>(true);
            if (waves == null || waves.Length == 0) return;

            _blockedLandings.Clear();
            int landings = 0;
            for (int i = 0; i < waves.Length; i++)
            {
                Wave w = waves[i];
                if (w == null) continue;

                w.waveStartTime = float.MaxValue;
                if (w.shipGroups == null) continue;

                for (int g = 0; g < w.shipGroups.Count; g++)
                {
                    ShipGroup sg = w.shipGroups[g];
                    if (sg == null || sg.landings == null) continue;

                    for (int l = 0; l < sg.landings.Count; l++)
                    {
                        if (sg.landings[l] == null) continue;
                        _blockedLandings.Add(sg.landings[l]);
                        landings++;
                    }
                }
            }

            Wave last = waves[waves.Length - 1];
            if (last != null) { last.haveAllLaunched = true; last.haveAllSpawned = true; }

            _blocked = island;
            DropPlanner.InvalidateOccupancy();
            IngameMenu.Say(Loc.F("接管模式：已拦下原版波次（{0} 波 / {1} 个登陆点）", waves.Length, landings));
            Util.Log(Loc.F("[NewMode][接管] 已拦下原版波次：{0} 波 / {1} 个登陆点不再发射（含其占位）→ 收尾请按 F3。", waves.Length, landings));
        }

        /// <summary>F3：走原版胜利入口 `EndOfLevel.AllVikingsKilled()`（结算屏/成就/存档全由原版处理）。</summary>
        static void TryForceWin(IslandGameplayManager gm)
        {
            string why;
            if (!Plugin.InBattle(gm, out why)) { IngameMenu.Say(why); return; }

            EndOfLevel eol = gm.endOfLevel;
            if (eol == null) { IngameMenu.Say(Loc.T("EndOfLevel 未就绪")); return; }
            if (eol.reason != EndOfLevel.Reason.None) { IngameMenu.Say(Loc.T("本关已经结束过了")); return; }

            eol.AllVikingsKilled();
            IngameMenu.Say(Loc.T("强制胜利：已走原版胜利流程"));
            Util.Log(Loc.T("[NewMode][接管] 强制胜利：EndOfLevel.AllVikingsKilled()（原版胜利流程）"));
        }
    }
}
