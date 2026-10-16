using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;

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

        /// <summary>指针是否落在**UI** 上（EventSystem 判定；我们自己的 ClickShield 拦截块也算）。
        /// v1.7.4：我们的原始输入路径（点击路由 / 框选）原本只检查自己那两块矩形 —— 点原版 UI（技能条、按钮）时
        /// 仍可能对世界下指令。加上这条与原版一致的门控后，"点 UI" 完全归 UI。</summary>
        internal static bool PointerOverUi()
        {
            EventSystem es = EventSystem.current;
            if (es == null) return false;
            if (!es.IsPointerOverGameObject()) return false;

            // v1.7.4：第一次因这条门控而让路时留一行证据 —— 若遥控点选"突然选不中"，看这行就知道是不是它。
            LogOnce("ovui", Loc.T("[NewMode] 本次点击落在 UI 上 → 归 UI（v1.7.4 门控，遥控不下令）"));
            return true;
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
