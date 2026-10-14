using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;
using Voxels.TowerDefense.TriFlow;

namespace BadNorthNewMode
{
    /// <summary>让船上未下船的敌人也可被索敌：原版 Longship 每帧只报 `hittable:false`（见 PROJECT_SPEC §4 坑表）。</summary>
    internal sealed class ShipboardThreat : AgentComponent, ITriFlowObject
    {
        Landing _landing;

        internal void Init(Landing landing, Agent owner)
        {
            _landing = landing;
            Setup(owner);                     // 后加的组件不会经过 Agent.Setup 的收集，这里手动绑定
        }

        void ITriFlowObject.OnProximity(Agent otherAgent)
        {
        }

        void Update()
        {
            Agent a = agent;
            if (a == null || _landing == null) return;
            if (a.navPos.island != null) { enabled = false; return; }   // 已下船 → 交回 Brain 自己上报

            // v1.7.1-b3（修"盾兵/盾矛挡不住我们投放的巨弓"）：与原版 `Brain.<Setup>m__0` 的门控对齐 ——
            // 原版的存在上报带 `if (agent.navPos.island == null) return;`（**船上根本不报**），船上只有 `Longship` 报，
            // 且它的 Data 是 `dangerous:false, hittable:false`。我们这里**只**补"可被索敌"（hittable:true），
            // 绝不能把 dangerous 写成 true：`Shield.ShieldCheck` 第②条是
            //     `enemyDist < 1 && enemyData.dangerous  →  noShield`（英军**主动放盾**），
            // 一旦我们在船上就以 distance 0 宣告"危险敌人"，英军会一直把盾放下 → 任何在飞的箭都挡不住。
            if (!a.spawned.active || !a.aliveState.active) return;        // 同原版 aliveAndGrounded 门控：死了/未生成不上报

            Longship ship = _landing.spawnedShip;
            if (ship != null && ship.distanceRemaining > 4f) return;    // 离岸还远：与原版一致不报

            a.faction.presenceObj.AddPending(new Data(this, _landing.navPos, false, true, 0f, 1f));
        }
    }
}
