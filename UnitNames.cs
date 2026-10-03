using System.Collections.Generic;

namespace BadNorthNewMode
{
    /// <summary>
    /// 菜单显示名（简中）。
    /// 事实说明：原版 I2 本地化表里**没有**敌方兵种的显示名——
    /// `HINTS/UNITS/*` 只覆盖我方兵种（民兵/弓箭手/长矛兵/盾牌），唯一提到维京兵种的是
    /// `HINTS/UNITS/VIKING_TANKS` =「巨大的维京莽汉拳头凶猛有力…」；手抄本（`META_INVENTORY/TITLE`=手抄本）
    /// 里也只有我方职业与升级。因此这里采取"**官方用词优先**（弓箭手 / 盾 / 维京莽汉），
    /// 其余沿用本项目文档既有叫法"，并在菜单里同时显示内部名以对应 cfg。
    /// </summary>
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

        /// <summary>
        /// 每兵种的**默认装载数**（体现强度梯度：弱的成群、精锐少见、巨人级 1 个）。
        /// 未收录（含未来的自定义兵种）= 0 → 回退到原版公式（最小船容量 ÷ 单体面积）。
        /// 注意：最终仍会被"最大长船容量上限"裁剪（ClampSquadSize），所以这里给大值也安全。
        /// </summary>
        static readonly Dictionary<string, int> DefaultCounts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
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

        /// <summary>内部名 → 简中显示名；未收录的去掉 Viking_ 前缀原样显示。</summary>
        internal static string Of(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return "随机";

            string cn;
            if (Map.TryGetValue(internalName, out cn)) return cn;
            if (internalName.StartsWith("Viking_")) return internalName.Substring("Viking_".Length);
            return internalName;
        }
    }
}
