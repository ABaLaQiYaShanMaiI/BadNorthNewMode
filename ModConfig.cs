using BepInEx.Configuration;
using UnityEngine;

namespace BadNorthNewMode
{
    /// <summary>全部 cfg 收在本静态类：Bind 按段拆小方法，Plugin.Awake 只调一次。</summary>
    public static class ModConfig
    {
        // ============ General ============
        public static ConfigEntry<KeyboardShortcut> Hotkey;
        public static ConfigEntry<KeyboardShortcut> CleanupHotkey;
        public static ConfigEntry<KeyboardShortcut> ForceWinHotkey;
        public static ConfigEntry<bool> ShowHud;
        public static ConfigEntry<string> EnemyName;
        public static ConfigEntry<int> SquadSize;
        public static ConfigEntry<bool> AllowCrossIslandUnits;

        /// <summary>菜单打开时盖住"原版点击"（见 PROJECT_SPEC §6 T2）。</summary>
        public static ConfigEntry<bool> MenuBlocksWorldClicks;

        // ============ UI（借用原版 UI + 原版贴图/配色/字体）============
        public static ConfigEntry<bool> UseVanillaUI;
        public static ConfigEntry<bool> UseVanillaFont;
        public static ConfigEntry<bool> ConfirmDestructive;
        public static ConfigEntry<int> UiFontSize;

        public static ConfigEntry<string> UiPanelColor;    // 面板灰蓝；空 = 内置 #3D4A59
        public static ConfigEntry<string> UiButtonColor;   // 按键黄（已淡化）；空 = 内置 #E5C98E

        /// <summary>UI 字体名（空 = 自动：原版语言字体 → 系统中文黑体）。</summary>
        public static ConfigEntry<string> UiFontName;

        public static ConfigEntry<bool> ShowSquadBar;

        public static ConfigEntry<int> SquadBarMax;

        /// <summary>头像条的横向位置（Left / Center / Right）。</summary>
        public static ConfigEntry<string> SquadBarAlign;

        /// <summary>头像条的离底像素；-1 = 自动（会避开原版"我方小队技能条"）。</summary>
        public static ConfigEntry<int> SquadBarBottom;

        // ============ Native（拦下原版波次 / 控制原生单位）============
        public static ConfigEntry<bool> BlockVanillaWaves;

        /// <summary>控制**原生单位**（原版上岛敌人）。v1.6.0 从 RemoteNativeUnits 改名 —— 旧键的 false 不再影响默认值。</summary>
        public static ConfigEntry<bool> ControlNativeUnits;

        // ============ Remote（v1.4.0 遥控非原生单位）============
        public static ConfigEntry<bool> RemoteControl;
        public static ConfigEntry<bool> RemoteMoveRequiresModifier;
        public static ConfigEntry<string> RemoteOrderButton;
        public static ConfigEntry<int> RemoteMarqueePixels;
        public static ConfigEntry<int> RemoteGrabRadius;
        public static ConfigEntry<float> RemoteClickRadius;
        public static ConfigEntry<KeyCode> RemoteSelectAllKey;
        public static ConfigEntry<bool> RemoteSlowMo;
        public static ConfigEntry<float> RemoteSlowMoScale;
        public static ConfigEntry<KeyCode> RemoteFreeMarqueeKey;
        public static ConfigEntry<bool> RemoteMarqueeFromUnit;
        public static ConfigEntry<int> RemoteSoftCap;
        public static ConfigEntry<bool> RemoteHighlight;

        // ============ Landing ============
        public static ConfigEntry<float> ShipSpeedMultiplier;
        public static ConfigEntry<float> MaxShoreDistance;
        public static ConfigEntry<float> MaxLandHeight;
        public static ConfigEntry<float> WaterLevelY;
        public static ConfigEntry<bool> ShowHoverPreview;
        public static ConfigEntry<float> MarkerSeconds;
        public static ConfigEntry<float> MinLandingSpacing;
        public static ConfigEntry<bool> LandingFallbackAnywhere;
        public static ConfigEntry<float> FlotillaDelay;
        public static ConfigEntry<float> FlotillaSpread;
        public static ConfigEntry<int> FlotillaMaxShips;
        public static ConfigEntry<bool> FollowDifficultyShipSpeed;
        public static ConfigEntry<float> DisembarkGrace;
        public static ConfigEntry<bool> DisembarkFix;

        // ============ Diag ============
        public static ConfigEntry<bool> VerboseLog;

        /// <summary>是否生成日志文件（默认 false：加载时摘掉 BepInEx 的磁盘日志监听器）。</summary>
        public static ConfigEntry<bool> LogToFile;

