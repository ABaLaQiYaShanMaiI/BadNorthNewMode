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

        // ============ Landing ============
        public static ConfigEntry<float> ShipSpeedMultiplier;
        public static ConfigEntry<float> MaxShoreDistance;
        public static ConfigEntry<float> MaxLandHeight;
        public static ConfigEntry<float> WaterLevelY;
        public static ConfigEntry<bool> ShowHoverPreview;
        public static ConfigEntry<float> MarkerSeconds;
        public static ConfigEntry<float> DisembarkGrace;
        public static ConfigEntry<bool> DisembarkFix;

        // ============ Diag ============
        public static ConfigEntry<bool> VerboseLog;

        public static void Bind(ConfigFile cfg)
        {
            BindGeneral(cfg);
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
            SquadSize = cfg.Bind("General", "SquadSize", 6,
                "每艘船搭载的敌人数。会按最大船的容量自动裁剪上限（与原版 SetLoadCount 同一套 area 算法）。");
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
            DisembarkGrace = cfg.Bind("Landing", "DisembarkGrace", 3f,
                "下船看门狗宽限期（秒）：船到岸后超过这么久还没人下船，就判定为卡住。\n" +
                "卡住时会打一条 [NewMode][下船] 完整诊断，并按 DisembarkFix 决定是否兜底。");
            DisembarkFix = cfg.Bind("Landing", "DisembarkFix", true,
                "卡住时的兜底下船开关（用原版公开成员完成，等价于原版 Pirate.MaybeAct 的后半段）。\n" +
                "true=自动把滞留敌人移下船；false=只打印诊断、保持原样（用于对照排查）。");
        }

        static void BindDiag(ConfigFile cfg)
        {
            VerboseLog = cfg.Bind("Diag", "VerboseLog", false,
                "详细日志：打印点击命中的地形点/海拔、滩头点、距离、最终 shipPrefab 与失败原因。排查时开。");
        }
    }
}
