using System.Collections.Generic;

namespace BadNorthNewMode
{
    /// <summary>菜单显示名 + 默认数量梯度；原版无敌方兵种显示名，取名依据见 PROJECT_SPEC §5。</summary>
    internal static class UnitNames
    {
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Viking_Sword",       "剑兵" },
            { "Viking_SwordShield", "盾兵" },
            { "Viking_Archer",      "弓箭手" },        // 官方用词
            { "Viking_AxeThrower",  "掷斧手" },
            { "Viking_Twohanded",   "双手剑士" },
            { "Viking_Berserker",   "狂战士" },        // 原版被禁用的兵种
            { "Viking_Tank",        "维京莽汉" },      // 取自官方 hint 用语
            { "Viking_TankArcher",  "重装弓箭手" },
        };

        static readonly Dictionary<string, string> MapEn = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)   // 英文显示名（官方用词）
        {
            { "Viking_Sword",       "Swordsman" },
            { "Viking_SwordShield", "Shield Swordsman" },
            { "Viking_Archer",      "Archer" },
            { "Viking_AxeThrower",  "Axe Thrower" },
            { "Viking_Twohanded",   "Two-handed Swordsman" },
            { "Viking_Berserker",   "Berserker" },
            { "Viking_Tank",        "Brute" },
            { "Viking_TankArcher",  "Brute Archer" },
        };

        static readonly Dictionary<string, int> DefaultCounts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)   // 每兵种默认装载数（梯度）；未收录回退原版公式
        {
            { "Viking_Sword",       12 },   // 最基础的剑兵 —— 成群
            { "Viking_SwordShield", 10 },   // 盾兵（能挡箭，比剑兵硬）
            { "Viking_Archer",       8 },   // 弓箭手
            { "Viking_AxeThrower",   7 },   // 掷斧手（近战 + 一柄飞斧）
            { "Viking_Twohanded",    5 },   // 双手剑士（高伤/高击退）
            { "Viking_Berserker",    5 },   // 狂战士（原版禁用）
            { "Viking_Tank",         1 },   // 巨人级：维京莽汉
            { "Viking_TankArcher",   1 },   // 巨人级：重装弓箭手
        };

        /// <summary>该兵种的默认装载数；未配置返回 0（调用方回退原版公式）。</summary>
        internal static int DefaultCount(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return 0;

            int n;
            if (DefaultCounts.TryGetValue(internalName, out n)) return n;
            return 0;
        }

        /// <summary>内部名 → 当前语言显示名；未收录的去掉 Viking_ 前缀原样显示。</summary>
        internal static string Of(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return Loc.T("随机");

            string shown;
            Dictionary<string, string> map = Loc.IsEnglish ? MapEn : Map;
            if (map.TryGetValue(internalName, out shown)) return shown;
            if (internalName.StartsWith("Viking_")) return internalName.Substring("Viking_".Length);
            return internalName;
        }
    }
}
