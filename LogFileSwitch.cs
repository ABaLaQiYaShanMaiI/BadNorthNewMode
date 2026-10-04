using BepInEx;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace BadNorthNewMode
{
    /// <summary>
    /// 文件日志开关（v1.5.0）：BepInEx 在**插件加载之前**就建好了写文件的 <see cref="DiskLogListener"/>，
    /// 所以只能在这里把它摘掉并释放——控制台（BepInEx 控制台窗口 / Unity 日志）不受影响。
    /// <para>默认 <c>false</c> = 不生成日志文件，并删掉本次启动刚写出的那点启动日志（很小，见上限）。</para>
    /// <para><c>true</c> = 完全不干预 BepInEx 原行为（排查点时选 / 下船卡住等问题时用）。</para>
    /// </summary>
    internal static class LogFileSwitch
    {
        const string FileName = "LogOutput.log";

        /// <summary>只删"启动那几行"的残留：超过这个大小说明还有别的写入者，就保留不动。</summary>
        const long StartupRemnantLimit = 64 * 1024;

        /// <summary>按 cfg 决定是否允许写日志文件。任何失败都只打一条警告，绝不影响游戏。</summary>
        internal static void Apply(bool allowFile)
        {
            if (allowFile) return;
            try
            {
                List<ILogListener> disk = new List<ILogListener>();
                foreach (ILogListener l in Logger.Listeners)
                    if (l is DiskLogListener) disk.Add(l);
                if (disk.Count == 0) return;                 // BepInEx.cfg 里本来就没开文件日志：不做事，也不删文件

                for (int i = 0; i < disk.Count; i++)
                {
                    Logger.Listeners.Remove(disk[i]);        // 先摘：之后一行都不会再落盘
                    ((DiskLogListener)disk[i]).Dispose();    // 再关句柄：文件停在此刻
                }
                Util.Log("[NewMode] 文件日志默认关闭（[Diag] LogToFile = false）：本次不写 BepInEx\\" + FileName +
                         "。需要排查时把它设成 true 再启动。");
                DeleteStartupRemnant();
            }
            catch (Exception e)
            {
                Util.Warn("[NewMode] 关闭文件日志失败（不影响游戏）：" + e.Message);
            }
        }

        /// <summary>删掉本次启动刚写出的残留（此时文件必然很小；删不掉就留着，不算错误）。</summary>
        static void DeleteStartupRemnant()
        {
            try
            {
                string path = Path.Combine(Paths.BepInExRootPath, FileName);
                if (!File.Exists(path)) return;
                if (new FileInfo(path).Length > StartupRemnantLimit) return;
                File.Delete(path);
                Util.Log("[NewMode] 已删除启动残留：" + path);
            }
            catch { }
        }
    }
}
