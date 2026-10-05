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
        public static ConfigEntry<bool> ShowHud;
        public static ConfigEntry<string> EnemyName;
        public static ConfigEntry<int> SquadSize;
        public static ConfigEntry<bool> AllowCrossIslandUnits;

        // ============ Remote（v1.4.0 遥控非原生单位）============
        public static ConfigEntry<bool> RemoteControl;
        public static ConfigEntry<int> RemoteMarqueePixels;
        public static ConfigEntry<int> RemoteGrabRadius;
        public static ConfigEntry<float> RemoteClickRadius;
        public static ConfigEntry<KeyCode> RemoteSelectAllKey;
        public static ConfigEntry<bool> RemoteSlowMo;
        public static ConfigEntry<float> RemoteSlowMoScale;
        public static ConfigEntry<KeyCode> RemoteFreeMarqueeKey;
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
            BindRemote(cfg);
            BindLanding(cfg);
            BindDiag(cfg);
        }

        static void BindGeneral(ConfigFile cfg)
        {
            // 显式传空数组：不能写 new KeyboardShortcut(KeyCode.F1) —— params 空数组会被 Roslyn
            // 优化成 Array.Empty<T>()，而游戏运行时 mscorlib 2.0 没有该 API（见 csproj 注释）。
            Hotkey = cfg.Bind("General", "Hotkey", new KeyboardShortcut(KeyCode.F1, new KeyCode[0]),
                "进/出投放模式的按键。默认 F1。进入后点击水面投放敌舰，右键或 Esc 取消。");
            ShowHud = cfg.Bind("General", "ShowHud", true,
                "左上角显示模式状态与上一次投放结果（纯 GUI 文本，不需要任何资源）。");
            CleanupHotkey = cfg.Bind("General", "CleanupHotkey", new KeyboardShortcut(KeyCode.F2, new KeyCode[0]),
                "一键清场：销毁本 mod 投放过的所有船/单位（调试用）。\n" +
                "销毁是安全的——Agent.OnDestroy 会自行从 faction.agents 摘除，不会留脏数据卡结算。默认 F2。");
            EnemyName = cfg.Bind("General", "EnemyName", "Viking_Sword",
                "投放的敌人种类（本阶段只做一种：最基础的普通小兵 = 剑兵 Viking_Sword）。\n" +
                "取值来自 island.levelNode.enemies / 全局引用字典的名字，例如：\n" +
                "Viking_Sword（基础剑兵·默认）、Viking_SwordShield、Viking_Archer、Viking_AxeThrower、\n" +
                "Viking_Twohanded、Viking_Berserker、Viking_Tank、Viking_TankArcher。\n" +
                "留空 = 每次随机；名字不在本关生成池时会退回全局引用字典取同一单位，再不行才随机并打警告。");
            SquadSize = cfg.Bind("General", "SquadSize", 0,
                "每艘船搭载的敌人数。\n" +
                "0 = 按兵种默认梯度（弱的多、精锐少、巨人级 1 个）：\n" +
                "    剑兵 12 / 盾兵 10 / 弓箭手 8 / 掷斧手 7 / 双手剑士 5 / 狂战士 5 / 维京莽汉 1 / 重装弓箭手 1；\n" +
                "    梯度表在 UnitNames.cs，未收录的兵种自动回退原版公式（最小船容量 ÷ 单体面积）。\n" +
                ">0 = 固定该数量（仍会被最大长船容量上限裁剪）。\n" +
                "游戏内 F1 菜单里的\"数量\"一排按钮就是改这个值（默认/1/2/3/4/6/8/10/12）。");
            AllowCrossIslandUnits = cfg.Bind("General", "AllowCrossIslandUnits", true,
                "是否允许跨岛借用兵种：\n" +
                "true = 本关生成池里没有的兵种，从全局引用字典借（菜单里列出的就是两处合集）；\n" +
                "false = 只允许本关生成池里的兵种（更贴近关卡自身的难度曲线）。");
        }

        static void BindLanding(ConfigFile cfg)
        {
            ShipSpeedMultiplier = cfg.Bind("Landing", "ShipSpeedMultiplier", 1f,
                "航行速度倍率，对应原版 TryPlace 的 speedMultiplier（原版用关卡难度里的 shipSpeedMultiplier）。");
            MaxShoreDistance = cfg.Bind("Landing", "MaxShoreDistance", 3f,
                "点击处到最近可登陆岸线点的最大水平距离（米）。超过就判定为\"离滩头太远\"并拒绝投放。");
            MaxLandHeight = cfg.Bind("Landing", "MaxLandHeight", 0.5f,
                "落差判定上限（米）：点击到的地块、以及最终落点滩头，其海拔与海平面的差值都必须 ≤ 此值。\n" +
                "用于把\"悬崖顶/高台地\"判掉——原版只有与海面齐平的沙滩才能登陆。\n" +
                "若发现明明点的是沙滩却被判\"高地\"，把这个值调大一点（比如 0.8）。");
            WaterLevelY = cfg.Bind("Landing", "WaterLevelY", 0f,
                "海平面世界高度，作为落差判定的基准（原版海面 ≈ 0，一般不用改）。");
            ShowHoverPreview = cfg.Bind("Landing", "ShowHoverPreview", true,
                "投放模式下把鼠标扫过的地形实时算一遍并显示落点标记（亮青=可投放，暗红=不可投放），\n" +
                "用来直观确认\"哪里是能登陆的滩头\"。");
            MarkerSeconds = cfg.Bind("Landing", "MarkerSeconds", 2.5f,
                "落点光亮标记的保持时长（秒）。");
            MinLandingSpacing = cfg.Bind("Landing", "MinLandingSpacing", 1f,
                "滩头占用判定的**首选基础间距**（米）。实际首选要求 = 本值 + 本船船长（小船约 +0.7m）：\n" +
                "离点击处最近的候选滩头若已有船且更近，就换下一个候选（逐个试），不是直接拒绝。\n" +
                "附近都找不到时自动放宽到\"只要不重叠\"（≈ 2×船半径），所以调大它只会让你更倾向留出空档，不会导致投不出来。\n" +
                "原版自己的规则是**朝向盒不相交**（Landing.TryPlace 的 ColCube.CheckBox），等效间距 ≈ 0.7~1.4m。");
            LandingFallbackAnywhere = cfg.Bind("Landing", "LandingFallbackAnywhere", true,
                "附近（MaxShoreDistance 内）实在没有空位时，是否自动改用**全岛最近的可投放滩头**。\n" +
                "true = 小岛后期也能投放（HUD 会写明实际距离与\"已改用最近空滩头\"）；false = 直接拒绝并提示原因。");
            FlotillaDelay = cfg.Bind("Landing", "FlotillaDelay", 1f,
                "编队窗口（秒）：窗口内连续投放的船会合并成**同一支编队**（原版一波本就多船），\n" +
                "于是只播一条接近音乐、避免一次投好几艘时多段音乐同时响。0 = 每艘各自立即出发（原行为）。");
            FlotillaSpread = cfg.Bind("Landing", "FlotillaSpread", 2f,
                "编队内各船出发时间的散布上限（秒）：覆盖 Wave 出厂时的随机值，避免编队被拖到十几秒。0 = 同时出发。");
            FlotillaMaxShips = cfg.Bind("Landing", "FlotillaMaxShips", 6,
                "单支编队最多合并几艘船（超过就开新编队）。");
            FollowDifficultyShipSpeed = cfg.Bind("Landing", "FollowDifficultyShipSpeed", true,
                "船速是否跟随关卡难度倍率（原版用 difficulty.shipSpeedMultiplier，VeryHard 更快）。\n" +
                "true = 与原版手感一致；false = 只用 ShipSpeedMultiplier。");
            DisembarkGrace = cfg.Bind("Landing", "DisembarkGrace", 3f,
                "下船看门狗宽限期（秒）：船到岸后超过这么久还没人下船，就判定为卡住。\n" +
                "卡住时会打一条 [NewMode][下船] 完整诊断，并按 DisembarkFix 决定是否兜底。");
            DisembarkFix = cfg.Bind("Landing", "DisembarkFix", true,
                "卡住时的兜底下船开关（用原版公开成员完成，等价于原版 Pirate.MaybeAct 的后半段）。\n" +
                "true=自动把滞留敌人移下船；false=只打印诊断、保持原样（用于对照排查）。");
        }

        static void BindRemote(ConfigFile cfg)
        {
            RemoteControl = cfg.Bind("Remote", "RemoteControl", true,
                "遥控非原生单位（v1.4.7）：\n" +
                "**Shift + 左键/右键点**一个非原生单位 = 选中它所在的**整队**（再点同一队 = 取消）；按 **R** = 全选所有可选单位。\n" +
                "然后**左键或右键点一个地块** = 选中的单位集结到那里：按兵种各成一个小队，多兵种/人多时就地分到**相邻格**（占完为止）。\n" +
                "没有选中时，左右键完全交还原版（选我方 / 移动我方）。注意：遥控只接管行军，它们**仍是我方的敌人**。");
            RemoteMarqueePixels = cfg.Bind("Remote", "RemoteMarqueePixels", 8,
                "左键拖动超过这么多像素才算\"框选\"，否则视为单击（= 移动命令 / 点在自己单位上时忽略）。");
            RemoteGrabRadius = cfg.Bind("Remote", "RemoteGrabRadius", 64,
                "只有从\"非原生单位多少像素以内\"起拖才算框选（也是**左键点选**的命中半径）；从别处拖动仍然是原版的相机平移。\n" +
                "0 = 任意位置起拖都框选。点选总不中就把这个值调大（比如 96）。");
            RemoteFreeMarqueeKey = cfg.Bind("Remote", "RemoteFreeMarqueeKey", KeyCode.LeftAlt,
                "按住这个键再用左键拖动 = 从**任意位置**起拖都算框选（等价于临时把相机交给框选）。\n" +
                "KeyCode.None = 关闭该快捷键（则只有从单位附近起拖能框选）。默认 左Alt。");
            RemoteSoftCap = cfg.Bind("Remote", "RemoteSoftCap", 40,
                "每个兵种小队的上限：0 = 不限；>0 = 框选时按离框中心由近到远取满该数，**多出来的不组队、保持原逻辑**。");
            RemoteHighlight = cfg.Bind("Remote", "RemoteHighlight", true,
                "绘制框选矩形、受控单位与目标点标记（纯 GUI / 运行时贴图，不需要任何资源）。");
            RemoteClickRadius = cfg.Bind("Remote", "RemoteClickRadius", 1.2f,
                "**左键点选**的世界距离半径（米）：点击处的地面点与单位脚点相距 ≤ 本值即算点中。\n" +
                "用世界距离判定（与投放同一套 NavSpotCast），不依赖屏幕投影；点不中就调大（比如 2）。");
            RemoteSelectAllKey = cfg.Bind("Remote", "RemoteSelectAllKey", KeyCode.R,
                "一键选中所有\"可选\"的非原生单位（之后左/右键点地块即可集结前进）。默认 R；KeyCode.None = 关闭。");
            RemoteSlowMo = cfg.Bind("Remote", "RemoteSlowMo", true,
                "框选中或已有选中时**放慢时间**（与原版\"选中我方小队\"同一套 TimeManager，按 requester 取最小值合并）。\n" +
                "敌方单位一直在动，减速后更容易框住。默认开。");
            RemoteSlowMoScale = cfg.Bind("Remote", "RemoteSlowMoScale", 0.1f,
                "减速倍率（原版选中我方小队用的是 0.1）。0.1~0.35 之间比较舒服。");
        }

        static void BindDiag(ConfigFile cfg)
        {
            VerboseLog = cfg.Bind("Diag", "VerboseLog", false,
                "详细日志：打印点击命中的地形点/海拔、滩头点、距离、最终 shipPrefab 与失败原因。排查时开。");
            LogToFile = cfg.Bind("Diag", "LogToFile", false,
                "是否生成日志文件 BepInEx\\LogOutput.log（默认 false = 不生成，这也是本次改动的原因）。\n" +
                "BepInEx 启动时就建好了写文件的监听器，本插件加载时会把它摘掉并释放，并删掉本次启动刚写出的那几行；\n" +
                "**控制台窗口的日志不受影响**。设成 true 就完全不干预 BepInEx 原行为（排查时选不中 / 下船卡住等问题时用）。");
        }
    }
}
