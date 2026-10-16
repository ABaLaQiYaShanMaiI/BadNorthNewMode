using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>MarqueeSelect 的**查询半边**（v1.7.3 / P2 拆分自 `MarqueeSelect.cs`，纯搬动、IL 级零行为变更）：
    /// 候选登记（`EnsureCandidates`）、矩形/点选命中（屏幕投影 + 世界四边形两套）、最近单位查询、
    /// 以及修饰键与光标行为（`ShiftHeld` / `SingleButtonMode` / `CursorBehaviour` …）。
    /// 这里的成员**不持有可变状态**（只读注册表与只读输入），改起来不会牵动输入时序。</summary>
    internal static partial class MarqueeSelect
    {
        /// <summary>世界四边形兜底框选：用 NavSpotCast 把矩形四角变成世界点，判单位脚点是否在四边形内（完全不依赖屏幕投影）。</summary>
        static List<ForeignUnit> PickWorld(Rect r)
        {
            List<ForeignUnit> res = new List<ForeignUnit>();
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (gm != null) ? gm.island : null;
            if (island == null) return res;

            Vector3 c0, c1, c2, c3;
            string diag;
            if (!Plugin.TryGetLandPoint(island, new Vector2(r.xMin, r.yMin), out c0, out diag)) return res;
            if (!Plugin.TryGetLandPoint(island, new Vector2(r.xMax, r.yMin), out c1, out diag)) return res;
            if (!Plugin.TryGetLandPoint(island, new Vector2(r.xMax, r.yMax), out c2, out diag)) return res;
            if (!Plugin.TryGetLandPoint(island, new Vector2(r.xMin, r.yMax), out c3, out diag)) return res;

            EnsureCandidates();
            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 p = a.wPos;
                if (InQuadXZ(p, c0, c1, c2, c3)) res.Add(f);
            }
            return res;
        }

        /// <summary>XZ 平面上的凸四边形包含判定（四角按矩形顺/逆时针皆可，取叉积同号）。</summary>
        static bool InQuadXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float s0 = Cross(a, b, p);
            float s1 = Cross(b, c, p);
            float s2 = Cross(c, d, p);
            float s3 = Cross(d, a, p);

            bool allNeg = (s0 <= 0f && s1 <= 0f && s2 <= 0f && s3 <= 0f);
            bool allPos = (s0 >= 0f && s1 >= 0f && s2 >= 0f && s3 >= 0f);
            return allNeg || allPos;
        }

        static float Cross(Vector3 a, Vector3 b, Vector3 p)
        {
            return (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
        }

        static List<ForeignUnit> Pick(Rect screenRect)
        {
            EnsureCandidates();
            List<ForeignUnit> res = new List<ForeignUnit>();

            Camera cam = Cam();
            if (cam == null) return res;

            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
                if (sp.z <= 0f) continue;                                    // 相机背后
                if (screenRect.Contains(new Vector2(sp.x, sp.y))) res.Add(f);
            }
            return res;
        }

        /// <summary>候选来源：标记注册表 ∪ 在册 squad 成员（缺标记**自愈**补上）∪（可选）原生单位（原版上岛的敌人）。
        /// v1.7.3（P1）：**同帧只扫一次**（按下 / 松开 / 分派框选 / Plugin 的 0.25s 定时都会调它），并把"我们的单位"表复用、
        /// 不再每次 `new List&lt;Agent&gt;()`。自愈只在首次扫描时起作用，所以同帧去重不影响正确性（新单位在投放时已 `Attach`）。</summary>
        internal static void EnsureCandidates()
        {
            if (_candFrame == Time.frameCount) return;
            _candFrame = Time.frameCount;

            ForeignUnit.Prune();

            List<Agent> ours = _candOurs;
            ours.Clear();
            SpawnLedger.CollectOurAgents(ours);
            for (int i = 0; i < ours.Count; i++)
            {
                Agent a = ours[i];
                if (a == null || a.GetComponent<ForeignUnit>() != null) continue;

                VikingAgent va = a.GetComponent<VikingAgent>();
                ForeignUnit.Attach(a, (va != null && va.vikingReference != null) ? va.vikingReference.name : a.name);
            }

            // 原生单位（原版上岛的敌人）也登记进来：与我们投放的（非原生）同为 vikings 阵营
            if (!Util.V(ModConfig.ControlNativeUnits, true)) return;

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (gm != null) ? gm.island : null;
            Faction vik = (island != null) ? island.vikings : null;
            if (vik == null || vik.agents == null) return;

            int added = 0;
            for (int i = 0; i < vik.agents.Count; i++)
            {
                Agent a = vik.agents[i];
                if (a == null || a.GetComponent<ForeignUnit>() != null) continue;
                if (!ForeignUnit.Selectable(a)) continue;                   // v1.6.0：船上的原生单位也先登记，能像我们的单位一样"上船前就选好"（落地自动去集结点）

                VikingAgent va = a.GetComponent<VikingAgent>();
                ForeignUnit.Attach(a, (va != null && va.vikingReference != null) ? va.vikingReference.name : a.name, true);
                added++;
            }

            if (added > 0 && !object.ReferenceEquals(_loggedIsland, island))
            {
                _loggedIsland = island;
                Util.Log(Loc.F("[NewMode] 已登记原生单位 {0} 个（可遥控；F1 菜单可关）。", added));
            }
        }

        static Island _loggedIsland;

        /// <summary>按下点附近最近的非原生单位（决定"单击选择"的目标；也决定拖动是否算框选）。</summary>
        static ForeignUnit NearestForeignUnit(Vector2 screenPos)
        {
            return NearestForeignUnitWithin(screenPos, Util.V(ModConfig.RemoteGrabRadius, 64));
        }

        static ForeignUnit NearestForeignUnitWithin(Vector2 screenPos, int radius)
        {
            EnsureCandidates();
            Camera cam = Cam();
            if (cam == null) return null;

            float r2 = (radius <= 0) ? float.MaxValue : (float)radius * radius;

            ForeignUnit best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
                if (sp.z <= 0f) continue;

                float dx = sp.x - screenPos.x;
                float dy = sp.y - screenPos.y;
                float d = dx * dx + dy * dy;
                if (d > r2 || d >= bestD) continue;

                bestD = d;
                best = f;
            }
            return best;
        }

        /// <summary>点选（世界距离版，主路径）：用投放同一套 NavSpotCast 取世界点，再比单位世界距离——不依赖屏幕投影。</summary>
        static ForeignUnit NearestForeignUnitWorld(Vector2 screenPos)
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (gm != null) ? gm.island : null;

            Vector3 land;
            string diag;
            if (!Plugin.TryGetLandPoint(island, screenPos, out land, out diag)) return null;

            CamFor(land, screenPos);                     // 用"地面点 ↔ 点击处"校正相机（框选/标记也受益）

            EnsureCandidates();
            float radius = Util.V(ModConfig.RemoteClickRadius, 1.2f);
            float best = radius;
            ForeignUnit pick = null;

            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 p = a.wPos;
                p.y = land.y;
                float d = Vector3.Distance(p, land);
                if (d < best) { best = d; pick = f; }
            }
            return pick;
        }

        static bool SelectAllKeyDown()
        {
            return KeyDown(ModConfig.RemoteSelectAllKey);
        }

        /// <summary>我们自己的快捷键是否按下（None = 关闭；cfg 为空也不炸）。</summary>
        static bool KeyDown(ConfigEntry<KeyCode> key)
        {
            return key != null && key.Value != KeyCode.None && Input.GetKeyDown(key.Value);
        }

        /// <summary>最近可用单位的屏幕信息（距离/原始坐标/z/鼠标/屏幕尺寸）——用来判定投影是否可信。</summary>
        static string NearestScreenInfo(Vector2 screenPos)
        {
            Camera cam = Cam();
            if (cam == null) return Loc.T("相机不可用");

            float best = float.MaxValue;
            string info = Loc.T("无（没有可用的非原生单位）");
            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
                float dx = sp.x - screenPos.x;
                float dy = sp.y - screenPos.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d >= best) continue;

                best = d;
                info = Loc.F("{0:F0}px（屏 {1:F0},{2:F0} z={3:F1}｜鼠标 {4:F0},{5:F0}｜屏幕 {6}×{7}｜相机 {8}）",
                    d, sp.x, sp.y, sp.z, screenPos.x, screenPos.y, Screen.width, Screen.height, cam.name);
            }
            return info;
        }

        /// <summary>按住"自由框选键"→ 从任意位置起拖都算框选（相机交给框选）。左右 Alt 都认。</summary>
        static bool FreeMarqueeKeyHeld()
        {
            ConfigEntry<KeyCode> key = ModConfig.RemoteFreeMarqueeKey;
            if (key == null || key.Value == KeyCode.None) return false;
            if (Input.GetKey(key.Value)) return true;

            return (key.Value == KeyCode.LeftAlt || key.Value == KeyCode.RightAlt) &&
                   (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
        }

        internal static bool ShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>立即下令的修饰键：Shift（精确指挥）或按住"全选键"R（全选 + 批量下令）；不按也能下令，只是走"延迟仲裁"路径（见 §5/T22）。</summary>
        internal static bool CommandModifierHeld()
        {
            if (ShiftHeld()) return true;
            ConfigEntry<KeyCode> key = ModConfig.RemoteSelectAllKey;
            return key != null && key.Value != KeyCode.None && Input.GetKey(key.Value);
        }

        internal static float ScreenDist2(Agent a, Vector2 center)
        {
            Camera cam = Cam();
            if (cam == null || a == null) return float.MaxValue;

            Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
            if (sp.z <= 0f) return float.MaxValue;

            float dx = sp.x - center.x;
            float dy = sp.y - center.y;
            return dx * dx + dy * dy;
        }

        /// <summary>原版的单/双键设置（`Navigator.OnButtonUp` 正是按它分派的：双键=左选右走；单键=没选中→选、有选中→走）。</summary>
        internal static UserSettings.CursorBehaviour CursorBehaviour()
        {
            UserSettings us = Profile.userSettings;
            return object.ReferenceEquals(us, null) ? UserSettings.CursorBehaviour.TwoButton : us.cursorBehaviour;
        }

        /// <summary>单键 / 触摸：一个键按"当前有没有选中"承担选中与位移两件事（照抄原版映射，v1.5.6）。</summary>
        internal static bool SingleButtonMode()
        {
            return CursorBehaviour() != UserSettings.CursorBehaviour.TwoButton;
        }

        internal static bool HasSelection()
        {
            return HasPending;      // 只看"本次选中"：已有小队在册 ≠ 有选中（未选中不得移动，见 §5）
        }

        /// <summary>可被框选/标记/点选：还活着、已生成——**含仍在船上**（v1.5.6 起可以在船上就选好）。</summary>
        static bool Usable(Agent a)
        {
            return ForeignUnit.Selectable(a);
        }

    }
}