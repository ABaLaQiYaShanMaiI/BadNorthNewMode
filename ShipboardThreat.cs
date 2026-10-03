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

            Longship ship = _landing.spawnedShip;
            if (ship != null && ship.distanceRemaining > 4f) return;    // 离岸还远：与原版一致不报

            a.faction.presenceObj.AddPending(new Data(this, _landing.navPos, true, true, 0f, 1f));
        }
    }
}
