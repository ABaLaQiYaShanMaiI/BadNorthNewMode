using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>
    /// 《旧灰复燃的战旗》整合包（林风柳 V3.0+）与联机模组 Together 的"让路"检测（v1.7.1）。
    /// 原则（见 BADNORTHMOD_COMPAT_PLAN.md §3.4 / TOGETHER_MP_SPEC.md §8）：
    ///   ① **零 Harmony 补丁**：只用 `Chainloader.PluginInfos` 与只读反射，绝不改别人的行为；
    ///   ② **检测失败一律按"没有"处理**（宁可不禁用我们的功能，也不误锁单机玩家）；
    ///   ③ 所有让路都有 cfg 开关，关掉即回到旧行为（见 ModConfig.BindCompat）。
    ///
    /// 让路面（v1.7.1）：**投放**（联机中 / 整合包终岛无尽）、**遥控**（联机中）、**拦下原版波次**（被接管波次的插件排斥）、
    /// **强制胜利**（整合包 / 联机环境先二次确认）。
    /// </summary>
    internal static class Compat
    {
        // ---- 整合包插件的 GUID（v1.7.1 实测自各 DLL 的 [BepInPlugin]，见兼容方案 §3.4）----
        internal const string GuidTogether = "com.linfeng.together";
        internal const string GuidEndlessFinale = "com.antigravity.badnorth.endless_finale";
        internal const string GuidSageTimeFreeze = "com.antigravity.sage_time_freeze";
        internal const string GuidCommandersSailIn = "com.antigravity.badnorth.commanders_sail_in_endless_finale";

        static readonly string[] PackGuids = new string[]
        {
            "com.linfeng.shield_spear", "com.linfeng.heavy_shield",
            "com.modder.viking_spearman", "com.modder.guerrilla_archery",
            "com.modder.giant_command_ring", "com.modder.badnorth.skillicons",
            GuidEndlessFinale, GuidCommandersSailIn, GuidSageTimeFreeze,
            "com.antigravity.firearrows", "com.antigravity.banner_enhance", "com.antigravity.audio_mine",
            "com.badnorth.plentytraits.axeenhance", "com.badnorth.cache32", "com.six.june.wash_class",
            "com.yyyyy.deadisnotenemy", "fancy.tankarcherfix",
            "nacu.bnapi", "nacu.plentytraits", "nacu.bringbackberserkers"
        };

        /// <summary>整合包**就地改造**游戏本体后才会出现的类型（原版没有）→ 即使插件被删也能认出"改造版 Assembly-CSharp"。</summary>
        static readonly string[] PackOnlyTypes = new string[]
        {
            "Voxels.TowerDefense.SpearShield",
            "Voxels.TowerDefense.Upgrades.ShieldChargeAbility",
            "Voxels.TowerDefense.Upgrades.ShieldJumpEnforcer",
            "Voxels.TowerDefense.CanGroundPound"
        };

        /// <summary>只可能由整合包挂上的组件（投放后自检用；只比类型名，不引用包内类型）。</summary>
        static readonly string[] EnhancementTypes = new string[]
        {
            "SpearShield", "ShieldChargeAbility", "ShieldJumpEnforcer", "BurningShipsBuff",
            "GiantSwordBuff", "RegenerativeBuff", "RegenerativeSpearBuff",
            "RegenerativeJumpResponder", "SwordsmanRecovery", "CanGroundPound"
        };

        static bool _packProbed;
        static bool _packByType;

        /// <summary>Together 会话静态类的**类型全名**候选（不带程序集限定）：第一个是**实测名字**
        /// （Cecil 读 `Together.dll` v0.6.13 + 32 位 PowerShell 实跑 `asm.GetType` 双重确认），
        /// 第二个是兼容笔记 `TOGETHER_MP_SPEC.md` 里写的名字（防它将来改回去）。
        /// 为什么必须两个都试：写错名字时取到 null → 让路**静默失效**（联机里投放/遥控都会真的执行）。</summary>
        static readonly string[] SessionTypeNames = new string[]
        {
            "Together.Session",
            "Together.Net.Session",
        };

        static Type _sessionType;
        static PropertyInfo _sessionActive;
        static bool _probeWarned;
        static bool _waveBlockWarned;
        static bool _remoteWarned;
        static bool _sessionDropWarned;
        static bool _lastTerminal;

        // ===================== 基础探测 =====================

        /// <summary>BepInEx 插件是否加载（Chainloader 是公开 API；异常一律当"没有"）。</summary>
        internal static bool PluginLoaded(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            try
            {
                Dictionary<string, PluginInfo> infos = Chainloader.PluginInfos;
                return infos != null && infos.ContainsKey(guid);
            }
            catch { return false; }
        }

        static Type PackType(string fullName)
        {
            Assembly asm = FindAssembly("Assembly-CSharp");       // 同 FindAssembly：不用带程序集限定的 Type.GetType
            if (object.ReferenceEquals(asm, null)) return null;
            try { return asm.GetType(fullName, false); }
            catch { return null; }
        }

        /// <summary>按简单名找**已加载**的程序集（`AppDomain.GetAssemblies`）。
        /// ⚠️ 为什么不用 `Type.GetType("NS.T, 程序集")`：宿主可能以 LoadFrom 上下文装载插件，
        /// 那种带程序集限定的取法实测会返回 null（32 位 PowerShell 已复现）→ 会让探测静默失效。</summary>
        static Assembly FindAssembly(string simpleName)
        {
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    AssemblyName n = asms[i].GetName();
                    if (n != null && n.Name == simpleName) return asms[i];
                }
            }
            catch { }
            return null;
        }

        static bool AssemblyLoaded(string simpleName)
        {
            return !object.ReferenceEquals(FindAssembly(simpleName), null);
        }

        /// <summary>cfg 强制离线（排障用）：为真时一切检测都按"没有整合包 / 没联机"处理。</summary>
        static bool ForcedOffline { get { return Util.V(ModConfig.CompatForceOffline, false); } }

        /// <summary>是否装了整合包（20 个已知 GUID 任一命中，或游戏本体的特征类型存在）。</summary>
        internal static bool PackPresent
        {
            get
            {
                if (ForcedOffline) return false;
                if (PackPluginCount() > 0) return true;
                if (!_packProbed)
                {
                    _packProbed = true;
                    for (int i = 0; i < PackOnlyTypes.Length; i++)
                    {
                        // 注意：Type 的 == / != 是 .NET 4 才有的运算符（游戏 mscorlib 2.0 没有）→ 一律用 ReferenceEquals。
                        if (object.ReferenceEquals(PackType(PackOnlyTypes[i]), null)) continue;
                        _packByType = true;
                        break;
                    }
                }
                return _packByType;
            }
        }

        /// <summary>命中的整合包插件数（用于 HUD / 自检日志；2 秒缓存 —— 运行期插件集合不会变，但避免每帧 20 次字典查找）。</summary>
        internal static int PackPluginCount()
        {
            if (ForcedOffline) return 0;
            if (Time.realtimeSinceStartup < _packCountUntil) return _packCount;

            int n = 0;
            for (int i = 0; i < PackGuids.Length; i++) if (PluginLoaded(PackGuids[i])) n++;
            _packCount = n;
            _packCountUntil = Time.realtimeSinceStartup + 2f;
            return n;
        }

        static int _packCount = -1;
        static float _packCountUntil;

        internal static bool TogetherLoaded { get { return !ForcedOffline && PluginLoaded(GuidTogether); } }

        /// <summary>Together 是否正处在联机会话里（只读反射会话类的 `Active` 属性；异常/失败都当"没联机"）。
        /// **实时读取**：F1 / 投放这些"决策点"必须用这个（缓存会漏掉"刚连上就按 F1"）。</summary>
        internal static bool TogetherOnline
        {
            get { return ProbeTogetherOnline(); }
        }

        /// <summary>联机中且**本机是客机**（= 世界由主机权威；本地改船速无意义）→ 菜单据此置灰。</summary>
        internal static bool SessionClient
        {
            get
            {
                if (!TogetherOnline) return false;
                try
                {
                    PropertyInfo isHost = SessionProperty("IsHost");
                    if (object.ReferenceEquals(isHost, null)) return false;      // 探测不到 → 不误禁
                    object v = isHost.GetValue(null, null);
                    return (v is bool) && !(bool)v;
                }
                catch { return false; }
            }
        }

        /// <summary>联机中的角色（HUD 用；探测不到返回 null → 就显示不带角色那版文案，不影响让路）。</summary>
        internal static string TogetherRole
        {
            get { RefreshTogetherCache(); return _roleCached; }
        }

        /// <summary>HUD 专用：0.5 秒缓存 —— 每帧反射取值没必要（决策点请用 `TogetherOnline`）。</summary>
        internal static bool TogetherOnlineCached
        {
            get { RefreshTogetherCache(); return _togetherCached; }
        }

        static void RefreshTogetherCache()
        {
            if (Time.realtimeSinceStartup < _togetherUntil) return;
            _togetherCached = ProbeTogetherOnline();
            _roleCached = _togetherCached ? ProbeTogetherRole() : null;
            _togetherUntil = Time.realtimeSinceStartup + 0.5f;
        }

        static bool _togetherCached;
        static string _roleCached;
        static float _togetherUntil;

        static bool ProbeTogetherOnline()
        {
            if (!TogetherLoaded) return false;
            try
            {
                PropertyInfo active = SessionProperty("Active");
                if (object.ReferenceEquals(active, null)) return false;

                object v = active.GetValue(null, null);
                return (v is bool) && (bool)v;
            }
            catch (Exception e) { WarnProbeOnce(e.GetType().Name); return false; }
        }

        /// <summary>"我在联机里是主机还是客机"（`Session.IsHost`；探测不到返回 null，不影响让路判定）。</summary>
        static string ProbeTogetherRole()
        {
            try
            {
                PropertyInfo isHost = SessionProperty("IsHost");
                if (object.ReferenceEquals(isHost, null)) return null;

                object v = isHost.GetValue(null, null);
                if (!(v is bool)) return null;
                return ((bool)v) ? Loc.T("主机") : Loc.T("客机");
            }
            catch { return null; }
        }

        /// <summary>取会话类的某个 public static bool 属性（`Active` 必需、`IsHost` 可选）；取不到只 Warn 一次。
        /// `Active` 命中后会缓存 —— 它是决策点热路径（每帧遥控让路判定 + 每次 F1），避免反复 `GetProperty`。</summary>
        static PropertyInfo SessionProperty(string name)
        {
            if (name == "Active" && !object.ReferenceEquals(_sessionActive, null)) return _sessionActive;

            if (object.ReferenceEquals(_sessionType, null)) _sessionType = FindSessionType();
            if (object.ReferenceEquals(_sessionType, null)) { WarnProbeOnce("类型缺失"); return null; }

            PropertyInfo p = _sessionType.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (object.ReferenceEquals(p, null) || !object.ReferenceEquals(p.PropertyType, typeof(bool)))
            {
                if (name == "Active") WarnProbeOnce("属性缺失");     // 只有 Active 是必需的；IsHost 缺了不告警
                return null;
            }
            if (name == "Active") _sessionActive = p;
            return p;
        }

        /// <summary>找 Together 的会话静态类：先用**程序集自身**的 `GetType` 试两个候选名，并要求它真的有
        /// `Active`(bool) 才算命中（这样名字报错时日志会留下"类型缺失"，而不是悄悄让路失效）。
        /// ⚠️ 不要用 `Type.GetType("Together.Session, Together")`：宿主可能以 LoadFrom 上下文装载插件，
        /// 那种取法实测返回 null（32 位 PowerShell 已复现）。</summary>
        static Type FindSessionType()
        {
            Assembly asm = FindAssembly("Together");
            if (object.ReferenceEquals(asm, null)) return null;

            for (int i = 0; i < SessionTypeNames.Length; i++)
            {
                Type t = null;
                try { t = asm.GetType(SessionTypeNames[i], false); }
                catch { t = null; }
                if (object.ReferenceEquals(t, null)) continue;

                PropertyInfo p = t.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
                if (!object.ReferenceEquals(p, null) && object.ReferenceEquals(p.PropertyType, typeof(bool)))
                    return t;
            }
            return null;
        }

        static void WarnProbeOnce(string reason)
        {
            if (_probeWarned) return;
            _probeWarned = true;
            Util.Warn(Loc.F("[NewMode][兼容] Together 会话状态探测失败（{0}）→ 按未联机处理", reason));
        }

        /// <summary>本关是不是"终岛"：原版 `LevelNode.isEnd`（整合包的终岛无尽用的就是这个判定）。</summary>
        internal static bool TerminalIsland(IslandGameplayManager gm)
        {
            Island island = (gm != null) ? gm.island : null;
            if (island == null || island.levelNode == null) return false;
            return island.levelNode.isEnd;
        }

        /// <summary>每帧记录"本关是否终岛"（HUD 在拿不到 gm 的上下文里也要用）。</summary>
        internal static void Tick(IslandGameplayManager gm)
        {
            _lastTerminal = TerminalIsland(gm);
        }

        // ===================== 让路策略（全部由 cfg 开关控制）=====================

        /// <summary>投放是否让路：Together 联机中，或"整合包终岛无尽"正在接管本关。</summary>
        internal static bool DropBlocked(IslandGameplayManager gm, out string why)
        {
            why = null;
            if (!Util.V(ModConfig.CompatAutoLetGo, true)) return false;

            if (TogetherOnline)
            {
                why = Loc.T("联机中（Together 在线）：本模组的投放会让整局联机冻结，已自动让路");
                return true;
            }
            if (PluginLoaded(GuidEndlessFinale) && !Util.V(ModConfig.CompatDropOnTerminal, false) && TerminalIsland(gm))
            {
                why = Loc.T("终岛无尽由整合包接管：本关投放已自动让路（可在 cfg 的 [Compat] 段调整）");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 拦下原版波次是否被禁止。
        /// **作者口径（2026-10-14 明确）**：拦下原版波次 = "把任意一关变成无限试炼场"的核心设计，1.7.1 **不得让它失效**
        /// → 单机（**包括装了整合包**）一律允许，只给一次"与接管波次的插件叠加"的提示（见 `WaveBlockRiskNote`）。
        /// 唯一的硬拒绝是 **Together 联机中**：每帧改写 `waveStartTime` 会让主机的波次永远发布不出去，
        /// 把整局合作打停在冻结态（TOGETHER_MP_SPEC §8 第 1/2 条），那属于"毁掉别人的存档"，不是让路。
        /// </summary>
        internal static bool WaveBlockForbidden(out string why)
        {
            why = null;
            if (!Util.V(ModConfig.CompatAutoLetGo, true)) return false;
            if (Util.V(ModConfig.CompatBlockWavesWithOthers, false)) return false;   // 强制开关：联机也允许（后果自负）

            if (TogetherOnline)
            {
                why = Loc.T("Together 联机中：拦波次会与主机的波次校验冲突");
                return true;
            }
            return false;
        }

        /// <summary>单机下"拦下原版波次"与整合包叠加的一次性**提示**（不拒绝，只说明可能的现象）。没有叠加时返回 null。</summary>
        internal static string WaveBlockRiskNote()
        {
            if (ForcedOffline) return null;

            if (PluginLoaded(GuidEndlessFinale))
                return Loc.T("叠加提示：整合包的终岛无尽也在管波次 —— 本关本来就不会再出波（这正是无限试炼的预期），但整合包的无尽可能不会推进");
            if (PluginLoaded(GuidSageTimeFreeze))
                return Loc.T("叠加提示：整合包的贤者时缓会门控波次 —— 叠加拦波次后它的缓速节奏可能一起停住");
            if (PluginLoaded(GuidCommandersSailIn))
                return Loc.T("叠加提示：整合包的入场运镜会延迟维京波次 —— 叠加拦波次后可能一直停在场运镜阶段");
            return null;
        }

        /// <summary>强制胜利是否需要二次确认（整合包/联机环境下改结算容易与对方口径对撞）。</summary>
        internal static bool ForceWinConfirmNeeded
        {
            get
            {
                if (!Util.V(ModConfig.CompatForceWinConfirm, true)) return false;
                return TogetherOnline || PackPresent;
            }
        }

        // ===================== 状态行与自检日志 =====================

        /// <summary>HUD 用的一行状态；无需提示时返回 null（零噪音）。用的都是缓存读取，不进每帧反射。</summary>
        internal static string StatusLine()
        {
            if (ForcedOffline) return null;
            if (TogetherOnlineCached)
            {
                string role = TogetherRole;      // 可能为 null（探测不到 IsHost → 退回不带角色那版文案）
                return string.IsNullOrEmpty(role)
                    ? Loc.T("兼容：Together 联机中 · 投放/遥控已让路")
                    : Loc.F("兼容：Together 联机中（{0}）· 投放/遥控已让路", role);
            }
            if (PluginLoaded(GuidEndlessFinale) && !Util.V(ModConfig.CompatDropOnTerminal, false) && _lastTerminal)
                return Loc.T("兼容：整合包终岛无尽 · 本关投放已让路");
            int n = PackPluginCount();
            if (n > 0) return Loc.F("兼容：检测到整合包（{0} 个插件）", n);
            if (PackPresent) return Loc.T("兼容：游戏本体已被整合包改造");
            return null;
        }

        /// <summary>拦波次被拒时打一次日志（LevelTools 每帧都会调，这里做去重）。</summary>
        internal static void WarnWaveBlockRefused(string why)
        {
            if (_waveBlockWarned) return;
            _waveBlockWarned = true;
            Util.Warn(Loc.F("[NewMode][兼容] 拦下原版波次被拒绝：{0}（cfg [Compat] 段 BlockWavesWithOthers=true 可强制打开）", why));
        }

        /// <summary>
        /// 拒绝打开"拦下原版波次"（v1.7.1-b，见兼容方案 §3.3 C4-B）：**把 cfg 写回 false** 并提示一次。
        /// 为什么必须回写：F1 菜单的开关是直接读 cfg 值画的 —— 只拦行为不回写，菜单会一直显示"原版波次：拦下"，
        /// 与实际状态不符（用户以为开着，其实每帧都被拒绝）。写回后 LevelTools 下一帧就不会再进来，天然只执行一次。
        /// </summary>
        internal static void RefuseWaveBlock(string why)
        {
            if (ModConfig.BlockVanillaWaves != null && ModConfig.BlockVanillaWaves.Value)
                ModConfig.BlockVanillaWaves.Value = false;            // BepInEx 会写回 cfg：下次启动也不会再犯
            WarnWaveBlockRefused(why);
            IngameMenu.Say(Loc.T("已拒绝：拦下原版波次会与整合包 / 联机的波次调度叠加，出现「波次永远不来」"));
        }

        /// <summary>遥控下单是否让路（v1.7.1-b）：Together 联机中，客机的指令被主机姿态流每帧覆盖（等于无效），
        /// 主机的指令又会被当成真实变化流出去、污染对方的波次账本与存活统计（见 TOGETHER_MP_SPEC §8 第 4/5/10 条）
        /// → 联机中整体让路（笔记 §8 的建议路线 L0：\"遥控功能默认关闭\"）。</summary>
        internal static bool RemoteBlocked(out string why)
        {
            why = null;
            if (!Util.V(ModConfig.CompatAutoLetGo, true)) return false;

            if (TogetherOnline)
            {
                why = Loc.T("联机中（Together 在线）：遥控下单在客机不成立、在主机还会污染对方账本，已自动让路");
                return true;
            }
            return false;
        }

        /// <summary>遥控被让路时打一次日志（点击/框选是每帧路径，这里做去重；HUD 状态行负责持续提示）。</summary>
        internal static void NoteRemoteRefused(string why)
        {
            if (_remoteWarned) return;
            _remoteWarned = true;
            Util.Warn(Loc.F("[NewMode][兼容] 遥控已让路：{0}", why));
        }

        /// <summary>
        /// 联机中**真的投放了一次**（说明让路被 cfg 关掉或强制开了）→ 提示一次"对方看不到你的单位"。
        /// 原因（Together 0.6.13 逆向，`AgentAuthority.SendBirth/CanPublishActor`）：敌人侧 actor 只有
        /// `RaidIdentity.IsCommitted(key.A) && HostRaidManifest.IsWaveReady(key.A)` 才会广播 `ActorSpawn`；
        /// 我们自建的 Wave 不在它的清单里 → 客机永远收不到出生消息（主机视角正常）。见 TOGETHER_MP_SPEC §8。
        /// </summary>
        internal static void NoteDropInSession()
        {
            if (_sessionDropWarned) return;
            _sessionDropWarned = true;

            string msg = Loc.T("注意：联机中投放的单位不会广播给对方（Together 只同步它自己清单里的波次）—— 对方看不到这些船与敌人，但伤害与结算仍会算到他头上");
            Util.Warn("[NewMode][兼容] " + msg);
            IngameMenu.Say(msg);
        }

        /// <summary>启动时一行环境自检：报障时这一行就能定位环境（见兼容方案 §3.4）。</summary>
        internal static void ReportEnvironment()
        {
            if (!Util.V(ModConfig.CompatReport, true)) return;
            Util.Log(Loc.F("[NewMode][兼容] 环境自检：整合包={0}（{1} 个插件），Together={2}（在线={3}），终岛无尽={4}，贤者时缓={5}，MMHOOK={6}，游戏本体={7}",
                PackPresent ? Loc.T("有") : Loc.T("无"), PackPluginCount(),
                TogetherLoaded ? Loc.T("有") : Loc.T("无"), TogetherOnline ? Loc.T("是") : Loc.T("否"),
                PluginLoaded(GuidEndlessFinale) ? Loc.T("有") : Loc.T("无"),
                PluginLoaded(GuidSageTimeFreeze) ? Loc.T("有") : Loc.T("无"),
                AssemblyLoaded("MMHOOK-Assembly-CSharp") ? Loc.T("有") : Loc.T("无"),
                PackPresent ? Loc.T("整合包改造版") : Loc.T("原版")));
        }

        // ===================== 投放后自检（整合包会把我们投的兵"增强"）=====================

        /// <summary>本船被整合包增强的成员数（无命中返回 null，零噪音；诊断用，见兼容方案 §3.3 C2）。</summary>
        internal static string AuditEnhancements(Longship ship)
        {
            if (!PackPresent || ship == null || ship.agents == null) return null;

            int total = 0;
            int hit = 0;
            string kinds = null;
            for (int i = 0; i < ship.agents.Count; i++)
            {
                Agent a = ship.agents[i];
                if (a == null) continue;
                total++;

                string kind = EnhancedAs(a);
                if (kind == null) continue;
                hit++;
                if (kinds == null) kinds = kind;
                else if (kinds.IndexOf(kind, StringComparison.Ordinal) < 0) kinds += "/" + kind;
            }
            if (hit == 0) return null;
            return Loc.F("[NewMode][兼容] 本船 {0} 名敌人里 {1} 名疑似被整合包增强（{2}）—— 属整合包设定，不是本模组行为",
                total, hit, kinds);
        }

        /// <summary>这个单位身上（含最多 4 层父级）是否挂着整合包的增强组件；返回命中的类型名。</summary>
        static string EnhancedAs(Agent a)
        {
            Transform t = a.transform;
            for (int depth = 0; depth < 4 && t != null; depth++)
            {
                Component[] comps = t.GetComponents<Component>();
                if (comps != null)
                {
                    for (int i = 0; i < comps.Length; i++)
                    {
                        if (comps[i] == null) continue;
                        string n = comps[i].GetType().Name;
                        for (int k = 0; k < EnhancementTypes.Length; k++)
                            if (n == EnhancementTypes[k]) return n;
                    }
                }
                t = t.parent;
            }
            return null;
        }
    }
}


