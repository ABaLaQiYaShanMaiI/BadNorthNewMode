using System.Reflection;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>与 Together 联机（`Together.dll` 0.6.13 / 协议 14）对接的**只读探测层**（v1.7.7）。
    ///
    /// 为什么先只做"只读探测"：`TOGETHER_L2_FEASIBILITY.md` 的结论是 —— 真要让客机看到我们投放的船，
    /// 必须在主机侧把自建 Wave 登记进 Together 的**波次账本 + 清单**；而任何一处不符（序号不连续 / 签名不同 /
    /// 资源解析失败）都会让它 `IOException → AgentAuthority.Fail` **冻结整局**，比"看不见"严重得多。
    /// 因此本版本只做三件事，**绝不写任何 Together 状态**：
    ///   ① 存在性 + 版本 + 协议号的探测（版本一变，字段名可能漂移）；
    ///   ② 每次投放打印一份"本波在 Together 眼里是什么样"的证据行（id / 是否已提交 / 签名 / 预检）；
    ///   ③ 给后续正式登记（B2）提供判据与失败原因 —— 先在真人双端上跑数据，再决定是否开写入。
    ///
    /// 反射纪律（与 `Compat` / `VanillaSettings` 相同）：
    ///   · 用 `Compat.FindAssembly`（不用带程序集限定的 `Type.GetType` —— LoadFrom 上下文会返回 null）；
    ///   · 反射成员一律 `object.ReferenceEquals` 判空（游戏 mscorlib 2.0 没有 `MemberInfo.op_Equality`）；
    ///   · 任何一项取不到 → **整体降级**并只 Warn 一次，绝不影响单机玩法。</summary>
    internal static class TogetherBridge
    {
        static bool _probed;
        static bool _failed;
        static string _why;
        static Island _probedIsland;      // 会话探测按岛屿去重

        static Assembly _asm;
        static System.Type _session, _identity, _manifest, _authority, _protocol, _features;

        static PropertyInfo _isHost;
        static MethodInfo _getWaveId, _isCommitted, _waveSignature, _isWaveReady, _canPublishWave;
        static FieldInfo _initialCount, _expectedLogicalId, _ready, _hostEntries;
        static FieldInfo _authorityEpoch, _authorityFailure;
        // 探针修正：Together 里 Epoch / HasFailure 是**属性**而不是字段
        static PropertyInfo _authorityEpochProp, _authorityFailureProp;

        internal static bool Available
        {
            get
            {
                return !_failed && _probed &&
                       !object.ReferenceEquals(_identity, null) && !object.ReferenceEquals(_manifest, null);
            }
        }

        /// <summary>只读探测当前这一波；`[Compat] TogetherProbe` 打开时由 `FlotillaLauncher.Launch` 调用一次。
        /// 不修改任何 Together / 游戏状态。</summary>
        internal static void ProbeWave(Wave wave)
        {
            if (!Util.V(ModConfig.TogetherProbe, false)) return;
            if (wave == null || !Compat.TogetherOnline) return;

            if (!Probe())
            {
                Util.LogOnce("together:probe-fail", Loc.F("[NewMode][Together] 探测不可用：{0}（不影响单机）", _why));
                return;
            }

            try
            {
                Island island = (wave.raid != null) ? wave.raid.island : null;
                int initial = IntStatic(_initialCount);
                int expected = IntStatic(_expectedLogicalId);
                int id = InvokeInt(_getWaveId, wave);
                bool committed = InvokeBool(_isCommitted, id);
                bool ready = InvokeBool(_isWaveReady, id);
                bool publish = InvokeBool(_canPublishWave, wave);
                uint sig = InvokeUInt(_waveSignature, wave);

                int groups = (wave.shipGroups != null) ? wave.shipGroups.Count : 0;
                int landings = CountLandings(wave);
                string pre = Preflight(wave);

                Util.Log(Loc.F("[NewMode][Together] 探测 {0} / 协议 {1} / 主机={2} · 账本 initial={3} expected={4} ready={5} entries={6} · 本波 id={7} committed={8} ready={9} publish={10} sig=0x{11:X8} · groups={12} landings={13} · {14} · island={15} · epoch={16} fail={17}",
                    VersionText(), ProtocolText(), IsHostText(), initial, expected, CountOf(_ready), CountOf(_hostEntries),
                    id, committed, ready, publish, sig, groups, landings, pre,
                    (island != null && wave.raid == island.raid) ? "raid=ok" : "raid=mismatch",
                    IntStatic(_authorityEpoch), BoolStatic(_authorityFailure) ? "Y" : "N"));
            }
            catch (System.Exception e)
            {
                Util.LogOnce("together:probe-error:" + e.GetType().Name,
                    Loc.F("[NewMode][Together] 探测出错（{0}）：{1}", e.GetType().Name, e.Message));
            }
        }
        /// <summary>会话级探测（每座岛一行 + 每条原版波一行）：不投放也能确认"账本序号 / 原版波在本岛是否已提交"。
        /// 由 `Compat.Tick` 每帧调用（内部按岛屿去重）。只读。</summary>
        internal static void ProbeSession(IslandGameplayManager gm)
        {
            if (!Util.V(ModConfig.TogetherProbe, false)) return;
            if (!Compat.TogetherOnline) return;

            Island island = (gm != null) ? gm.island : null;
            if (island == null || !island.generated) return;
            if (object.ReferenceEquals(_probedIsland, island)) return;
            _probedIsland = island;

            if (!Probe())
            {
                Util.LogOnce("together:probe-fail", Loc.F("[NewMode][Together] 探测不可用：{0}（不影响单机）", _why));
                return;
            }

            try
            {
                int waves = (island.raid != null && island.raid.waves != null) ? island.raid.waves.Count : -1;
                Util.Log(Loc.F("[NewMode][Together] 会话探测 {0} / 协议 {1} / 主机={2} · 本岛 raid.waves={3} · 账本 initial={4} expected={5} ready={6} entries={7} · epoch={8} fail={9}",
                    VersionText(), ProtocolText(), IsHostText(), waves,
                    IntStatic(_initialCount), IntStatic(_expectedLogicalId), CountOf(_ready), CountOf(_hostEntries),
                    IntStatic(_authorityEpoch), BoolStatic(_authorityFailure) ? "Y" : "N"));

                if (island.raid == null || island.raid.waves == null) return;
                for (int i = 0; i < island.raid.waves.Count; i++)
                {
                    Wave w = island.raid.waves[i];
                    if (w == null) continue;
                    int id = InvokeInt(_getWaveId, w);
                    Util.Log(Loc.F("[NewMode][Together]   原版波 #{0}: id={1} committed={2} ready={3}",
                        i, id, InvokeBool(_isCommitted, id), InvokeBool(_isWaveReady, id)));
                }
            }
            catch (System.Exception e)
            {
                Util.LogOnce("together:probe-session-error:" + e.GetType().Name,
                    Loc.F("[NewMode][Together] 会话探测出错（{0}）：{1}", e.GetType().Name, e.Message));
            }
        }

        /// <summary>v1.8.0：联机投放放行判定（**只读**，不写任何 Together 状态）。
        /// 只有同时满足"装了同步版 Together（BadNorthTogether） + 本机是主机 + 战斗基线已建立（Epoch&gt;0）"才放行；
        /// 具体同步由 Together 侧 `HostRaidExternal` 在 `wave.BeginWave()` 处接管（登记逻辑编号 + 清单 + 开波）。</summary>
        internal static bool CanHostDispatch(out string why)
        {
            why = null;
            if (!Compat.TogetherOnline) return true;                     // 单机：与本模组无关

            if (!Probe())
            {
                why = Loc.F("联机同步不可用：{0}", _why);
                return false;
            }
            if (!ExternalWaveSync())
            {
                why = Loc.T("当前 Together 不是 BadNorthTogether（没有普通岛外部波通道）：联机投放会只有主机可见，已让路");
                return false;
            }
            if (ProtocolText() != "14")
            {
                why = Loc.F("Together 协议不是 14（当前 {0}）", ProtocolText());
                return false;
            }
            if (IsHostText() != "Y")
            {
                why = Loc.T("本机是客机（只有主机能投放）");
                return false;
            }
            if (EpochNow() <= 0)
            {
                why = Loc.T("本局没有建立联机战斗基线（进岛后才开房）：请回战役地图开房，再一起进岛");
                return false;
            }
            if (FailureNow())
            {
                why = Loc.T("Together 已判定本局失败（请重开一局）");
                return false;
            }
            return true;
        }

        /// <summary>v1.8.0：对面 Together 是否是带"普通岛外部波通道"的 BadNorthTogether。
        /// 读 `Together.BadNorthTogetherFeatures.ExternalWaves`（const bool）；原版 Together 没有这个类型 → false。</summary>
        static bool ExternalWaveSync()
        {
            try
            {
                if (object.ReferenceEquals(_features, null)) return false;
                FieldInfo f = _features.GetField("ExternalWaves", BindingFlags.Public | BindingFlags.Static);
                if (object.ReferenceEquals(f, null)) return false;
                object v = f.GetRawConstantValue();
                return v is bool && (bool)v;
            }
            catch (System.Exception) { return false; }
        }

        static int EpochNow()
        {
            try
            {
                if (!object.ReferenceEquals(_authorityEpochProp, null))
                    return (int)_authorityEpochProp.GetValue(null, null);
            }
            catch (System.Exception) { }
            return IntStatic(_authorityEpoch);
        }

        static bool FailureNow()
        {
            try
            {
                if (!object.ReferenceEquals(_authorityFailureProp, null))
                {
                    object v = _authorityFailureProp.GetValue(null, null);
                    return v is bool && (bool)v;
                }
            }
            catch (System.Exception) { }
            return BoolStatic(_authorityFailure);
        }

        /// <summary>预检（正式登记前的硬条件，逐项只读）：landing 已放置 / navPos 有效 / 资源名可解析 / 数量不超限。</summary>
        static string Preflight(Wave wave)
        {
            if (wave.raid == null) return "预检：波未绑定 Raid → 不可登记";

            bool placed = true, navOk = true, resOk = true;
            bool inWaves = (wave.raid.waves != null) && wave.raid.waves.Contains(wave);
            int loads = 0, agents = 0;

            if (wave.shipGroups != null)
            {
                for (int g = 0; g < wave.shipGroups.Count; g++)
                {
                    ShipGroup sg = wave.shipGroups[g];
                    if (sg == null || sg.landings == null) continue;

                    for (int l = 0; l < sg.landings.Count; l++)
                    {
                        Landing ld = sg.landings[l];
                        if (ld == null) continue;
                        if (!ld.placed) placed = false;
                        if (ld.navPos.isNull) navOk = false;
                        if (ld.shipPrefab == null || !Resolvable(ld.shipPrefab.name)) resOk = false;

                        if (ld.shipLoads == null) continue;
                        for (int k = 0; k < ld.shipLoads.Count; k++)
                        {
                            ShipLoad load = ld.shipLoads[k];
                            if (load == null) continue;
                            loads++;
                            agents += load.count;
                            if (load.vikingRef == null || !Resolvable(load.vikingRef.name)) resOk = false;
                        }
                    }
                }
            }

            bool limits = wave.shipGroups != null && wave.shipGroups.Count <= 64 && loads <= 16 * 64 && agents <= 8192;
            return string.Format("pre placed={0} navPos={1} res={2} limits={3} inWaves={4}",
                placed ? "Y" : "N", navOk ? "Y" : "N", resOk ? "Y" : "N", limits ? "Y" : "N", inWaves ? "Y" : "N");
        }

        /// <summary>名字能否被客机侧解析（Together `ResolveResources` 用同一个引用字典）。</summary>
        static bool Resolvable(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                System.Collections.Generic.Dictionary<string, UnityEngine.Object> dict = LevelStateObjectReferences.dict;
                return dict != null && dict.ContainsKey(name);
            }
            catch { return false; }
        }

        static int CountLandings(Wave wave)
        {
            int n = 0;
            if (wave.shipGroups == null) return 0;
            for (int g = 0; g < wave.shipGroups.Count; g++)
            {
                ShipGroup sg = wave.shipGroups[g];
                if (sg != null && sg.landings != null) n += sg.landings.Count;
            }
            return n;
        }

        // ===================== 反射探测 =====================

        static bool Probe()
        {
            if (_probed) return !_failed;
            _probed = true;

            try
            {
                _asm = Compat.FindAssembly("Together");
                if (object.ReferenceEquals(_asm, null)) { Fail("找不到 Together.dll"); return false; }

                _session = TypeOf("Together.Session");
                _identity = TypeOf("Together.RaidIdentity");
                _manifest = TypeOf("Together.HostRaidManifest");
                _authority = TypeOf("Together.AgentAuthority");
                _protocol = TypeOf("Together.Net.Protocol");
                _features = TypeOf("Together.BadNorthTogetherFeatures");
                if (object.ReferenceEquals(_identity, null) || object.ReferenceEquals(_manifest, null))
                { Fail("缺少 RaidIdentity / HostRaidManifest"); return false; }

                if (!object.ReferenceEquals(_session, null))
                {
                    _isHost = Prop(_session, "IsHost");
                }

                _getWaveId = Method(_identity, "GetWaveId", 1);
                _isCommitted = Method(_identity, "IsCommitted", 1);
                _waveSignature = Method(_identity, "WaveSignature", 1);

                _isWaveReady = Method(_manifest, "IsWaveReady", 1);
                _canPublishWave = Method(_manifest, "CanPublishWave", 1);
                _initialCount = Field(_manifest, "initialCount");
                _expectedLogicalId = Field(_manifest, "expectedLogicalId");
                _ready = Field(_manifest, "ready");
                _hostEntries = Field(_manifest, "hostEntries");

                if (object.ReferenceEquals(_getWaveId, null) || object.ReferenceEquals(_isCommitted, null) ||
                    object.ReferenceEquals(_waveSignature, null) || object.ReferenceEquals(_isWaveReady, null) ||
                    object.ReferenceEquals(_initialCount, null) || object.ReferenceEquals(_expectedLogicalId, null))
                { Fail("关键成员缺失（GetWaveId / IsCommitted / WaveSignature / IsWaveReady / 账本字段）"); return false; }

                if (!object.ReferenceEquals(_authority, null))
                {
                    _authorityEpoch = Field(_authority, "Epoch");
                    _authorityFailure = Field(_authority, "HasFailure");
                    _authorityEpochProp = Prop(_authority, "Epoch");
                    _authorityFailureProp = Prop(_authority, "HasFailure");
                }
                return true;
            }
            catch (System.Exception e)
            {
                Fail(e.GetType().Name + ": " + e.Message);
                return false;
            }
        }
        static void Fail(string why)
        {
            _failed = true;
            _why = why;
            Util.Warn(Loc.F("[NewMode][Together] 探测层不可用（{0}）→ 只按\"联机让路\"处理，不做任何登记", why));
        }

        static System.Type TypeOf(string fullName)
        {
            try { return _asm.GetType(fullName, false); }
            catch (System.Exception) { return null; }
        }

        static PropertyInfo Prop(System.Type t, string name)
        {
            try { return t.GetProperty(name, BindingFlags.Public | BindingFlags.Static); }
            catch (System.Exception) { return null; }
        }

        static MethodInfo Method(System.Type t, string name, int argc)
        {
            try
            {
                MethodInfo[] all = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < all.Length; i++)
                    if (string.Equals(all[i].Name, name, System.StringComparison.Ordinal) && all[i].GetParameters().Length == argc)
                        return all[i];
            }
            catch (System.Exception) { }
            return null;
        }

        static FieldInfo Field(System.Type t, string name)
        {
            try { return t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
            catch (System.Exception) { return null; }
        }

        // ===================== 只读调用 =====================

        static int IntStatic(FieldInfo f)
        {
            if (object.ReferenceEquals(f, null)) return -1;
            object v = f.GetValue(null);
            return (v is int) ? (int)v : -1;
        }

        static bool BoolStatic(FieldInfo f)
        {
            if (object.ReferenceEquals(f, null)) return false;
            object v = f.GetValue(null);
            return v is bool && (bool)v;
        }

        static string VersionText()
        {
            try { return "v" + _asm.GetName().Version; }
            catch (System.Exception) { return "v?"; }
        }

        static string ProtocolText()
        {
            try
            {
                if (object.ReferenceEquals(_protocol, null)) return "?";
                FieldInfo f = Field(_protocol, "Version");
                if (object.ReferenceEquals(f, null)) return "?";
                return f.GetRawConstantValue().ToString();
            }
            catch (System.Exception) { return "?"; }
        }

        static string IsHostText()
        {
            try
            {
                if (object.ReferenceEquals(_isHost, null)) return "?";
                return ((bool)_isHost.GetValue(null, null)) ? "Y" : "N";
            }
            catch (System.Exception) { return "?"; }
        }

        static int CountOf(FieldInfo f)
        {
            if (object.ReferenceEquals(f, null)) return -1;
            try
            {
                object coll = f.GetValue(null);
                if (coll == null) return -1;
                PropertyInfo p = coll.GetType().GetProperty("Count");
                if (object.ReferenceEquals(p, null)) return -1;
                return (int)p.GetValue(coll, null);
            }
            catch (System.Exception) { return -1; }
        }

        static int InvokeInt(MethodInfo m, object arg)
        {
            object v = m.Invoke(null, new object[] { arg });
            return (v is int) ? (int)v : -1;
        }

        static bool InvokeBool(MethodInfo m, object arg)
        {
            object v = m.Invoke(null, new object[] { arg });
            return v is bool && (bool)v;
        }

        static uint InvokeUInt(MethodInfo m, object arg)
        {
            object v = m.Invoke(null, new object[] { arg });
            return (v is uint) ? (uint)v : 0u;
        }
    }
}
