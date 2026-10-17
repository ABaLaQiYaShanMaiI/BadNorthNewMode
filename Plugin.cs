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
        public const string VERSION = "1.8.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        /// <summary>v1.7.5：**模组总开关**（cfg `[General] Enabled`，游戏内可在原版设置里切）。
        /// false = 完全静默：不接管输入、不绘制、不拦波次（见 `Update` / `OnGUI` / `ApplyDisabled`）。</summary>
        internal static bool Enabled { get { return Util.V(ModConfig.Enabled, true); } }

        bool _disabledApplied;             // v1.7.5：总开关"关掉"的收尾只做一次
        Island _lastIsland;                // v1.7.6：上一帧的岛屿实例（变了 / 变 null = 离开战局或换岛 → 清每局状态）

        bool _subscribed;
        bool _subscribeFailed;
        bool _envReported;                 // v1.7.4：环境自检只在第一次 Update 报一次（那时插件才全部加载完）
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
            // v1.7.4：第二个参数 = 是否顺手删掉上一次的共享日志（默认 false，见 ModConfig.CleanLogOnStart）。
            LogFileSwitch.Apply(Util.V(ModConfig.LogToFile, false), Util.V(ModConfig.CleanLogOnStart, false));

            ConfigEntry<KeyboardShortcut> hk = ModConfig.Hotkey;
            Log.LogInfo(Loc.F("[NewMode] v{0} 已加载：{1} 开关投放模式，点击滩头陆地投放敌舰。",
                VERSION, (hk != null) ? hk.Value.ToString() : Loc.T("(热键未绑定)")));
            // v1.7.1：一行环境自检；v1.7.4 起**挪到第一次 Update**才报 —— Awake 阶段 BepInEx 还在按顺序加载插件，
            // `Chainloader.PluginInfos` 里只有我们自己（实机日志因此打出"整合包=有（0 个插件）"这种误导数字）。
            Log.LogInfo(Loc.F("[NewMode] 接管开关：原版波次拦下 = {0}，原生单位可遥控 = {1}（F1 菜单可切）。",
                Util.V(ModConfig.BlockVanillaWaves, false), Util.V(ModConfig.ControlNativeUnits, true)));
        }

        void Update()
        {
            // v1.7.4：环境自检挪到**第一次 Update**（此时 Chainloader 已把 23 个插件全加载完）；
            // 原先放在 Awake 会让"整合包=N 个插件"永远是 0（我们排在最前，后面还没加载）。
            if (!_envReported)
            {
                _envReported = true;
                Compat.ReportEnvironment();
            }

            LogFileSwitch.Tick();                       // 重试删日志残留（≤30s，见 LogFileSwitch）

            // v1.7.5：把两行开关挂进**原版设置界面**（只做一次）。必须在总开关判断**之前** —— 关掉模组之后
            // 还得靠这一项从游戏里再打开它（原版设置是我们的"逃生门"）。
            VanillaSettings.Tick();

            // v1.7.5：**总开关**。false = 完全静默：不接管点击 / 不画菜单·HUD / 快捷键失效 / 不拦原版波次。
            // 从"开"变"关"的那一帧做一次收尾（见 ApplyDisabled），之后直接 return（不注册、不扫描、不绘制）。
            if (!Enabled)
            {
                if (!_disabledApplied) { _disabledApplied = true; ApplyDisabled(); }
                return;
            }
            if (_disabledApplied)
            {
                _disabledApplied = false;
                Util.Log(Loc.T("[NewMode] 总开关已开：投放菜单 / HUD / 点击遥控恢复（原版设置里可随时再关）"));
            }

            // v1.7.6 专题（见 §11.14）：**岛屿实例变了 / 回地图** → 立刻清一次"每局状态"。
            // 判定用岛屿引用而不是 `InBattle`：后者在暂停时为 false，而暂停绝不能清遥控组。
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island curIsland = (gm != null) ? gm.island : null;
            if (!object.ReferenceEquals(curIsland, _lastIsland))
            {
                // 首次进岛（_lastIsland == null）不用清；之后每次离开 / 换岛都清（离开时 curIsland 为 null）。
                if (!object.ReferenceEquals(_lastIsland, null)) SessionCleanup.PurgeBattleState();
                _lastIsland = curIsland;
            }

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
            if (!Enabled) return;                 // v1.7.5：总开关关掉 = 一个像素都不画（菜单 / HUD / 头像条 / 覆盖层全停）

            // v1.7.4 修订：绘制顺序 = 覆盖层 → 头像条 → **菜单最后**。
            // 菜单是我们自己的"前台面板"，必须盖在其它两层之上 —— 曾出现头像条/标记压住菜单最后几行
            //（第 5 行正是"靠岸速度"档位条）的报障；IMGUI 里后画的在上层，所以这里改顺序即可。
            MarqueeSelect.DrawOverlay();
            SquadBar.Draw();                      // 底部小队头像条
            IngameMenu.Draw();                    // 菜单 + HUD（最后画 = 最上层）
        }

        /// <summary>v1.7.5：总开关从"开"变"关"的那一帧做一次收尾（**不每帧跑**）。目标 = 完全不介入：
        /// 菜单 / HUD / 拦截面收起、遥控归还给原版、时间减速的账清掉、**被拦的原版波次放回去**、在航船速还原原版；
        /// 此后 Update/OnGUI 全部早退。v1.7.6：收尾统一走 `SessionCleanup`（见 §11.14）。</summary>
        void ApplyDisabled()
        {
            try
            {
                IngameMenu.Close();                       // 关菜单 + 收起 HUD（内部会 Hide ClickShield）
                ClickShield.Hide();

                // ① 把我们**改到游戏里**的东西还回去。旧实现漏了 `LevelTools.ReleaseWaves()` ——
                //    "拦下原版波次"开着时关掉总开关，`waveStartTime` 还停在 float.MaxValue ⇒ 整局永远不出波，
                //    而 Update 已早退、再也不会有人去还原它（这是最隐蔽的一类"关掉了但没真关"）。
                SessionCleanup.RestoreGameState(Singleton<IslandGameplayManager>.instance);

                // ② 每局状态（选中 / 遥控组 / 看门狗 / 编队窗口 / 各类缓存）立刻清掉；
                //    我们**已经投放**的船与单位**不销毁**（那是玩家摆出来的，关模组不等于删场景）。
                SessionCleanup.PurgeBattleState();

                Util.Log(Loc.T("[NewMode] 总开关已关（原版设置 → 分类「BadNorthNewMode 模组」可再打开）：菜单/HUD 已收起、遥控已归还、波次与船速已还原"));
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 总开关关闭时的收尾出错：{0}", e.Message));
            }
        }

        /// <summary>插件卸载（关游戏 / 热重载）：先还原游戏状态，再销毁我们创建的一切（见 `SessionCleanup` / §11.14）。</summary>
        void OnDestroy()
        {
            SessionCleanup.RestoreGameState(Singleton<IslandGameplayManager>.instance);
            SessionCleanup.DestroyHelpers();
        }

    }
}
