using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>
    /// 下船看门狗：只针对本 mod 投放的船。
    /// 原版下船链是「船动画事件置 Longship.landed=true → Pirate.MaybeAct（要求 landed 且 orderDist&lt;0.01）
    /// → 把 agent.navPos 换成岛屿导航网格 → RemoveFromShip()」；其中 Brain.Setup 收集 IBrainAction/IAgentOrder
    /// 发生在 CreateAgent（同步）之时，而 Pirate 是那之后才挂上的，晚投的船可能因此卡住。
    /// 本看门狗：船到岸后若若干秒仍有人没下船 → 打一行完整诊断，并用**原版公开成员**把这批人踢下船。
    /// </summary>
    internal sealed class DisembarkWatchdog : MonoBehaviour
    {
        sealed class Entry
        {
            internal Landing landing;
            internal Longship ship;
            internal float arrivedAt = -1f;   // 到岸时刻（-1 = 还没到）
            internal bool diagnosed;
            internal bool fixedUp;
        }

        static DisembarkWatchdog _instance;
        readonly List<Entry> _entries = new List<Entry>();

        internal static DisembarkWatchdog Get()
        {
            if (_instance != null) return _instance;
            GameObject go = new GameObject("BadNorthNewMode.Watchdog");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<DisembarkWatchdog>();
            return _instance;
        }

        /// <summary>登记一艘本 mod 投放的船。</summary>
        internal void Watch(Landing landing)
        {
            if (landing == null) return;
            Entry e = new Entry();
            e.landing = landing;
            e.ship = landing.spawnedShip;
            _entries.Add(e);
        }

        void Update()
        {
            if (_entries.Count == 0 || Plugin.Log == null) return;

            bool fix = (ModConfig.DisembarkFix != null) && ModConfig.DisembarkFix.Value;
            float grace = (ModConfig.DisembarkGrace != null) ? ModConfig.DisembarkGrace.Value : 3f;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry e = _entries[i];
                Longship ship = e.ship;
                if (ship == null && e.landing != null) ship = e.landing.spawnedShip;
                if (ship == null) { _entries.RemoveAt(i); continue; }                                  // 船被销毁（关卡清场等）
                if (ship.agents == null || ship.agents.Count == 0) { _entries.RemoveAt(i); continue; }  // 已全部下船 = 正常完成

                bool arrived = ship.landed || ship.interpolator >= 0.999f;
                if (!arrived) continue;
                if (e.arrivedAt < 0f) e.arrivedAt = Time.time;
                if (Time.time - e.arrivedAt < grace) continue;

                if (!e.diagnosed) { e.diagnosed = true; Diagnose(e.landing, ship); }
                if (!fix || e.fixedUp) continue;

                e.fixedUp = true;
                ForceDisembark(e.landing, ship);
            }
        }

        /// <summary>把"为什么没下船"一次性打全（interpolator / landed / 动画器 / brain 是否收下了 Pirate）。</summary>
        internal static void Diagnose(Landing landing, Longship ship)
        {
            Animator anim = ship.GetComponent<Animator>();
            Plugin.Log.LogWarning(string.Format(
                "[NewMode][下船] 到岸后仍未下船：interpolator={0:F3} landed={1} enabled={2} agents={3} haveAllSpawned={4} animator={5}",
                ship.interpolator, ship.landed, ship.enabled, ship.agents.Count, ship.haveAllSpawned,
                (anim == null) ? "无" : ("有/enabled=" + anim.enabled.ToString())));

            for (int i = 0; i < ship.agents.Count; i++)
            {
                Agent a = ship.agents[i];
                if (a == null) { Plugin.Log.LogWarning("[NewMode][下船]   敌 " + i + "：已销毁"); continue; }

                Pirate pirate = a.GetComponent<Pirate>();
                Brain brain = a.brain;
                bool inActions = (brain != null) && (pirate != null) && brain.actions.Contains(pirate);
                bool isOrder = (brain != null) && (pirate != null) && object.ReferenceEquals(brain.order, pirate);
                Plugin.Log.LogWarning(string.Format(
                    "[NewMode][下船]   敌 {0}：navPos.island={1} orderDist={2:F3} spawned={3} pirate={4} brainActions={5}(含Pirate={6}) brainOrder={7}(是Pirate={8})",
                    i, (a.navPos.island != null), a.orderDist, (a.spawned != null && a.spawned.active),
                    (pirate != null), (brain != null) ? brain.actions.Count : -1, inActions,
                    (brain != null && brain.order != null) ? brain.order.GetType().Name : "null", isOrder));
            }
        }

        /// <summary>
        /// 用原版公开成员完成下船：照抄 Pirate.MaybeAct 的后半段
        /// （把 navPos 换成 landing.navPos 这套岛屿网格坐标 → PirateUpdate() 内部检测 navPos.island 成立 → RemoveFromShip()）。
        /// 同时补登记 brain.actions / brain.order，等价于"Brain.Setup 当时就已经有 Pirate"。
        /// </summary>
        internal static int ForceDisembark(Landing landing, Longship ship)
        {
            int moved = 0;
            LandingInjector.AttachPirateOrder(ship);             // 先补 order（与正常投放路径同一套逻辑）
            for (int i = ship.agents.Count - 1; i >= 0; i--)     // 倒序：RemoveFromShip 会改动 agents 列表
            {
                Agent a = ship.agents[i];
                if (a == null) continue;

                Pirate pirate = a.GetComponent<Pirate>();
                if (pirate == null)
                {
                    Plugin.Log.LogWarning("[NewMode][下船]   该敌人没有 Pirate 组件，跳过（无法走原版下船逻辑）");
                    continue;
                }

                NavPos islandPos = landing.navPos;
                Vector3 border = a.navPos.GetBorderVector();
                islandPos.wPos = a.navPos.transform.TransformPoint(a.navPos.pos - border.normalized * 0.3f);
                a.navPos = islandPos;                            // 与 Pirate.MaybeAct 完全一致
                pirate.PirateUpdate();                           // public：navPos.island 成立 → 原版 RemoveFromShip()
                moved++;
            }

            Plugin.Log.LogInfo(string.Format("[NewMode][下船] 兜底下船：把 {0} 个敌人移下船（走原版 RemoveFromShip）", moved));
            return moved;
        }
    }
}
