using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>会话收尾（v1.7.6 专题：生命周期与状态清理，见 PROJECT_SPEC §11.14）。
    ///
    /// 把所有"我们加到游戏里 / 我们自己缓存的东西"分成三类，各自有明确的时机 ——
    /// 这样就不会再出现"某个子系统记得清、另一个忘了"的漏清理：
    ///
    ///   ① `PurgeBattleState()` —— **每局一份**的运行时状态（选中、遥控组、看门狗、编队窗口、滩头/兵种缓存）。
    ///      **换岛 / 回地图 / 关总开关**都会调；它**不碰跨局必须保留的账** ——
    ///      尤其 `ShipSpeedRuntime` 的"每条船原版基准用时"（清了就会复现 §11.13A 修的"×2 叠乘"）。
    ///   ② `RestoreGameState(gm)` —— 把我们**改到游戏里**的东西还回去：被拦的原版波次 `waveStartTime`、
    ///      在航船速、时间减速、原版点击栈、`brain.order`。**关总开关 / 插件卸载**时调；
    ///      离开战局时**不**调（那时还回去反而会让波次在拆场景时抢跑）。
    ///   ③ `DestroyHelpers()` —— 销毁我们 `new` 出来的常驻对象（辅助 MonoBehaviour / 运行时贴图 / 原版设置里的三行）。
    ///      **只在插件卸载**时调（关总开关不销毁，重新打开还要继续用）。
    ///
    /// 为什么值得单列一个文件：v1.6.0 曾经踩过"重开战局后我们的单位还在"（只销毁 Wave、没销毁 lazy squad），
    /// v1.7.6 又踩过"退出岛屿再进 → 船速叠乘"（每船账被清）。两次都是**同一类**问题：状态的生命周期
    /// 与它依赖的游戏对象不同步。现在把时机固定成上面三条，新增功能时只要挂到对应的一条上。</summary>
    internal static class SessionCleanup
    {
        /// <summary>清"每局一份"的运行时状态。幂等、可重复调用；**不销毁**我们投放的对象（那些归 `SpawnLedger`）。</summary>
        internal static void PurgeBattleState()
        {
            try
            {
                MarqueeSelect.ClearSession();      // 待成队 / 拖动 / 相机让位 / 减速
                RemoteGroup.Clear();               // 受控小队的 order 与组件
                DisembarkWatchdog.ForgetAll();      // 下船看门狗盯着的船
                FlotillaLauncher.Cancel();          // 还没发射的编队窗口
                PlacementMarker.HideIfAny();        // 悬停落点标记（不新建实例）
                RemoteCursor.ForceRelease();        // CursorManager 栈
                IngameMenu.ClearSessionCache();     // 兵种列表 / HUD 行 / 悬停 / 量高
                SquadBar.ClearSession();            // 底部头像条
                DropPlanner.ClearCache();           // 岸线采样 + 占位表
                UnitCatalog.ClearCache();           // 兵种查找缓存
                LevelTools.ClearScanCache();        // 原版波扫描缓存（**保留** waveStartTime 原值，见 §11.14）
                ShipSpeedRuntime.PruneDead();       // 只摘已销毁的船，**不清每船基准**（§11.13A）
                ForeignUnit.Prune();                // 非原生单位登记表

                if (Util.V(ModConfig.VerboseLog, false))
                    Util.Log(Loc.T("[NewMode][清理] 每局状态已清（选中 / 编队 / 看门狗 / 滩头与兵种缓存）"));
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 每局状态清理出错（不影响游戏）：{0}", e.Message));
            }
        }

        /// <summary>把我们改到游戏里的东西还原。幂等；`gm` 可为 null（不在战局里就只做不依赖场景的部分）。</summary>
        internal static void RestoreGameState(IslandGameplayManager gm)
        {
            try
            {
                LevelTools.ReleaseWaves();         // 被拦的原版波次：waveStartTime 还回原值（否则波永远不来）
                ShipSpeedRuntime.RestoreVanilla(gm);// 在航船速还原（保留每船基准账，便于再启用）
                MarqueeSelect.ClearPending();
                MarqueeSelect.ClearSlowMo();        // TimeManager 的减速
                RemoteCursor.ForceRelease();        // 原版 Navigator 的停用态
                RemoteGroup.Clear();                // brain.order 交还
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 还原游戏状态出错（不影响游戏）：{0}", e.Message));
            }
        }

        /// <summary>插件卸载：先还原游戏状态，再把我们创建的常驻对象与运行时贴图全部销毁（见 §11.14）。</summary>
        internal static void DestroyHelpers()
        {
            try
            {
                FlotillaLauncher.Destroy();
                DisembarkWatchdog.Destroy();
                SpawnLedger.Destroy();              // 内含 DestroyAll：把我们投放的 Wave / squad 一并销毁
                PlacementMarker.Destroy();
                RemoteCursor.Destroy();
                ClickShield.Destroy();
                MenuSkin.Destroy();
                VanillaSettings.DestroyWidgets();   // 把三行从原版设置里摘掉（热重载不会出现两份）
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 卸载时的收尾出错：{0}", e.Message));
            }
        }
    }
}
