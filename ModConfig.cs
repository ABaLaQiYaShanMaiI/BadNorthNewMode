using BepInEx.Configuration;
using UnityEngine;

namespace BadNorthNewMode
{
    /// <summary>全部 cfg 收在本静态类：Bind 按段拆小方法，Plugin.Awake 只调一次。</summary>
    public static class ModConfig
    {
        // ============ General ============
        public static ConfigEntry<KeyboardShortcut> Hotkey;
        public static ConfigEntry<bool> ShowHud;
        public static ConfigEntry<string> EnemyName;
        public static ConfigEntry<int> SquadSize;

        // ============ Landing ============
        public static ConfigEntry<float> ShipSpeedMultiplier;
        public static ConfigEntry<float> MaxShoreDistance;
        public static ConfigEntry<float> MinOutwardDot;
        public static ConfigEntry<float> WaterLevelY;

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
            Hotkey = cfg.Bind("General", "Hotkey", new KeyboardShortcut(KeyCode.F1),
                "进/出投放模式的按键。默认 F1。进入后点击水面投放敌舰，右键或 Esc 取消。");
            ShowHud = cfg.Bind("General", "ShowHud", true,
                "左上角显示模式状态与上一次投放结果（纯 GUI 文本，不需要任何资源）。");
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
            MaxShoreDistance = cfg.Bind("Landing", "MaxShoreDistance", 6f,
                "点击水面处到最近滩头岸线点的最大距离（米）。超过就判定为\"离岸太远\"并拒绝投放。");
            MinOutwardDot = cfg.Bind("Landing", "MinOutwardDot", 0.25f,
                "方向校验阈值：点击方向与滩头朝海外法线的点积下限。\n用于把\"点在岛上/点在海湾内侧\"判掉——否则船会从陆地深处开过来。0=不校验。");
            WaterLevelY = cfg.Bind("Landing", "WaterLevelY", 0f,
                "海平面世界高度，用于把鼠标射线换算成水面点。原版海面≈0，一般不用改。");
        }

        static void BindDiag(ConfigFile cfg)
        {
            VerboseLog = cfg.Bind("Diag", "VerboseLog", false,
                "详细日志：打印每次点击的水面点、滩头点、方向点积、最终 shipPrefab 与失败原因。排查时开。");
        }
    }
}
