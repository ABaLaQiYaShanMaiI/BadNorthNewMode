using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>入口与生命周期（v1.7.3 / P2 拆分）：`Awake`/`Update`/`OnGUI`/`OnDestroy` 与静态单例。
    /// 另两个 `partial` 半边：`Plugin.Drop.cs`（投放：菜单模式的悬停预览、`DoDrop`、地形取点）、
    /// `Plugin.Remote.cs`（遥控：点击路由的旧路径兜底、下令、全选/释放、世界点击订阅）。
    /// 原职责说明：菜单（IngameMenu）→ 世界点击 → 解析目标（DropPlanner）→ 投放（LandingInjector）。</summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string GUID = "badnorth.newmode";
        public const string NAME = "Bad North - New Mode";
        public const string VERSION = "1.7.3";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        bool _subscribed;
        bool _subscribeFailed;
        Vector2 _pressPos;                 // 本次按下的屏幕位置（区分"点击"与"拖动/平移相机"）
        Vector2 _pendingMovePos;           // 挂起的"普通点击下令"（见 HandleRemoteMode / FlushPendingMove）
        int _pendingMoveFrame;             // 0 = 无挂起
        bool _pressOrdered;                // 本次按下是否已下过令（避免"按下立即下令 + 松开又挂起"）
        bool _pressVanillaSelected;        // **按下那一帧**原版是否正选着我方小队（OneButton 模式下原版会"移动并取消选择"，松开时已查不出来）
        bool _pendingVanillaBusy;          // 挂起的这次点击当时原版正管着我方小队 → 整次点击归原版
        float _nextCandidateScan;          // 候选刷新节流（原生单位落地后尽快可选中）

        void Awake()
        {
            Instance = this;
            Log = Logger;
            try { ModConfig.Bind(Config); }
            catch (System.Exception e)
            {
                // 配置绑定失败不再让整个插件在 Awake 抛异常（否则游戏里完全无反馈），只报错后降级。
                Log.LogError(Loc.F("[NewMode] 配置绑定失败：{0}", e));
            }

            // v1.5.0：默认不生成日志文件（尽早执行，cfg 里的启动日志也一并清掉）。
            LogFileSwitch.Apply(Util.V(ModConfig.LogToFile, false));

            ConfigEntry<KeyboardShortcut> hk = ModConfig.Hotkey;
            Log.LogInfo(Loc.F("[NewMode] v{0} 已加载：{1} 开关投放模式，点击滩头陆地投放敌舰。",
                VERSION, (hk != null) ? hk.Value.ToString() : Loc.T("(热键未绑定)")));
            Log.LogInfo(Loc.F("[NewMode] 接管开关：原版波次拦下 = {0}，原生单位可遥控 = {1}（F1 菜单可切）。",
                Util.V(ModConfig.BlockVanillaWaves, false), Util.V(ModConfig.ControlNativeUnits, true)));

            // v1.7.1：一行环境自检（整合包 / Together 联机 / MMHOOK / 游戏本体是否被改造）—— 报障时先看这一行。
            Compat.ReportEnvironment();
        }

        void Update()
        {
            LogFileSwitch.Tick();                       // 重试删日志残留（≤30s，见 LogFileSwitch）
            IngameMenu.HandleDrag();                    // 菜单标题栏 / 提示框拖动
            // 菜单 / 头像条上盖住原版点击（见 §6 T2），用上一帧矩形；原版确认框在前台时反而要让开（它自己会挡世界点击）
            bool modal = VanillaUI.ModalShowing;
            ClickShield.Sync(IngameMenu.IsOpen && !modal, IngameMenu.MenuRect,
                             SquadBar.Visible && !modal, SquadBar.BarRect,
                             IngameMenu.HudVisible && !modal, IngameMenu.HudRect);
            if (ModConfig.Hotkey == null) return;

            TrySubscribeGameClick();
            RemoteCursor.Ensure();                      // v1.5.6：点击路由（左键点单位即选中、右键点地块即前进）

            // 候选定时刷新：原生单位一落地就能被选中 / 计入 HUD（否则要等玩家先点一下别处才登记，见 §5）
            if (Time.unscaledTime >= _nextCandidateScan)
            {
                _nextCandidateScan = Time.unscaledTime + 0.25f;
                MarqueeSelect.EnsureCandidates();
            }

            if (ModConfig.Hotkey.Value.IsDown())
            {
                IngameMenu.Toggle();                      // 菜单即投放模式
                if (!IngameMenu.IsOpen) return;
            }

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Compat.Tick(gm);                               // v1.7.1：记录本关是否终岛（HUD 兼容状态行用）
            ShipSpeedRuntime.Tick(gm);                     // v1.7.1：靠岸速度档位（原生 + 非原生船；1s 节流）

            MarqueeSelect.Tick(gm);                        // 菜单开着时内部自动取消
            SquadBar.Tick(gm);                             // 底部小队头像条（分组 + 点击选中）
            RemoteGroup.Tick();                            // 组维护与投放/遥控模式无关
            LevelTools.Tick(gm);                           // F3 强制胜利 + 拦下原版波次（v1.5.6）

            if (IngameMenu.IsOpen) { _pendingMoveFrame = 0; HandleDropMode(gm); return; }   // 开菜单：丢弃挂起的下令
            HandleRemoteMode(gm);                          // v1.4.0：菜单关闭时 = 遥控模式
        }

        void OnGUI()
        {
            IngameMenu.Draw();
            MarqueeSelect.DrawOverlay();
            SquadBar.Draw();                      // 底部小队头像条
        }

        void OnDestroy()
        {
            MarqueeSelect.ClearSlowMo();          // 卸载时释放减速，避免 TimeManager 里留残账
            ClickShield.Destroy();
            RemoteCursor.ForceRelease();
            MenuSkin.Destroy();                   // 释放菜单的运行时贴图
        }

    }
}