        public static void Bind(ConfigFile cfg)
        {
            Loc.Bind(cfg);                 // 先绑语言项；本文件的说明文案保持简中，不随语言切换
            BindGeneral(cfg);
            BindUi(cfg);
            BindNative(cfg);
            BindRemote(cfg);
            BindLanding(cfg);
            BindDiag(cfg);
        }

        static void BindGeneral(ConfigFile cfg)
        {
            // 显式传空数组：不能写 new KeyboardShortcut(KeyCode.F1) —— params 空数组会被 Roslyn
            // 优化成 Array.Empty<T>()，而游戏运行时 mscorlib 2.0 没有该 API（见 csproj 注释）。
            Hotkey = cfg.Bind("General", "Hotkey", new KeyboardShortcut(KeyCode.F1, new KeyCode[0]),
                "投放模式的开关。默认 F1。");
            ShowHud = cfg.Bind("General", "ShowHud", true,
                "左上角显示模式状态与上次投放结果。");
            MenuBlocksWorldClicks = cfg.Bind("General", "MenuBlocksWorldClicks", true,
                "菜单打开时盖一层不可见 UI：点菜单不会顺带触发游戏自己的点击（选中 / 移动我方小队）。\n" +
                "false = 旧行为（游戏也会收到这次点击）。");
            CleanupHotkey = cfg.Bind("General", "CleanupHotkey", new KeyboardShortcut(KeyCode.F2, new KeyCode[0]),
                "清场：销毁本模组投放的船与单位。默认 F2。");
            ForceWinHotkey = cfg.Bind("General", "ForceWinHotkey", new KeyboardShortcut(KeyCode.F3, new KeyCode[0]),
                "强制胜利：走原版结算（结算屏 / 成就 / 存档都正常）。默认 F3。");
            EnemyName = cfg.Bind("General", "EnemyName", "Viking_Sword",
                "投放的兵种（内部名）。留空 = 每次随机；本关没有就从全局引用字典取同名单位。\n" +
                "Viking_Sword（默认）、Viking_SwordShield、Viking_Archer、Viking_AxeThrower、\n" +
                "Viking_Twohanded、Viking_Berserker、Viking_Tank、Viking_TankArcher。");
            SquadSize = cfg.Bind("General", "SquadSize", 0,
                "每艘船搭载的敌人数。0 = 按兵种默认梯度（弱兵多、精锐少、巨人 1）；\n" +
                ">0 = 固定该数量（仍受船容量裁剪）。F1 菜单的\"数量\"按钮写的就是这里。");
            AllowCrossIslandUnits = cfg.Bind("General", "AllowCrossIslandUnits", true,
                "允许使用本关生成池之外的兵种（从全局引用字典借）。默认开。");
        }

        static void BindUi(ConfigFile cfg)
        {
            UseVanillaUI = cfg.Bind("UI", "UseVanillaUI", true,
                "提示走原版通知条，破坏性操作弹原版确认框；取不到原版对象时自动回退到 HUD 文本。默认开。");
            UseVanillaFont = cfg.Bind("UI", "UseVanillaFont", true,
                "菜单与 HUD 用游戏自带字体；false = IMGUI 默认字体（缺中文会显示方块，仅排障）。默认开。");
            UiFontSize = cfg.Bind("UI", "UiFontSize", 13,
                "菜单 / HUD 字号（9~20）。默认 13；嫌挡岛就调回 12。");
            UiFontName = cfg.Bind("UI", "FontName", "",
                "界面字体名；留空 = 自动（原版艺术字 → 原版语言字体表 → 系统中文黑体）。\n" +
                "要指定就填系统已安装的字体名（如 Microsoft YaHei UI、SimHei）。");
            UiPanelColor = cfg.Bind("UI", "PanelColor", "",
                "面板底色（RRGGBB / RRGGBBAA，可带 #）；留空 = 沙色 D5D0C8。\n" +
                "标题栏用同系的蓝灰 89A1AD，跟着一起变。");
            UiButtonColor = cfg.Bind("UI", "ButtonColor", "",
                "按键底色（格式同上）；留空 = 淡黄 E5C98E（悬停更亮、开关\"开\"更深）。");
            ConfirmDestructive = cfg.Bind("UI", "ConfirmDestructive", true,
                "破坏性操作（F2 清场、释放遥控）前弹确认框。false = 点一下立刻执行。默认开。");
            ShowSquadBar = cfg.Bind("UI", "SquadBar", true,
                "屏幕底部的小队头像条：左键点一下 = 选中整队（Shift = 并入 / 移出）。\n" +
                "在投放菜单里点它 = 先收起菜单再选中。默认开。");
            SquadBarMax = cfg.Bind("UI", "SquadBarMax", 8,
                "头像条最多显示几格（屏幕放不下会自动减少）。");
            SquadBarAlign = cfg.Bind("UI", "SquadBarAlign", "Center",
                "头像条的横向位置：Left / Center / Right。默认居中。");
            SquadBarBottom = cfg.Bind("UI", "SquadBarBottom", -1,
                "头像条离屏幕底部的像素。-1（默认）= 自动：选中我方小队时抬到原版技能条上方；\n" +
                ">= 0 = 固定像素（不再自动避让）。");
        }

