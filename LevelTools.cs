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

            // v1.7.1：与"正在接管波次的插件"（整合包终岛无尽 / 贤者时缓 / 入场运镜 / Together 在线）叠加会出现
            // "波次永远不来" → 默认拒绝（cfg [Compat] BlockWavesWithOthers=true 可强制）。
            if (Util.V(ModConfig.BlockVanillaWaves, false))
            {
                string why;
                if (Compat.WaveBlockForbidden(out why))
                {
                    // 回写 cfg + 提示：只拦行为不回写，F1 菜单会一直显示"原版波次：拦下"（与实际状态不符）。
                    Compat.RefuseWaveBlock(why);
                    if (_island != null) ReleaseWaves();
                    return;
                }

                TryBlockWaves(gm);                 // 放行：与往常一致
                return;
            }

            if (_island != null) ReleaseWaves();
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

            // v1.7.1-b2：整合包叠加**不拒绝**（作者的"无限试炼场"口径），只把风险并进同一条提示里说一次。
            string say = Loc.F("自定义模式（无尽）：本关只有你投放的单位，不会自然结束 —— 按 F3 强制胜利退出（{0} 波原版波次已拦下）", waves.Length);
            string risk = Compat.WaveBlockRiskNote();
            if (!string.IsNullOrEmpty(risk)) { say += "　" + risk; Util.Warn(risk); }

            IngameMenu.Say(say);
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

            // v1.7.1：整合包 / 联机环境下先二次确认（对方的胜负改写可能与我们的结算口径对撞，见兼容方案 §3.3 C5）。
            if (Compat.ForceWinConfirmNeeded)
            {
                EndOfLevel captured = eol;
                if (!VanillaUI.Confirm(Loc.T("强制胜利确认"),
                        Loc.T("整合包/联机环境：强制胜利会立刻走原版结算，确定继续？"),
                        delegate { ForceWinNow(captured); }))
                {
                    ForceWinNow(captured);      // 确认框没弹出来（或 cfg 关掉了确认）→ 直接执行
                }
                return;
            }

            ForceWinNow(eol);
        }

        /// <summary>真正的结算动作（与确认流程分开，两条路径共用）。</summary>
        static void ForceWinNow(EndOfLevel eol)
        {
            eol.AllVikingsKilled();
            IngameMenu.Say(Loc.T("强制胜利：已走原版胜利流程"));
            Util.Log(Loc.T("[NewMode][接管] 强制胜利：EndOfLevel.AllVikingsKilled()（原版胜利流程）"));
        }
    }
}

