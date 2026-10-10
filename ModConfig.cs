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

        /// <summary>菜单打开时盖住"原版点击"（v1.5.6，见 PROJECT_SPEC §6 T2）。</summary>
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
                "进/出投放模式的按键。默认 F1。进入后点击水面投放敌舰，右键或 Esc 取消。");
            ShowHud = cfg.Bind("General", "ShowHud", true,
                "左上角显示模式状态与上一次投放结果（纯 GUI 文本，不需要任何资源）。");
            MenuBlocksWorldClicks = cfg.Bind("General", "MenuBlocksWorldClicks", true,
                "菜单打开时，在菜单矩形上盖一层不可见的 UI 拦截面：让游戏自己的点击（选中/移动我方小队）不再被菜单上的点击顺带触发。\n" +
                "原理 = 原版点击走 EventSystem 射线，被这层挡下后手势接收器收不到按下事件；IMGUI 菜单本身不属于 EventSystem，所以只有这样才能拦住。\n" +
                "false = 回到旧行为（点菜单时游戏仍会收到这次点击，可能选中压在菜单下的小队）。");
            CleanupHotkey = cfg.Bind("General", "CleanupHotkey", new KeyboardShortcut(KeyCode.F2, new KeyCode[0]),
                "一键清场：销毁本 mod 投放过的所有船/单位（调试用）。\n" +
                "销毁是安全的——Agent.OnDestroy 会自行从 faction.agents 摘除，不会留脏数据卡结算。默认 F2。");
            ForceWinHotkey = cfg.Bind("General", "ForceWinHotkey", new KeyboardShortcut(KeyCode.F3, new KeyCode[0]),
                "强制胜利：直接走**原版胜利流程**（结算屏 / 成就 / 存档 / checkpoint 全部正常，不是伪造状态）。\n" +
                "用途 = 接管模式（BlockVanillaWaves 或自己投放的敌人）打完后原版判定不一定能满足，用这个收尾。默认 F3。");
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

        static void BindUi(ConfigFile cfg)
        {
            UseVanillaUI = cfg.Bind("UI", "UseVanillaUI", true,
                "借用**原版 UI**：\n" +
                "① 提示消息改走原版通知条（游戏自己的羊皮纸提示 + 自带上/下音效），不再只是我们那块黑底文字；\n" +
                "②「清场」「释放遥控」这类破坏性操作弹**原版确认框**（标题 + 正文 + 确定/取消，键盘与手柄都能点）。\n" +
                "取不到原版对象时自动回退（提示回到 HUD 文本、确认框直接执行），只打一条日志，不会报错。默认开。");
            UseVanillaFont = cfg.Bind("UI", "UseVanillaFont", true,
                "菜单与 HUD 使用**游戏自带字体**（从原版 UI 借同一个 Font 对象）——中英文显示与游戏内一致，不需要任何字体文件。\n" +
                "false = 用 IMGUI 默认字体（缺中文字形时可能显示为方块，仅排障用）。默认开。");
            UiFontSize = cfg.Bind("UI", "UiFontSize", 13,
                "菜单 / HUD 字号（9~20）。默认 13（菜单会相应变高一点，看得更清）；嫌挡岛就调回 12。");
            UiFontName = cfg.Bind("UI", "FontName", "",
                "界面字体名，留空 = 自动。自动顺序：① 原版**自己的艺术字**（按资源名找：`Body_Chinese_Simp` → `Body_Chinese` → `Body` → `Buttons`）\n" +
                "② 原版语言字体表（`UserSettingsMenu.fontMap`）③ 系统里能画中文的字体（微软雅黑 / 思源黑体 / 黑体 …）④ 原版某个 Text 用的字体。\n" +
                "要指定就填**系统已安装的字体名**（`Font.GetOSInstalledFontNames()` 里的名字，例如 `Microsoft YaHei UI`、`SimHei`）。\n" +
                "日志里会打印 `[NewMode] UI 字体 = game:xxx / vanilla:xxx / os:xxx / cfg:xxx`，据此判断用的是哪一支。");
            UiPanelColor = cfg.Bind("UI", "PanelColor", "",
                "面板/横幅的**底色**（照坏北官网配色：**沙色**）。格式 `RRGGBB` / `RRGGBBAA`，可带 `#`；留空 = 内置的 `D5D0C8`\n" +
                "（标题栏用同系的**蓝灰** `89A1AD`，会自动跟着变）。");
            UiButtonColor = cfg.Bind("UI", "ButtonColor", "",
                "按键/行的**底色**（坏北官网那种**淡黄键**，上面配黑字）。格式同上；留空 = 内置的 `E5C98E`\n" +
                "（悬停略亮 `F0D69F`、开关\"开\"略深 `D2B375`；当前兵种是**蓝灰 + 原版虚线焦点框**）。想更艳填 `F2C15A`，更淡填 `EBD7AE`。");
            ConfirmDestructive = cfg.Bind("UI", "ConfirmDestructive", true,
                "**破坏性操作前弹确认框**：`F2` 清场（销毁本模组投放的船与单位）与 F1 菜单里的「一键释放遥控」。\n" +
                "false = 点一下立刻执行（自己反复调试时更顺手）。默认开。");
            ShowSquadBar = cfg.Bind("UI", "SquadBar", true,
                "屏幕底部的**小队头像条**（新功能）：把场上可选的小队按「一次投放 / 一次登陆」分队排成一行，\n" +
                "**左键点一下 = 选中整队**（按住 Shift = 并入 / 移出），亮青 = 已选中、青色图标 = 已在受控小队里。\n" +
                "在投放菜单里点它 = 先收起菜单再选中（省得先按 F1）。默认开。");
            SquadBarMax = cfg.Bind("UI", "SquadBarMax", 8,
                "头像条最多显示几格（按人数从多到少取；屏幕放不下会自动再少显示几格）。");
            SquadBarAlign = cfg.Bind("UI", "SquadBarAlign", "Center",
                "头像条的**横向**位置：`Left` / `Center` / `Right`。默认居中；若你觉得和原版底部的 UI 挤在一起，就改 `Left` 或 `Right`。");
            SquadBarBottom = cfg.Bind("UI", "SquadBarBottom", -1,
                "头像条的**纵向**位置（离屏幕底部多少像素）：\n" +
                "-1（默认）= 自动：平时贴底，但**你选中我方小队、原版底部弹出技能条时，会自动抬到技能条上方**；\n" +
                ">= 0 = 固定离底像素（这时不再自动避让）。");
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

        static void BindNative(ConfigFile cfg)
        {
            BlockVanillaWaves = cfg.Bind("Native", "BlockVanillaWaves", false,
                "**无尽自定义模式**：进岛后彻底拦下原版波次（每帧重写 waveStartTime —— 原版协程会在最后一刻按排序赋它，只拦一次会被覆盖），\n" +
                "于是本关只会出现你自己投放的单位，且这些永不发射的登陆点不再占滩头（投放更宽松）。\n" +
                "⚠️ 本关**不会自然结束**：清光自己投放的单位也不会判胜（留给你思考布局的时间）—— 退出请按 F3 强制胜利。\n" +
                "关掉开关会把原版波次的计时还回去（原版敌人会立刻补发）。默认关。");
            ControlNativeUnits = cfg.Bind("Native", "ControlNativeUnits", true,
                "**控制敌我**：让**原生单位**（原版上岛的敌人）也能被选中 / 框选 / 点地块前进。\n" +
                "开启后它们与**非原生单位**（本 mod 投放的）共用同一套操作；被遥控期间不再自行作战。\n" +
                "F2 清场与离开战局**不会销毁**它们（只撤我们的登记）。与 BlockVanillaWaves 同时开启时没有意义（那时没有原生单位）。\n" +
                "（v1.6.0 起本项改名为 ControlNativeUnits：旧的 RemoteNativeUnits=false 不再被读取，默认开。）");
        }

        static void BindRemote(ConfigFile cfg)
        {
            RemoteControl = cfg.Bind("Remote", "RemoteControl", true,
                "遥控投放的单位（鼠标方案自动跟随游戏的单/双键设置，见 RemoteOrderButton）：\n" +
                "双键：左键点单位 = 选中它**自己**（精确）；**双击** = 选中它所在**整队**；右键点地块 = 前进。\n" +
                "单键 / 触摸：没选中时点单位 = 选中；有选中时点地块 = 前进（与原版同一套映射）。\n" +
                "Shift + 点 = 并入 / 移出；R = 全选；Alt + 拖动 = 框选；点海面 = 取消选中。\n" +
                "有选中时点地块 = 按兵种各成一个小队前进，多兵种 / 人多时就地分到**相邻格**。\n" +
                "还在船上也能选：点地块先记住\"登陆后的集结点\"，落地后自动前往。\n" +
                "注意：遥控只接管行军，它们**仍是我方的敌人**（会照常烧房子等，属于敌方行为）。");
            RemoteOrderButton = cfg.Bind("Remote", "RemoteOrderButton", "Auto",
                "遥控的鼠标操作方式（v1.5.6 起**自动跟随游戏的单/双键设置**，即设置里的光标模式）：\n" +
                "Auto / Right（默认）= 对齐原版：\n" +
                "    双键模式：左键点我们的单位 = 选整队（不用按 Shift）、右键点地块 = 前进；\n" +
                "    单键 / 触摸模式：一个键按当前有没有选中决定——没选中时点我们的单位 = 选整队，有选中时点地块 = 前进（与原版同一套映射）。\n" +
                "Left = v1.5.4 的旧手感：左键点地块即前进（内部等 2 帧确认原版没接管，手感略慢），且不随单/双键适配。\n" +
                "两种模式都有：Shift + 点 = 并入/移出选择；R = 全选；Alt + 拖动 = 框选。");
            RemoteMoveRequiresModifier = cfg.Bind("Remote", "RemoteMoveRequiresModifier", false,
                "下令是否必须按住 Shift / R：\n" +
                "false（默认）= 有选中时**普通点击地块**也能下令（更顺手），代价是每次下令多等 2 帧（约 33ms）用于确认原版是否接管这次点击；\n" +
                "true = 回到 v1.5.3 的手感——不按修饰键的点击 100% 归原版（最保守）。");
            RemoteMarqueePixels = cfg.Bind("Remote", "RemoteMarqueePixels", 8,
                "左键拖动超过这么多像素才算\"框选\"，否则视为单击（= 按住 Shift 时下达前进命令 / 点在自己单位上时忽略）。");
            RemoteGrabRadius = cfg.Bind("Remote", "RemoteGrabRadius", 64,
                "只有从\"非原生单位多少像素以内\"起拖才算框选（也是 **Shift+左键点选** 的命中半径）；从别处拖动仍然是原版的相机平移。\n" +
                "0 = 任意位置起拖都框选。点选总不中就把这个值调大（比如 96）。\n" +
                "注意：v1.6.0 起\"从单位上起拖 = 框选\"默认**关闭**（见 RemoteMarqueeFromUnit），本值只在它开启时用于起拖判定与 Shift 点选。");
            RemoteFreeMarqueeKey = cfg.Bind("Remote", "RemoteFreeMarqueeKey", KeyCode.LeftAlt,
                "按住这个键 + 左键拖动 = 框选（v1.6.0 起这是**默认唯一**的框选入口；不按它就都是原版相机平移）。\n" +
                "按下左键**前后**按住都认：中途补按会把这一次拖动就地转成框选（起点 = 补按处）；左右 Alt 都认。\n" +
                "KeyCode.None = 关闭该快捷键（则框选只能靠 RemoteMarqueeFromUnit 那条老路）。默认 左Alt。");
            RemoteMarqueeFromUnit = cfg.Bind("Remote", "RemoteMarqueeFromUnit", false,
                "是否保留旧版（v1.5.7）的老手感：**从自己的单位上起拖**也算框选（不必按 Alt）。\n" +
                "false（默认，v1.6.0）= 拖动永远是原版相机平移，框选请按 Alt；\n" +
                "true = 老行为（想拖着看地图时容易误框选，按需开启）。");
            RemoteSoftCap = cfg.Bind("Remote", "RemoteSoftCap", 40,
                "每个兵种小队的上限：0 = 不限；>0 = 框选时按离框中心由近到远取满该数，**多出来的不组队、保持原逻辑**。");
            RemoteHighlight = cfg.Bind("Remote", "RemoteHighlight", true,
                "绘制框选矩形、受控单位与目标点标记（纯 GUI / 运行时贴图，不需要任何资源）。");
            RemoteClickRadius = cfg.Bind("Remote", "RemoteClickRadius", 1.2f,
                "**Shift+左键点选**的世界距离半径（米）：点击处的地面点与单位脚点相距 ≤ 本值即算点中。\n" +
                "用世界距离判定（与投放同一套 NavSpotCast），不依赖屏幕投影；点不中就调大（比如 2）。");
            RemoteSelectAllKey = cfg.Bind("Remote", "RemoteSelectAllKey", KeyCode.R,
                "一键选中所有\"可选\"的非原生单位；**按住该键再点左/右键 = 全选并直接前进**（不必再按 Shift）。默认 R；KeyCode.None = 关闭（同时失去\"按住下令\"这条通道）。");
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
