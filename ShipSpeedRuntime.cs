using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>
    /// 局内"靠岸速度"4 档（v1.7.1）：1 / 1.25 / 1.5 / 2.0 —— **原生波次的船与本模组投放的船都生效**。
    ///
    /// 原理（见 PROJECT_SPEC §10）：原版 `Longship.UpdateIncoming` / `get_timeRemaining` 每帧读
    /// `landing.shipTravel.duration`，`UpdateOutGoing` 读 `fleeDuration`
    /// ⇒ 只改这两个字段就能"在航的船立刻变速、不改变航线、不跳位"。
    ///
    /// 四条铁律（v1.7.6 加固）：
    ///   ① **只写每个 Landing 自己那份 `ShipTravel`**，绝不碰共享的 `shipPrefab`（否则会改所有船）；
    ///   ② 每条船记一份**未经缩放的基准用时**，目标值恒为 `基准 / 档位`；**绝不再用"上次档位"做比值累乘** ——
    ///      比值路线一旦漏记一次就会叠乘（×2 写两次 = 用时 ÷4、÷8…），实机表现正是
    ///      "开着二倍速退出岛屿再重进，船越来越快"（v1.7.6 修，见 PROJECT_SPEC §11.13）；
    ///   ③ 档位 = ×1（默认）时**完全不读不写** —— 玩家没动过这条滑条，本模组对船速零影响；
    ///   ④ 反射拿不到 / 写入被拒（`duration` 是 `initonly`）→ **整体禁用并 Warn 一次**（绝不静默错、绝不每帧重试）。
    ///
    /// 诊断：每座岛开局打一行"本关按 ×N 缩放靠岸用时（含原版波次）"—— 玩家报"船靠岸太快"时，
    /// 有这一行就能一眼确认是不是本模组的档位在起作用。
    ///
    /// 门控：联机**客机**侧本地改动无意义（船由主机权威）→ 由菜单置灰（`Compat.SessionClient`）。
    /// </summary>
    internal static class ShipSpeedRuntime
    {
        /// <summary>4 个档位（作者口径）：1 = 初始速度。</summary>
        internal static readonly float[] Gears = new float[] { 1f, 1.25f, 1.5f, 2f };

        const float MinDuration = 0.05f;      // 极端档位下也不给游戏喂 0/负值
        const float TickInterval = 1f;        // 周期扫描（新出现的船）节流；换档时是**立即**套用
        const float Epsilon = 0.0001f;
        const float RewriteTolerance = 0.01f; // 与"我们应该写出的值"差超过它 → 说明游戏自己重写了 shipTravel

        /// <summary>一条船的记账：基准用时 = **原版档位下的用时**（已含该船 `TryPlace` 时的随机档位与难度倍率）。</summary>
        sealed class Entry
        {
            internal float baseDur = -1f;
            internal float baseFlee = -1f;
            internal float gear = -1f;        // 我们上次写进去的档位（-1 = 还没写过）
        }

        // ⚠️ v1.7.6：登记表**不再在"离开战局 / 换岛"时清空** —— 退出岛屿再进来时 Landing 与它们被改过的
        //    `shipTravel` 都还在，清空就会把它们当成"原版速度的新船"再缩一次（叠乘 = 船越来越快）。
        //    只靠 `Prune()` 摘掉已销毁的 Landing；换到别的岛时旧条目会随对象销毁被摘掉。
        static readonly Dictionary<Landing, Entry> _entries = new Dictionary<Landing, Entry>();
        static FieldInfo _travelField;        // Landing.shipTravel（值类型字段 → 装箱改再整块写回）
        static FieldInfo _durationField;      // ShipTravel.duration
        static FieldInfo _fleeField;          // ShipTravel.fleeDuration
        static bool _probed;
        static bool _disabled;
        static float _nextTick;
        static Island _announcedIsland;       // 本关的"档位诊断行"只打一次

        /// <summary>当前档位下标 0..3（cfg 值吸附到最近的档位）。</summary>
        internal static int GearIndex
        {
            get
            {
                float g = Util.V(ModConfig.ShipSpeedGear, 1f);
                int best = 0;
                float diff = Mathf.Abs(Gears[0] - g);
                for (int i = 1; i < Gears.Length; i++)
                {
                    float d = Mathf.Abs(Gears[i] - g);
                    if (d < diff) { diff = d; best = i; }
                }
                return best;
            }
        }

        internal static float Gear { get { return Gears[GearIndex]; } }

        /// <summary>反射不可用（整体禁用）时为 true —— 菜单据此提示，而不是假装能调。</summary>
        internal static bool Disabled { get { return _disabled; } }

        internal static string Describe()
        {
            return Loc.F("靠岸速度 ×{0}（第 {1}/{2} 档）", Gear, GearIndex + 1, Gears.Length);
        }

        /// <summary>换档：写 cfg（BepInEx 会落盘）+ 立即套用到本关所有船。</summary>
        internal static void SetGearIndex(int index, Island island)
        {
            if (index < 0) index = 0;
            if (index >= Gears.Length) index = Gears.Length - 1;

            if (ModConfig.ShipSpeedGear != null && Mathf.Abs(ModConfig.ShipSpeedGear.Value - Gears[index]) > 0.0001f)
                ModConfig.ShipSpeedGear.Value = Gears[index];

            _nextTick = 0f;                                  // 下一次 Tick 立刻重扫
            ApplyAll(island, true);
        }

        internal static void Tick(IslandGameplayManager gm)
        {
            if (_disabled) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickInterval;

            Island island = (gm != null) ? gm.island : null;
            if (island == null || !island.generated || island.state != Island.State.Playing)
            {
                // v1.7.6：**不再 `_entries.Clear()`**（见字段注释）。这里只摘掉已销毁的船，并把"本关诊断行"复位。
                Prune();
                _announcedIsland = null;
                return;
            }

            ApplyAll(island, false);
        }

        /// <summary>v1.7.5：**总开关关掉时**把在航 / 在场的船还原成原版速度（**不动 cfg 里的档位**）。
        /// v1.7.6：改为直接写回每条船的**基准用时**（不再靠比值抵消），并**保留登记表**（记录里 gear 变 1）——
        /// 这样重新启用时按 cfg 档位再套一次即可，绝不会把"已经还原过的值"再缩一遍。</summary>
        internal static void RestoreVanilla(IslandGameplayManager gm)
        {
            if (_entries.Count == 0) return;                              // 没改过任何船（或已经还原过）→ 什么都不做

            Island island = (gm != null) ? gm.island : null;
            // 不在战局里（战役地图上关掉开关）：**保留登记表**，等回到战局再还原；否则退出岛屿再进来会重复缩放。
            if (island == null || island.raid == null || island.raid.landingContainer == null) return;
            if (!Probe()) return;

            int restored = 0;
            Wave[] waves = island.raid.landingContainer.GetComponentsInChildren<Wave>(true);
            if (waves != null)
            {
                for (int w = 0; w < waves.Length; w++)
                {
                    Wave wave = waves[w];
                    if (wave == null || wave.shipGroups == null) continue;

                    for (int g = 0; g < wave.shipGroups.Count; g++)
                    {
                        ShipGroup sg = wave.shipGroups[g];
                        if (sg == null || sg.landings == null) continue;

                        for (int l = 0; l < sg.landings.Count; l++)
                            if (Adjust(sg.landings[l], 1f)) restored++;
                    }
                }
            }

            Prune();
            if (restored > 0)
                Util.Log(Loc.F("[NewMode][船速] 总开关关闭 → 已把 {0} 艘船的靠岸速度还原成原版", restored));
        }

        static void ApplyAll(Island island, bool announce)
        {
            if (_disabled) return;
            if (island == null || island.raid == null || island.raid.landingContainer == null) return;
            if (!Probe()) return;

            float gear = Gear;
            int changed = 0;

            // 原生波次的船与我们投放的船**都挂在 `raid.landingContainer` 下** ⇒ 一次遍历全覆盖。
            Wave[] waves = island.raid.landingContainer.GetComponentsInChildren<Wave>(true);
            if (waves != null)
            {
                for (int w = 0; w < waves.Length; w++)
                {
                    Wave wave = waves[w];
                    if (wave == null || wave.shipGroups == null) continue;

                    for (int g = 0; g < wave.shipGroups.Count; g++)
                    {
                        ShipGroup sg = wave.shipGroups[g];
                        if (sg == null || sg.landings == null) continue;

                        for (int l = 0; l < sg.landings.Count; l++)
                            if (Adjust(sg.landings[l], gear)) changed++;
                    }
                }
            }

            Prune();

            // v1.7.6：每座岛只打一行"本关档位"（玩家报"船靠岸太快"时的第一手证据：档位是不是被滑条改过）
            if (!object.ReferenceEquals(_announcedIsland, island) && Mathf.Abs(gear - 1f) > Epsilon)
            {
                _announcedIsland = island;
                Util.Log(Loc.F("[NewMode][船速] 本关按 ×{0} 缩放靠岸用时（含原版波次；档位来自 F1 菜单 / cfg）", gear));
            }

            if (announce)
                Util.Log(Loc.F("[NewMode][船速] {0} · 已套用 {1} 艘船（原生 + 非原生）", Describe(), changed));
        }

        /// <summary>把这条 Landing 套用到当前档位；返回是否真的改了值。
        /// v1.7.6 起**目标值恒为 `基准 / 档位`**（不再拿"上次档位"做比值），所以同一艘船无论被调用多少次
        /// 都只会得到同一个结果 —— 这是"×2 退出岛屿再重进 → 船速叠乘"的根治办法（见 PROJECT_SPEC §11.13）。</summary>
        static bool Adjust(Landing landing, float gear)
        {
            if (landing == null) return false;

            Entry e;
            if (!_entries.TryGetValue(landing, out e))
            {
                e = new Entry();
                _entries[landing] = e;
            }

            float seen = (e.gear > 0f) ? e.gear : 1f;                     // 新船：出生时就是"原版速度"
            if (Mathf.Abs(seen - gear) < Epsilon)
            {
                // 档位没变 = 已经是目标值 → 不写。唯一例外：档位不是原版时回头确认一次，
                // 因为原版可能重新 `TryPlace` 把这条船的 shipTravel 写回原版速度（那就需要再套一次）。
                if (seen < 1f + Epsilon) return false;
                if (MatchesApplied(landing, e)) return false;
            }

            object box;
            try { box = _travelField.GetValue(landing); }
            catch (System.Exception ex) { Disable(ex, null); return false; }
            if (box == null || !_travelField.FieldType.IsValueType) return false;

            float dur, flee;
            try
            {
                dur = (float)_durationField.GetValue(box);
                flee = (float)_fleeField.GetValue(box);
            }
            catch (System.Exception ex) { Disable(ex, null); return false; }

            if (dur <= 0.0001f && flee <= 0.0001f) return false;          // 还没 TryPlace / 未放置

            CaptureBase(ref e.baseDur, dur, e.gear);                      // 只在"游戏自己写了新值"时刷新基准
            CaptureBase(ref e.baseFlee, flee, e.gear);

            float wantDur = (e.baseDur > 0f) ? Scale(e.baseDur, gear) : 0f;
            float wantFlee = (e.baseFlee > 0f) ? Scale(e.baseFlee, gear) : 0f;

            try
            {
                if (wantDur > 0f) _durationField.SetValue(box, wantDur);
                if (wantFlee > 0f) _fleeField.SetValue(box, wantFlee);
                _travelField.SetValue(landing, box);                      // 值类型字段：整块写回
            }
            catch (System.Exception ex) { Disable(ex, null); return false; }

            // 读回校验：`duration` 是 initonly，若运行时忽略了这次写入，必须当"不可用"，而不是静默无效。
            float expectDur = (wantDur > 0f) ? wantDur : dur;
            try
            {
                object back = _travelField.GetValue(landing);
                if (back == null || Mathf.Abs((float)_durationField.GetValue(back) - expectDur) > 0.002f)
                {
                    Disable(null, Loc.T("字段只读（写入被忽略）"));
                    return false;
                }
            }
            catch (System.Exception ex) { Disable(ex, null); return false; }

            e.gear = gear;
            return true;
        }

        /// <summary>目标用时 = 基准 / 档位（基准里已含每船随机档位与难度倍率 → 只缩放、不丢抖动）。</summary>
        static float Scale(float baseValue, float gear)
        {
            if (baseValue <= 0f) return baseValue;
            if (gear <= 0.0001f) gear = 1f;
            return Mathf.Max(MinDuration, baseValue / gear);
        }

        /// <summary>捕捉 / 刷新基准用时：只有当"当前值不等于我们按旧基准写出的值"时才认为游戏重写了它。</summary>
        static void CaptureBase(ref float baseValue, float current, float appliedGear)
        {
            if (current <= 0.0001f) return;
            if (baseValue <= 0f) { baseValue = current; return; }
            if (appliedGear > 0f && Mathf.Abs(current - Scale(baseValue, appliedGear)) > RewriteTolerance)
                baseValue = current;
        }

        /// <summary>当前 `shipTravel` 是否还是我们写进去的那份（档位没变时用来识别"原版重新 TryPlace 过"）。
        /// 读失败一律当"已经是目标值"，不去写它（写也多半会失败，下一轮 `Disable` 会兜底）。</summary>
        static bool MatchesApplied(Landing landing, Entry e)
        {
            if (e.baseDur <= 0f && e.baseFlee <= 0f) return false;

            object box;
            try { box = _travelField.GetValue(landing); }
            catch (System.Exception) { return true; }
            if (box == null || !_travelField.FieldType.IsValueType) return true;

            float dur, flee;
            try
            {
                dur = (float)_durationField.GetValue(box);
                flee = (float)_fleeField.GetValue(box);
            }
            catch (System.Exception) { return true; }

            if (e.baseDur > 0f && Mathf.Abs(dur - Scale(e.baseDur, e.gear)) > RewriteTolerance) return false;
            if (e.baseFlee > 0f && Mathf.Abs(flee - Scale(e.baseFlee, e.gear)) > RewriteTolerance) return false;
            return true;
        }

        /// <summary>v1.7.6：只摘掉已销毁的船（换岛 / 回地图时由 `SessionCleanup` 调）。
        /// ⚠️ **绝不清 `baseDur/baseFlee`** —— 退出岛屿再进来还要靠它们，清了就是 §11.13A 的"×2 叠乘"。</summary>
        internal static void PruneDead() { Prune(); }

        static void Prune()
        {
            if (_entries.Count == 0) return;
            List<Landing> dead = null;
            foreach (KeyValuePair<Landing, Entry> kv in _entries)
            {
                if (kv.Key != null) continue;                             // Unity 的 == 能识别已销毁对象
                if (dead == null) dead = new List<Landing>();
                dead.Add(kv.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) _entries.Remove(dead[i]);
        }

        static bool Probe()
        {
            if (_probed) return true;
            try
            {
                _travelField = typeof(Landing).GetField("shipTravel", BindingFlags.Public | BindingFlags.Instance);
                // ⚠️ 反射成员一律用 ReferenceEquals 判空：`FieldInfo == null` 会编译成 `MemberInfo.op_Equality`，
                //    那是 .NET 4.0 才有的成员，游戏 mscorlib 2.0 里不存在 → 运行期 MissingMethodException
                //    （tools/check-api.ps1 的闸门就是这么抓到的）。
                if (object.ReferenceEquals(_travelField, null)) { Disable(null, null); return false; }

                System.Type travel = _travelField.FieldType;
                _durationField = travel.GetField("duration", BindingFlags.Public | BindingFlags.Instance);
                _fleeField = travel.GetField("fleeDuration", BindingFlags.Public | BindingFlags.Instance);
                if (object.ReferenceEquals(_durationField, null) || object.ReferenceEquals(_fleeField, null))
                { Disable(null, null); return false; }
            }
            catch (System.Exception e) { Disable(e, null); return false; }

            _probed = true;
            return true;
        }

        static void Disable(System.Exception e, string extra)
        {
            if (_disabled) return;
            _disabled = true;

            string why = !string.IsNullOrEmpty(extra) ? extra
                       : (e != null) ? e.GetType().Name
                       : Loc.T("字段缺失");
            Util.Warn(Loc.F("[NewMode][船速] 反射写入不可用（{0}）→ 靠岸速度调节已禁用（只 Warn 一次）", why));
        }
    }
}
