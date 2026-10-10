using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>接管本关的"生成与收尾"：拦下原版波次 + F3 强制胜利。见 PROJECT_SPEC §5。</summary>
    internal static class LevelTools
    {
        static Island _island;
        static bool _announced;
        static readonly Dictionary<Wave, float> _origStart = new Dictionary<Wave, float>();
        static readonly List<Landing> _blockedLandings = new List<Landing>();

        /// <summary>被拦下的原版 Landing（永不发射）→ 投放占位表要忽略，否则小岛被"看不见的船"占满。</summary>
        internal static bool IsBlocked(Landing landing)
        {
            return landing != null && _blockedLandings.Contains(landing);
        }

        /// <summary>无尽自定义模式进行中（HUD 用来常显"不会自然结束，F3 退出"）。</summary>
        internal static bool CustomMode { get { return _island != null; } }

        internal static void Tick(IslandGameplayManager gm)
        {
            if (ModConfig.ForceWinHotkey != null && ModConfig.ForceWinHotkey.Value.IsDown()) TryForceWin(gm);

            if (Util.V(ModConfig.BlockVanillaWaves, false)) TryBlockWaves(gm);
            else if (_island != null) ReleaseWaves();
        }

        /// <summary>彻底拦下原版波次（见 §4）：必须**每帧重写** —— 原版 `IIslandFirstEnter` 是协程，创建 Wave 之后才按排序赋 `waveStartTime`（第 0 波 = 0，进岛即发），只拦一次会被它覆盖回去。</summary>
        static void TryBlockWaves(IslandGameplayManager gm)
        {
            Island island = (gm != null) ? gm.island : null;
            if (island == null || !island.generated || island.state != Island.State.Playing) return;

            if (!object.ReferenceEquals(_island, island))
            {
                ReleaseWaves();                       // 换岛：先把上一座岛还原，避免留下被我们改过的值
                _island = island;
            }

            Raid raid = island.raid;
            if (raid == null || raid.landingContainer == null) return;

            Wave[] waves = raid.landingContainer.GetComponentsInChildren<Wave>(true);
            if (waves == null || waves.Length == 0) return;

            for (int i = 0; i < waves.Length; i++)
            {
                Wave w = waves[i];
                if (w == null) continue;

                if (w.waveStartTime != float.MaxValue)
                {
                    if (!_origStart.ContainsKey(w)) _origStart[w] = w.waveStartTime;
                    w.waveStartTime = float.MaxValue;              // MaybeLaunchWaves: timer >= waveStartTime 永不成立
                }
            }

            _blockedLandings.Clear();
            for (int i = 0; i < waves.Length; i++)
            {
                Wave w = waves[i];
                if (w == null || w.shipGroups == null) continue;

                for (int g = 0; g < w.shipGroups.Count; g++)
                {
                    ShipGroup sg = w.shipGroups[g];
                    if (sg == null || sg.landings == null) continue;

                    for (int l = 0; l < sg.landings.Count; l++)
                        if (sg.landings[l] != null) _blockedLandings.Add(sg.landings[l]);
                }
            }

            if (_announced) return;
            _announced = true;
            DropPlanner.InvalidateOccupancy();
            IngameMenu.Say(Loc.F("自定义模式（无尽）：本关只有你投放的单位，不会自然结束 —— 按 F3 强制胜利退出（{0} 波原版波次已拦下）", waves.Length));
            Util.Log(Loc.F("[NewMode][接管] 已拦下原版波次：{0} 波不再发射（占位一并放行）。本关不会自然结束，退出请按 F3 强制胜利。", waves.Length));
        }

        static void ReleaseWaves()
        {
            foreach (KeyValuePair<Wave, float> kv in _origStart)
                if (kv.Key != null) kv.Key.waveStartTime = kv.Value;

            _origStart.Clear();
            _blockedLandings.Clear();
            _island = null;
            _announced = false;
            DropPlanner.InvalidateOccupancy();
        }

        /// <summary>菜单按钮入口（与 F3 共用同一实现）。</summary>
        internal static void RequestForceWin()
        {
            TryForceWin(Singleton<IslandGameplayManager>.instance);
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

