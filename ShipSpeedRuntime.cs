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
    /// 三条铁律：
    ///   ① **只写每个 Landing 自己那份 `ShipTravel`**，绝不碰共享的 `shipPrefab`（否则会改所有船）；
    ///   ② 缩放用**比值** `duration' = duration × 旧档 / 新档` ⇒ 每条船在 `TryPlace` 里的随机档位自动约掉，抖动保留；
    ///   ③ 反射拿不到 / 写入被拒（`duration` 是 `initonly`）→ **整体禁用并 Warn 一次**（绝不静默错、绝不每帧重试）。
    ///
    /// 门控：联机**客机**侧本地改动无意义（船由主机权威）→ 由菜单置灰（`Compat.SessionClient`）。
    /// </summary>
    internal static class ShipSpeedRuntime
    {
        /// <summary>4 个档位（作者口径）：1 = 初始速度。</summary>
        internal static readonly float[] Gears = new float[] { 1f, 1.25f, 1.5f, 2f };

        const float MinDuration = 0.05f;     // 极端档位下也不给游戏喂 0/负值
        const float TickInterval = 1f;       // 周期扫描（新出现的船）节流；换档时是**立即**套用

        static readonly Dictionary<Landing, float> _applied = new Dictionary<Landing, float>();
        static FieldInfo _travelField;       // Landing.shipTravel（值类型字段 → 装箱改再整块写回）
        static FieldInfo _durationField;     // ShipTravel.duration
        static FieldInfo _fleeField;         // ShipTravel.fleeDuration
        static bool _probed;
        static bool _disabled;
        static float _nextTick;

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
                _applied.Clear();                            // 离开战局 / 换岛：登记表作废
                return;
            }

            ApplyAll(island, false);
        }

        internal static void Reset() { _applied.Clear(); _nextTick = 0f; }

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
            if (announce)
                Util.Log(Loc.F("[NewMode][船速] {0} · 已套用 {1} 艘船（原生 + 非原生）", Describe(), changed));
        }

        /// <summary>把这条 Landing 从"上次套用的档"缩放到当前档；返回是否真的改了值。</summary>
        static bool Adjust(Landing landing, float gear)
        {
            if (landing == null) return false;

            float seen;
            if (!_applied.TryGetValue(landing, out seen)) seen = 1f;      // 新船：出生时就是"初始速度"
            if (Mathf.Abs(seen - gear) < 0.0001f) return false;           // 已经是当前档 → 不碰

            object box;
            try { box = _travelField.GetValue(landing); }
            catch (System.Exception e) { Disable(e, null); return false; }
            if (box == null || !_travelField.FieldType.IsValueType) return false;

            float dur, flee;
            try
            {
                dur = (float)_durationField.GetValue(box);
                flee = (float)_fleeField.GetValue(box);
            }
            catch (System.Exception e) { Disable(e, null); return false; }

            if (dur <= 0.0001f && flee <= 0.0001f) return false;          // 还没 TryPlace / 未放置

            float k = seen / gear;                                        // 旧档 / 新档（比值 → 保留每船随机档位）
            float newDur = dur;
            try
            {
                if (dur > 0.0001f) { newDur = Mathf.Max(MinDuration, dur * k); _durationField.SetValue(box, newDur); }
                if (flee > 0.0001f) _fleeField.SetValue(box, Mathf.Max(MinDuration, flee * k));
                _travelField.SetValue(landing, box);                      // 值类型字段：整块写回
            }
            catch (System.Exception e) { Disable(e, null); return false; }

            // 读回校验：`duration` 是 initonly，若运行时忽略了这次写入，必须当"不可用"，而不是静默无效。
            try
            {
                object back = _travelField.GetValue(landing);
                if (back == null || Mathf.Abs((float)_durationField.GetValue(back) - newDur) > 0.002f)
                {
                    Disable(null, Loc.T("字段只读（写入被忽略）"));
                    return false;
                }
            }
            catch (System.Exception e) { Disable(e, null); return false; }

            _applied[landing] = gear;
            return true;
        }

        static void Prune()
        {
            if (_applied.Count == 0) return;

            List<Landing> dead = null;
            foreach (KeyValuePair<Landing, float> kv in _applied)
            {
                if (kv.Key != null) continue;                             // Unity 的 == 能识别已销毁对象
                if (dead == null) dead = new List<Landing>();
                dead.Add(kv.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) _applied.Remove(dead[i]);
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
