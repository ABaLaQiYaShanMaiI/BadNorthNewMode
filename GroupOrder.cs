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
        internal void SetTarget(NavSpot t) { _target = t; }
        internal void SetSlot(Vector3 world, bool has) { _slotWorld = world; _hasSlot = has; }

        void IAgentOrder.ApplyOrder()
        {
            agent.walkDir = agent.orderDir;
            agent.movability = Mathf.Lerp(2f, 0.2f, agent.orderDist);
            agent.LookInDirection(agent.orderDir, 720f, 20f);
        }

        void IAgentOrder.ApplyWalk()
        {
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

            TrackStuck();                                           // T8：卡住检测

            // 接近目标格后回自己的槽位（到位即停，避免原地抖动）；解卡期间跳过槽位、先走距离场
            if (_hasSlot && Time.time >= _bypassUntil)
            {
                Vector3 toSlot = _slotWorld - agent.wPos;
                toSlot.y = 0f;
                float d = toSlot.magnitude;

                if (dist < 0.12f && d < 0.3f) { dir = Vector3.zero; dist = 0f; return; }    // 已就位 → 站住
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
