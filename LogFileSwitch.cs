using BepInEx;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace BadNorthNewMode
{
    /// <summary>文件日志开关：默认不生成日志文件（摘掉 BepInEx 磁盘监听器 + 亲自关其 writer + 清掉本次残留，见 PROJECT_SPEC §5/T21）。</summary>
    internal static class LogFileSwitch
    {
        const string FileName = "LogOutput.log";
        const float RetrySeconds = 30f;

        static readonly List<string> _targets = new List<string>();
        static bool _warnedClose;
        static bool _warnedDelete;
        static float _until;
        static float _nextTry;
        static bool _done;

        /// <summary>按 cfg 决定是否允许写日志文件。任何失败都只打警告，绝不影响游戏。</summary>
        internal static void Apply(bool allowFile)
        {
            if (allowFile) { _done = true; return; }             // 排查模式：完全不干预 BepInEx 原行为

            try
            {
                List<ILogListener> disk = new List<ILogListener>();
                foreach (ILogListener l in Logger.Listeners)
                    if (l is DiskLogListener) disk.Add(l);

                for (int i = 0; i < disk.Count; i++)
                {
                    ILogListener l = disk[i];
                    AddTarget(l);                                // 先记下它的真实文件路径（最可靠）
                    Logger.Listeners.Remove(l);                  // 摘掉：之后一行都不会再落盘
                    CloseListener(l);                            // 亲自关句柄：BepInEx 自带 Dispose 关不掉文件
                }
                Util.Log(Loc.F("[NewMode] 文件日志默认关闭（[Diag] LogToFile = false）：本次不写 BepInEx\\{0}。需要排查时把它设成 true 再启动。",
                    FileName));
            }
            catch (Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 关闭文件日志失败（不影响游戏）：{0}", e.Message));
            }

            AddFallbackTarget();                                 // 没有监听器 / 上面失败时，也按 BepInEx 根目录兜底清一次
            TryDelete();
            if (!_done) _until = UnityEngine.Time.realtimeSinceStartup + RetrySeconds;
        }

        /// <summary>Plugin.Update 每帧调用：把删不掉的残留重试到成功（句柄释放有延迟也不怕）。</summary>
        internal static void Tick()
        {
            if (_done || _targets.Count == 0) return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now > _until)
            {
                _done = true;
                Util.Warn(Loc.F("[NewMode] 仍未能删除日志残留（{0}）；若不需要日志文件，可把 BepInEx.cfg 的 [Logging.Disk] Enabled 设为 false。", _targets[0]));
                return;
            }
            if (now < _nextTry) return;

            _nextTry = now + 0.25f;
            TryDelete();
        }

        /// <summary>从监听器取真实日志路径（StreamWriter → FileStream.Name；拿不到就用 BepInEx 根目录兜底）。</summary>
        static void AddTarget(ILogListener l)
        {
            try
            {
                StreamWriter sw = ((DiskLogListener)l).LogWriter as StreamWriter;
                if (sw == null) return;

                FileStream fs = sw.BaseStream as FileStream;
                if (fs != null) AddPath(fs.Name);
            }
            catch { }
        }

        /// <summary>亲自关句柄：先 Dispose 监听器，再 Dispose 它的 LogWriter（后者才是真正关文件的那一步）。</summary>
        static void CloseListener(ILogListener l)
        {
            try { ((DiskLogListener)l).Dispose(); } catch (Exception e) { WarnClose(e); }

            try
            {
                TextWriter w = ((DiskLogListener)l).LogWriter;
                if (w != null) w.Dispose();
            }
            catch (Exception e) { WarnClose(e); }
        }

        static void AddFallbackTarget()
        {
            try { AddPath(Path.Combine(Paths.BepInExRootPath, FileName)); } catch { }
            try { AddPath(Path.Combine(Path.Combine(Paths.GameRootPath, "BepInEx"), FileName)); } catch { }
        }

        static void AddPath(string p)
        {
            if (!string.IsNullOrEmpty(p) && !_targets.Contains(p)) _targets.Add(p);
        }

        static void WarnClose(Exception e)
        {
            if (_warnedClose) return;
            _warnedClose = true;
            Util.Warn(Loc.F("[NewMode] 关闭文件日志失败（不影响游戏）：{0}", e.Message));
        }

        /// <summary>删除全部目标残留；全部处理完（含"本就不存在"）即视为完成。</summary>
        static void TryDelete()
        {
            int left = 0;
            for (int i = 0; i < _targets.Count; i++)
            {
                string p = _targets[i];
                try
                {
                    if (!File.Exists(p)) continue;               // 本就不存在：不必发消息
                    File.Delete(p);
                    if (File.Exists(p)) { left++; continue; }    // 极少数情况：删除被拒却不抛异常
                    Util.Log(Loc.F("[NewMode] 已删除启动残留：{0}", p));
                }
                catch (Exception e)
                {
                    left++;
                    if (_warnedDelete) continue;
                    _warnedDelete = true;
                    Util.Warn(Loc.F("[NewMode] 暂时删不掉日志残留（{0}）：{1}", p, e.Message));
                }
            }
            if (left == 0) _done = true;
        }
    }
}
