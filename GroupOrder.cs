using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>遥控行军 order：原版距离场寻路 + 本组槽位（与 EnglishPatherAgent 同构，见 PROJECT_SPEC §5）。</summary>
    internal sealed class GroupOrder : AgentComponent, IAgentOrder
    {
        NavSpot _target;
        Vector3 _slotWorld;
        bool _hasSlot;
        bool _settled;                 // 已就位：站住（v1.5.7 —— 到位后不再输出位移，否则互相推挤 = 抽动）
        IAgentOrder _prevOrder;        // 接管前的 order（清场时原样还回去）
        MonoBehaviour _prevMono;
        Vector3 _lastWPos;             // T8：卡住检测用
        float _stuckTime;
        float _bypassUntil;

        /// <summary>后加的组件不会经过 Agent.Setup 的收集，这里手动绑定（同 ShipboardThreat）。</summary>
        internal void Init(IAgentOrder prevOrder, MonoBehaviour prevMono)
        {
            _prevOrder = prevOrder;
            _prevMono = prevMono;
            Setup(agent);
        }

        internal IAgentOrder PrevOrder { get { return _prevOrder; } }
        internal MonoBehaviour PrevMono { get { return _prevMono; } }
        internal NavSpot Target { get { return _target; } }

        internal void SetTarget(NavSpot t) { _target = t; _settled = false; }
        internal void SetSlot(Vector3 world, bool has) { _slotWorld = world; _hasSlot = has; _settled = false; }

        const float SettledMovability = 0.05f;

        void IAgentOrder.ApplyOrder()
        {
            if (_settled) { agent.walkDir = Vector3.zero; agent.movability = SettledMovability; return; }

            agent.walkDir = agent.orderDir;
            agent.movability = Mathf.Lerp(2f, 0.2f, agent.orderDist);
            agent.LookInDirection(agent.orderDir, 720f, 20f);
        }

        void IAgentOrder.ApplyWalk()
        {
            if (_settled) { agent.walkDir = Vector3.zero; agent.movability = SettledMovability; return; }

            agent.walkDir = agent.orderDir;
            agent.movability = Mathf.Lerp(2f, 0.2f, agent.orderDist);
        }

        bool IAgentOrder.WantsControl() { return enabled; }

        void IAgentOrder.SampleOrder(NavPos navPos, ref Vector3 dir, ref float dist)
        {
            NavSpot t = _target;
            if (t == null || !navPos.valid) { dir = Vector3.zero; dist = 0f; return; }

            IPathTarget path = t;
            path.SampleDistanceDir(navPos, ref dir, ref dist);      // 原版距离场：绕障、贴地

            if (_hasSlot && Time.time >= _bypassUntil)
            {
                Vector3 toSlot = _slotWorld - agent.wPos;
                toSlot.y = 0f;
                float d = toSlot.magnitude;

                if (_settled)
                {
                    // 滞回：被挤一点点不重新起步。dist 写"到最近完好房子的距离" —— `Arsonist` 拿 orderDist ≤ 0.2 判"已到房前"，
                    // 写 0 会让它站在任何地方都以为贴着房子（隔岛扔火炬，见 §5）
                    dist = NearestHouseDist();
                    dir = Vector3.zero;
                    if (d < 0.8f) return;
                    _settled = false;
                }
                else if (dist < 0.12f && d < 0.35f)                            // 到位即停（阈值要给够，避免边缘反复起步）
                {
                    _settled = true;
                    dir = Vector3.zero;
                    dist = 0f;
                    return;
                }

                if (d > 0.05f && (dist < 1.5f || d < 1.5f)) { dir = toSlot / d; dist = d; return; }
            }
            else if (Time.time < _bypassUntil && dir.sqrMagnitude > 0.0001f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, dir);      // 解卡：加一点横向分量，绕开堵住自己的同伴
                if (side.sqrMagnitude > 0.0001f) dir = dir.normalized + side.normalized * 0.35f;
            }

            if (dir.sqrMagnitude < 0.0001f)                        // 距离场退化（贴边/极端地形）→ 直线兜底，别原地站住
            {
                Vector3 straight = t.navPos.wPos - agent.wPos;
                straight.y = 0f;
                if (straight.sqrMagnitude > 0.0001f)
                {
                    dir = straight.normalized;
                    dist = Mathf.Max(dist, straight.magnitude);
                }
            }

            TrackStuck();                                          // 就位后不做卡住检测（否则每 1.5s 自己去绕一圈 = 抽动）
        }

        /// <summary>到最近完好房子的**水平**距离，并用火炬落点 `worldTargetPos` 做高差门控（否则山脚下会烧崖上的房，见 §5）。</summary>
        float NearestHouseDist()
        {
            Island island = (agent != null && agent.faction != null) ? agent.faction.island : null;
            Village village = (island != null) ? island.village : null;
            if (village == null || village.houses == null) return 1f;

            Vector3 p = agent.wPos;
            float best = 1f;
            for (int i = 0; i < village.houses.Length; i++)
            {
                House h = village.houses[i];
                if (h == null || !h.intact) continue;

                Vector3 target = h.worldTargetPos;
                if (Mathf.Abs(target.y - p.y) > 0.8f) continue;      // 高差太大 → 不算"贴着房子"

                float dx = target.x - p.x;
                float dz = target.z - p.z;
                float dd = Mathf.Sqrt(dx * dx + dz * dz);
                if (dd < best) best = dd;
            }
            return best;
        }

        /// <summary>T8：连续 1.5s 几乎没位移 → 判定被堵，接下来 0.8s 走距离场 + 横向绕行。</summary>
        void TrackStuck()
        {
            Vector3 p = agent.wPos;
            float moved = (p - _lastWPos).magnitude;
            _lastWPos = p;

            if (moved < 0.002f) _stuckTime += Time.deltaTime; else _stuckTime = 0f;
            if (_stuckTime > 1.5f) { _stuckTime = 0f; _bypassUntil = Time.time + 0.8f; }
        }
    }
}
