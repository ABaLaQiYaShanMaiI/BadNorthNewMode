using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

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

        static readonly List<string> _once = new List<string>();

        /// <summary>同一 key 只打一次（悬停预览每帧都会走解析，防刷屏）。</summary>
        internal static void LogOnce(string key, string message)
        {
            if (Plugin.Log == null || string.IsNullOrEmpty(key)) return;
            if (_once.Contains(key)) return;
            _once.Add(key);
            Plugin.Log.LogInfo(message);
        }
    }
}
