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
            { "Viking_Spearman",    "长矛兵" },        // 《旧灰复燃的战旗》整合包的外生兵种（克隆投斧手引用注册，见兼容方案 §10.3）
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
            { "Viking_Spearman",    "Spearman" },
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

        /// <summary>该名字是不是**原版内置**兵种（内置表里有 = 原版；没有 = 别的 mod 加进来的外生兵种）。</summary>
        internal static bool IsKnownVanilla(string internalName)
        {
            return !string.IsNullOrEmpty(internalName) && Map.ContainsKey(internalName);
        }

        static string _customRaw;
        static Dictionary<string, string> _custom;

        /// <summary>cfg `[UI] UnitNameOverrides` 里的自定义显示名（`内部名=显示名;…`）。
        /// 原串变了才重解析 → cfg 热改也能立刻生效；不合法片段直接跳过（绝不抛）。</summary>
        static string Custom(string internalName)
        {
            string raw = Util.V(ModConfig.UnitNameOverrides, null);
            if (string.IsNullOrEmpty(raw)) return null;

            if (!string.Equals(raw, _customRaw, System.StringComparison.Ordinal))
            {
                _customRaw = raw;
                Dictionary<string, string> map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                string[] parts = raw.Split(new char[] { ';', '；', '\n', '\r' });
                for (int i = 0; i < parts.Length; i++)
                {
                    string s = parts[i];
                    if (string.IsNullOrEmpty(s)) continue;

                    int eq = s.IndexOf('=');
                    if (eq <= 0 || eq >= s.Length - 1) continue;

                    string k = s.Substring(0, eq).Trim();
                    string v = s.Substring(eq + 1).Trim();
                    if (k.Length > 0 && v.Length > 0) map[k] = v;
                }
                _custom = map;
            }

            string hit;
            return (_custom != null && _custom.TryGetValue(internalName, out hit)) ? hit : null;
        }

        /// <summary>没收录的名字"可读化"：`WeiJingSpearman` → `Wei Jing Spearman`（只影响显示，不碰内部名）。</summary>
        static string Humanize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length + 6);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>内部名 → 当前语言显示名。优先级：**cfg 覆盖** → 内置表 → 去掉 `Viking_` 前缀（自动分字）。
        /// 外生兵种（别的 mod 加的）走最后一条，因此永远不会"没名字"或显示成空。</summary>
        internal static string Of(string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return Loc.T("随机");

            string custom = Custom(internalName);
            if (!string.IsNullOrEmpty(custom)) return custom;

            string shown;
            Dictionary<string, string> map = Loc.IsEnglish ? MapEn : Map;
            if (map.TryGetValue(internalName, out shown)) return shown;

            if (internalName.StartsWith("Viking_", System.StringComparison.Ordinal))
                return Humanize(internalName.Substring("Viking_".Length));
            return Humanize(internalName);
        }
    }
}
