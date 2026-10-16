using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>零散工具：向量格式化、cfg 取值守卫、日志守卫（原先在 Plugin / LandingInjector 各有一份）。</summary>
    internal static class Util
    {
        internal static string Fmt(Vector3 v)
        {
            return string.Format("({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
        }

        /// <summary>读 cfg：Bind 失败时 entry 为 null，这里兜底默认值。</summary>
        internal static float V(ConfigEntry<float> e, float fallback) { return (e != null) ? e.Value : fallback; }
        internal static int V(ConfigEntry<int> e, int fallback) { return (e != null) ? e.Value : fallback; }
        internal static bool V(ConfigEntry<bool> e, bool fallback) { return (e != null) ? e.Value : fallback; }
        internal static string V(ConfigEntry<string> e, string fallback) { return (e != null) ? e.Value : fallback; }

        internal static void Log(string msg)
        {
            if (Plugin.Log != null) Plugin.Log.LogInfo(msg);
        }

        internal static void Warn(string msg)
        {
            if (Plugin.Log != null) Plugin.Log.LogWarning(msg);
        }

        internal static void Error(string msg)
        {
            if (Plugin.Log != null) Plugin.Log.LogError(msg);
        }

        /// <summary>指针是否落在**真正的 UI 控件**上（v1.7.4 修订 3 实现 / 修订 4 起**默认关闭**）。
        /// ⚠️ 两条硬约束：① **不要用 `EventSystem.IsPointerOverGameObject()`** —— 本游戏里它**恒为真**（世界点击走一个
        /// 全屏手势接收器 `PointerRationalizer`），会把我们的世界点击**静默吞掉**（v1.0.4 与 v1.7.4 各栽过一次，见 §4 坑表）；
        /// ② 本函数**默认不生效**（`[Remote] UiClickGate = false`）—— 它要挡的"点原版 UI 会误下令"从来**没能实机复现**
        /// （既有互斥规则已覆盖：技能条只在原版正选着我方小队时出现，那一刻我们没有选中），所以不许拿它改默认行为（见 §11.10 修订 4）。
        /// 打开时的做法：`EventSystem.RaycastAll` 拿命中列表（已按"从上到下"排序），**跳过原版那个全屏接收器**
        /// （它代表"点在世界上"），剩下的命中项才算 UI 控件（技能条、按钮、原版面板）→ 这时才归 UI。</summary>
        internal static bool PointerOverUi()
        {
            if (!V(ModConfig.UiClickGate, false)) return false;   // v1.7.4 修订 4：默认关 = 与 v1.7.3 及以前完全一致

            EventSystem es = EventSystem.current;
            if (es == null) return false;

            if (_ped == null) _ped = new PointerEventData(es);
            _ped.position = Input.mousePosition;                 // v1.7.4：PointerEventData 复用，不每帧 new

            _hits.Clear();
            es.RaycastAll(_ped, _hits);

            for (int i = 0; i < _hits.Count; i++)
            {
                GameObject go = _hits[i].gameObject;
                if (go == null) continue;                        // 命中项可能已被销毁
                if (IsWorldReceptor(go)) continue;

                // 每个"真的挡住了点击"的 UI 物体只打一行（同一个 UI 反复点不会刷屏）—— 若遥控点选"突然选不中"，看这行就知道是谁挡的。
                LogOnce("ovui:" + go.name, Loc.T("[NewMode] 本次点击落在 UI 上 → 归 UI（v1.7.4 门控，遥控不下令）") + " ← " + go.name);
                return true;
            }
            return false;
        }

        static readonly List<RaycastResult> _hits = new List<RaycastResult>();
        static PointerEventData _ped;

        /// <summary>这个命中的物体属于原版"全屏世界手势接收器"吗？—— 它代表"点在世界上"，**不算 UI**。</summary>
        static bool IsWorldReceptor(GameObject go)
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            PointerRationalizer pr = (gm != null) ? gm.pointerRationalizer : null;
            if (pr == null) return false;

            Transform r = pr.transform;                          // 接收器自己 / 它身上的 Graphic / 它的父级（Graphic 在父级的情形）
            Transform t = go.transform;
            return (t == r) || t.IsChildOf(r) || r.IsChildOf(t);
        }

        static readonly List<string> _once = new List<string>();

        internal static void LogOnce(string key, string message)   // 同一 key 只打一次（悬停预览每帧都会走解析，防刷屏）
        {
            if (Plugin.Log == null || string.IsNullOrEmpty(key)) return;
            if (_once.Contains(key)) return;
            _once.Add(key);
            Plugin.Log.LogInfo(message);
        }
    }
}
