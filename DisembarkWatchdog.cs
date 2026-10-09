using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>下船看门狗（只盯本 mod 投放的船）：到岸宽限期后仍有人没下船 → 打一行诊断并用原版公开成员兜底下船（安全网）。</summary>
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
            if (_entries.Count == 0) return;

            bool fix = Util.V(ModConfig.DisembarkFix, false);
            float grace = Util.V(ModConfig.DisembarkGrace, 3f);

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

        /// <summary>一次性打全"为什么没下船"的关键状态。</summary>
        internal static void Diagnose(Landing landing, Longship ship)
        {
            Animator anim = ship.GetComponent<Animator>();
            Util.Warn(Loc.F(
                "[NewMode][下船] 到岸后仍未下船：interpolator={0:F3} landed={1} enabled={2} agents={3} haveAllSpawned={4} animator={5}",
                ship.interpolator, ship.landed, ship.enabled, ship.agents.Count, ship.haveAllSpawned,
                (anim == null) ? Loc.T("无") : (Loc.T("有/enabled=") + anim.enabled.ToString())));

            for (int i = 0; i < ship.agents.Count; i++)
            {
                Agent a = ship.agents[i];
                if (a == null) { Util.Warn(Loc.F("[NewMode][下船]   敌 {0}：已销毁", i)); continue; }

                Pirate pirate = a.GetComponent<Pirate>();
                Brain brain = a.brain;
                bool inActions = (brain != null) && (pirate != null) && brain.actions.Contains(pirate);
                bool isOrder = (brain != null) && (pirate != null) && object.ReferenceEquals(brain.order, pirate);
                Util.Warn(Loc.F(
                    "[NewMode][下船]   敌 {0}：navPos.island={1} orderDist={2:F3} spawned={3} pirate={4} brainActions={5}(含Pirate={6}) brainOrder={7}(是Pirate={8})",
                    i, (a.navPos.island != null), a.orderDist, (a.spawned != null && a.spawned.active),
                    (pirate != null), (brain != null) ? brain.actions.Count : -1, inActions,
                    (brain != null && brain.order != null) ? brain.order.GetType().Name : "null", isOrder));
            }
        }

        /// <summary>下船看门狗：到岸宽限期后仍没人下船 → 打诊断日志 + 用原版成员兜底下船（见 §6 T4）。</summary>
        internal static int ForceDisembark(Landing landing, Longship ship)
        {
            int moved = 0;
            LandingInjector.AttachAgentBehaviours(landing);      // 先补 order（与正常投放路径同一套逻辑）
            for (int i = ship.agents.Count - 1; i >= 0; i--)     // 倒序：RemoveFromShip 会改动 agents 列表
            {
                Agent a = ship.agents[i];
                if (a == null) continue;

                Pirate pirate = a.GetComponent<Pirate>();
                if (pirate == null)
                {
                    Util.Warn(Loc.T("[NewMode][下船]   该敌人没有 Pirate 组件，跳过（无法走原版下船逻辑）"));
                    continue;
                }

                NavPos islandPos = landing.navPos;
                Vector3 border = a.navPos.GetBorderVector();
                islandPos.wPos = a.navPos.transform.TransformPoint(a.navPos.pos - border.normalized * 0.3f);
                a.navPos = islandPos;                            // 与 Pirate.MaybeAct 完全一致
                pirate.PirateUpdate();                           // public：navPos.island 成立 → 原版 RemoveFromShip()
                moved++;
            }

            Util.Log(Loc.F("[NewMode][下船] 兜底下船：把 {0} 个敌人移下船（走原版 RemoveFromShip）", moved));
            return moved;
        }
    }
}