        static void BindLanding(ConfigFile cfg)
        {
            ShipSpeedMultiplier = cfg.Bind("Landing", "ShipSpeedMultiplier", 1f,
                "航行速度倍率（原版 TryPlace 的 speedMultiplier）。");
            MaxShoreDistance = cfg.Bind("Landing", "MaxShoreDistance", 3f,
                "点击处到最近可登陆岸线的最大距离（米），超过就拒绝投放。");
            MaxLandHeight = cfg.Bind("Landing", "MaxLandHeight", 0.5f,
                "落差上限（米）：点击的地块与最终落点滩头，海拔都必须在 ±本值 之内。\n" +
                "原版只有与海面齐平的沙滩能登陆；点沙滩却被判\"高地\"就调大（比如 0.8）。");
            WaterLevelY = cfg.Bind("Landing", "WaterLevelY", 0f,
                "海平面世界高度，作为落差判定的基准（原版海面 ≈ 0，一般不用改）。");
            ShowHoverPreview = cfg.Bind("Landing", "ShowHoverPreview", true,
                "投放模式下实时显示鼠标处的落点标记（亮青 = 可投放，暗红 = 不可）。");
            MarkerSeconds = cfg.Bind("Landing", "MarkerSeconds", 2.5f,
                "落点光亮标记的保持时长（秒）。");
            MinLandingSpacing = cfg.Bind("Landing", "MinLandingSpacing", 1f,
                "滩头占用的首选间距（米）：实际要求 = 本值 + 船长。最近的滩头被占就试下一个候选，\n" +
                "附近都没有才放宽到\"不重叠\"；调大只会更倾向留空档，不会导致投不出来。");
            LandingFallbackAnywhere = cfg.Bind("Landing", "LandingFallbackAnywhere", true,
                "附近没有空位时改用全岛最近的滩头。true = 小岛后期也投得出来；false = 直接拒绝并提示。");
            FlotillaDelay = cfg.Bind("Landing", "FlotillaDelay", 1f,
                "编队窗口（秒）：窗口内连续投放的船合并成一支编队（只播一条接近音乐）。0 = 每艘各自出发。");
            FlotillaSpread = cfg.Bind("Landing", "FlotillaSpread", 2f,
                "编队内各船出发时间的散布上限（秒）。0 = 同时出发。");
            FlotillaMaxShips = cfg.Bind("Landing", "FlotillaMaxShips", 6,
                "单支编队最多合并几艘船（超过就开新编队）。");
            FollowDifficultyShipSpeed = cfg.Bind("Landing", "FollowDifficultyShipSpeed", true,
                "船速是否跟随关卡难度倍率（原版用 difficulty.shipSpeedMultiplier，VeryHard 更快）。\n" +
                "true = 与原版手感一致；false = 只用 ShipSpeedMultiplier。");
            DisembarkGrace = cfg.Bind("Landing", "DisembarkGrace", 3f,
                "船到岸后超过这么久还没人下船，就判为卡住（秒）。");
            DisembarkFix = cfg.Bind("Landing", "DisembarkFix", true,
                "卡住时兜底下船（走原版 RemoveFromShip）；false = 只打诊断日志。默认开。");
        }

        static void BindNative(ConfigFile cfg)
        {
            BlockVanillaWaves = cfg.Bind("Native", "BlockVanillaWaves", false,
                "无尽模式：拦下原版波次，本关只出现你投放的单位，而且不会自然结束（按 F3 退出）。\n" +
                "关掉开关会把原版波次的计时还回去。默认关。");
            ControlNativeUnits = cfg.Bind("Native", "ControlNativeUnits", true,
                "原生单位（原版上岛的敌人）也能被选中 / 框选 / 点地块前进；被遥控期间不再自行作战。\n" +
                "F2 清场与离开战局不会销毁它们（只撤我们的登记）。默认开。");
        }

