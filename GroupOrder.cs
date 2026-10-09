using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>遥控行军 order：原版距离场寻路 + 本组槽位（与 EnglishPatherAgent 同构，见 PROJECT_SPEC §5）。</summary>
    internal sealed class GroupOrder : AgentComponent, IAgentOrder
    {
        NavSpot _target;
        Vector3 _slotWorld;
        NavPos _slotNavPos;
        bool _hasSlot;
        bool _settled;                 // 已就位：站住（到位后不再输出位移，否则互相推挤 = 抽动）
        Arsonist _arsonist;
        bool _arsonistWasEnabled;
        bool _arsonistChecked;
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

        internal void SetSlot(NavPos slot, bool has)
        {
            _slotNavPos = slot;
            _slotWorld = has ? slot.wPos : Vector3.zero;      // 空 NavPos 的 wPos 会抛 "NavPos pos is null"（见 §4 坑表）
            _hasSlot = has;
            _settled = false;
        }

        /// <summary>设置槽位；`world` 可覆盖行走目标点（原版 `SlotPusher` 的推挤结果，见 §5/T37）。</summary>
        internal void SetSlot(NavPos slot, Vector3 world)
        {
            _slotNavPos = slot;
            _slotWorld = world;
            _hasSlot = true;
            _settled = false;
        }

        const float SettledMovability = 0.05f;

        void IAgentOrder.ApplyOrder()
        {
            if (_settled) { agent.walkDir = Vector3.zero; agent.movability = SettledMovability; return; }

            agent.walkDir = agent.orderDir;
            // 贴房子时 orderDist 会被换成房距（很大）→ 必须夹 0..1，否则 Lerp 外推成负 movability（见 §5）
            agent.movability = Mathf.Lerp(2f, 0.2f, Mathf.Clamp01(agent.orderDist));
            agent.LookInDirection(agent.orderDir, 720f, 20f);
        }

        void IAgentOrder.ApplyWalk()
        {
            if (_settled) { agent.walkDir = Vector3.zero; agent.movability = SettledMovability; return; }

            agent.walkDir = agent.orderDir;
            agent.movability = Mathf.Lerp(2f, 0.2f, Mathf.Clamp01(agent.orderDist));
        }

        bool IAgentOrder.WantsControl() { return enabled; }

        void IAgentOrder.SampleOrder(NavPos navPos, ref Vector3 dir, ref float dist)
        {
            NavSpot t = _target;

            if (t == null || !navPos.valid)
            {
                dir = Vector3.zero;
                dist = 0f;
            }
            else
            {
                IPathTarget path = t;
                path.SampleDistanceDir(navPos, ref dir, ref dist);      // 原版距离场：绕障、贴地

                if (_hasSlot && Time.time >= _bypassUntil)
                {
                    Vector3 toSlot = _slotWorld - agent.wPos;
                    toSlot.y = 0f;
                    float d = toSlot.magnitude;

                    // 就位判定（v1.6.0 放宽）：人多时外层槽位被同伴占住、d 降不下来 → **离目标格够近就算到位**（见 §5）
                    if (_settled)
                    {
                        if (d >= 0.8f && dist >= 0.5f) _settled = false;          // 真被挤走才重新起步（滞回）
                    }
                    else if (dist < 0.25f || (dist < 0.6f && d < 0.45f))
                    {
                        _settled = true;
                    }

                    if (_settled) { dir = Vector3.zero; dist = 0f; }
                    // 只在"直线可达"时直奔槽位（同原版 EnglishFormationAgent.ModifyPath 的 TriCast 闸）；否则保持距离场绕行
                    else if (d > 0.05f && (dist < 1.5f || d < 1.5f) && _slotNavPos.valid && navPos.TriCast(_slotNavPos))
                    {
                        dir = toSlot / d;
                        dist = d;
                    }
                }
                else if (Time.time < _bypassUntil && dist >= 0.8f && dir.sqrMagnitude > 0.0001f)
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

                TrackStuck(dist);                                      // 就位后不做卡住检测（否则每 1.5s 自己去绕一圈 = 抽动）
            }

            // `Arsonist` 拿 orderDist ≤ 0.2 判"已到房前"，而我们的 orderDist 是"到指定格的距离"（站定 ≈0）→ 会隔岛扔火炬；换成原版口径的房距（见 §5/T30）
            float houseDist = HouseDist(navPos);
            if (dist < 0.2f) dist = houseDist;
            UpdateArson(houseDist);
        }
        /// <summary>只在"真的贴着完好房子"时把 `Arsonist` 留在 `brain.actions` 里（它会从任何地方扔火炬，见 §5/T30）。</summary>
        void UpdateArson(float houseDist)
        {
            if (agent == null || agent.brain == null || agent.brain.actions == null) return;

            Arsonist arson = Arson();
            if (arson == null) return;

            if (!_arsonistChecked)
            {
                _arsonistChecked = true;
                _arsonistWasEnabled = agent.brain.actions.Contains(arson);
            }

            bool want = _arsonistWasEnabled && houseDist < 0.2f;
            bool has = agent.brain.actions.Contains(arson);
            if (want)
            {
                if (!has) agent.brain.actions.Add(arson);
            }
            else if (has)
            {
                agent.brain.actions.Remove(arson);
            }

            if (Util.V(ModConfig.VerboseLog, false) && Time.unscaledTime >= _nextArsonLog)
            {
                _nextArsonLog = Time.unscaledTime + 2f;
                Util.Log(Loc.F("[NewMode][烧房] 房距 {0:F2}｜orderDist {1:F2}｜放行 {2}｜房子 {3}",
                    houseDist, agent.orderDist, want, HouseName(arson)));
            }
        }

        static float _nextArsonLog;

        string HouseName(Arsonist arson)
        {
            House h = (arson != null && arson.pather != null) ? arson.pather.house : null;
            return (h == null) ? Loc.T("无（没有分到房子）") : h.name;
        }

        internal void RestoreArson()
        {
            Arsonist arson = Arson();
            if (arson == null || agent == null || agent.brain == null || agent.brain.actions == null) return;
            if (_arsonistWasEnabled && !agent.brain.actions.Contains(arson)) agent.brain.actions.Add(arson);
            _arsonist = null;
        }

        Arsonist Arson()
        {
            if (_arsonist == null && agent != null) _arsonist = agent.GetComponent<Arsonist>();
            return _arsonist;
        }

        /// <summary>照抄原版 `VikingPatherSquad.SampleOrder`：用房子距离场算"到那座房子的距离"再减高差补偿（见 §5/T30）。</summary>
        float HouseDist(NavPos navPos)
        {
            Arsonist arson = Arson();
            if (arson == null || arson.pather == null) return 1f;

            House h = arson.pather.house;
            if (h == null || !h.intact || h.distanceField == null || !navPos.valid) return 1f;

            float d = h.distanceField.SampleDistance(navPos);
            float bonus = ExtraMath.RemapValue(Mathf.Abs(navPos.pos.y - h.transform.position.y), 0.2f, 0f);
            return Mathf.Max(0f, d - bonus);
        }

        /// <summary>T8：连续 1.5s 几乎没位移 → 判定被堵，接下来 0.8s 绕行；目标格附近不做（同伴挤住不算被堵，见 §5）。</summary>
        void TrackStuck(float dist)
        {
            if (dist < 0.8f) { _stuckTime = 0f; _bypassUntil = 0f; return; }

            Vector3 p = agent.wPos;
            float moved = (p - _lastWPos).magnitude;
            _lastWPos = p;

            if (moved < 0.002f) _stuckTime += Time.deltaTime; else _stuckTime = 0f;
            if (_stuckTime > 1.5f) { _stuckTime = 0f; _bypassUntil = Time.time + 0.8f; }
        }
    }
}
