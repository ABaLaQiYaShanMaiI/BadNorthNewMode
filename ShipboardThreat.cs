using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;
using Voxels.TowerDefense.TriFlow;

namespace BadNorthNewMode
{
    /// <summary>
    /// 让"还没下船的敌人"也算威胁。
    /// 原版 Longship 每帧会以 `Data(this, landing.navPos, dangerous:false, hittable:false, …)` 上报流场——
    /// 所以我方只能"感到有敌人要来"，却不会把他们当目标（原版设计：要打船得用火箭技能）。
    /// 本组件在船靠岸前 4m 起（与原版 amount 门控一致）到敌人下船为止，额外追加一条 **可命中/有威胁** 的存在，
    /// 使我方索敌把他们当正常敌人交战；敌人一旦下船（navPos.island 成立）即自动停用，交回 Brain 自己上报。
    /// </summary>
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