        static void BindRemote(ConfigFile cfg)
        {
            RemoteControl = cfg.Bind("Remote", "RemoteControl", true,
                "遥控投放的单位（鼠标方案见 RemoteOrderButton）：\n" +
                "双键：左键点单位 = 选中这一个；双击 = 整队；右键点地块 = 前进。\n" +
                "单键 / 触摸：没选中时点单位 = 选中；有选中时点地块 = 前进。\n" +
                "Shift + 点 = 并入 / 移出；R = 全选；Alt + 拖动 = 框选；点海面 = 取消选中。\n" +
                "有选中时点地块 = 前进；多兵种 / 人多时就地分到相邻格。\n" +
                "还在船上也能选：点地块 = 记住登陆后的集结点。\n" +
                "遥控只接管行军，它们仍是我方的敌人（会照常烧房子）。");
            RemoteOrderButton = cfg.Bind("Remote", "RemoteOrderButton", "Auto",
                "遥控的鼠标方案。Auto（默认）= 跟随游戏的单/双键设置（设置里的光标模式）；Left = 旧手感（左键点地块即前进，不随单/双键适配）。\n" +
                "两种模式都有：Shift + 点 = 并入 / 移出；R = 全选；Alt + 拖动 = 框选。");
            RemoteMoveRequiresModifier = cfg.Bind("Remote", "RemoteMoveRequiresModifier", false,
                "下令是否必须按住 Shift / R：false（默认）= 有选中时普通点击也能下令，代价是每次多等 2 帧（约 33ms）确认原版没接管；\n" +
                "true = 不按修饰键的点击全归原版。");
            RemoteMarqueePixels = cfg.Bind("Remote", "RemoteMarqueePixels", 8,
                "左键拖动超过这么多像素才算\"框选\"，否则视为单击（= 按住 Shift 时下达前进命令 / 点在自己单位上时忽略）。");
            RemoteGrabRadius = cfg.Bind("Remote", "RemoteGrabRadius", 64,
                "从非原生单位这么多像素以内起拖才算框选（也是 Shift+点选的命中半径）；0 = 任意位置。\n" +
                "点选总不中就把这个值调大（比如 96）。");
            RemoteFreeMarqueeKey = cfg.Bind("Remote", "RemoteFreeMarqueeKey", KeyCode.LeftAlt,
                "按住该键 + 左键拖动 = 框选（默认唯一的框选入口；不按它就是原版相机平移）。\n" +
                "None = 关闭该快捷键。默认 左Alt。");
            RemoteMarqueeFromUnit = cfg.Bind("Remote", "RemoteMarqueeFromUnit", false,
                "旧版手感：从自己的单位上起拖也算框选。默认 false（拖动始终是原版相机平移）。");
            RemoteSoftCap = cfg.Bind("Remote", "RemoteSoftCap", 40,
                "每个兵种小队的上限：0 = 不限；>0 = 框选时按离框中心由近到远取满该数，**多出来的不组队、保持原逻辑**。");
            RemoteHighlight = cfg.Bind("Remote", "RemoteHighlight", true,
                "绘制框选矩形、受控单位与目标点标记。");
            RemoteClickRadius = cfg.Bind("Remote", "RemoteClickRadius", 1.2f,
                "Shift+点选的世界半径（米）：点击处与单位脚点相距 ≤ 本值即算点中；点不中就调大（比如 2）。");
            RemoteSelectAllKey = cfg.Bind("Remote", "RemoteSelectAllKey", KeyCode.R,
                "全选键：选中所有可选单位；按住它再点左/右键 = 全选并直接前进。None = 关闭。默认 R。");
            RemoteSlowMo = cfg.Bind("Remote", "RemoteSlowMo", true,
                "框选中或有选中时放慢时间（与原版选中我方小队同一套减速）。默认开。");
            RemoteSlowMoScale = cfg.Bind("Remote", "RemoteSlowMoScale", 0.1f,
                "减速倍率（原版选中我方小队用的是 0.1）。0.1~0.35 之间比较舒服。");
        }

        static void BindDiag(ConfigFile cfg)
        {
            VerboseLog = cfg.Bind("Diag", "VerboseLog", false,
                "详细日志：打印点击命中的地形点/海拔、滩头点、距离、最终 shipPrefab 与失败原因。排查时开。");
            LogToFile = cfg.Bind("Diag", "LogToFile", false,
                "是否生成 BepInEx\\LogOutput.log。默认 false（控制台窗口的日志不受影响）。");
        }
    }
}
