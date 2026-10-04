using System.Reflection;

namespace BadNorthNewMode
{
    /// <summary>
    /// 读**原版 Rewired 动作**（反射，避免新增编译依赖）：用原版动作名（如 `SelectNextSquad` / `ManualSlomo`），
    /// 于是玩家在 Options 里的按键重绑定对我们同样生效（见 PROJECT_SPEC §4 事实表）。
    /// 任何一步失败都静默降级为 false（我们的自建键仍然可用）。
    /// </summary>
    internal static class GameInput
    {
        static object _player;
        static MethodInfo _down, _up, _held;
        static bool _failed;

        static void Ensure()
        {
            if (!object.ReferenceEquals(_player, null) || _failed) return;

            try
            {
                System.Type reInput = FindType("Rewired.ReInput");
                if (object.ReferenceEquals(reInput, null)) { _failed = true; return; }

                object players = reInput.GetProperty("players", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
                MethodInfo getPlayer = players.GetType().GetMethod("GetPlayer", new System.Type[] { typeof(int) });
                _player = getPlayer.Invoke(players, new object[] { 0 });

                System.Type pt = _player.GetType();
                _down = pt.GetMethod("GetButtonDown", new System.Type[] { typeof(string) });
                _up = pt.GetMethod("GetButtonUp", new System.Type[] { typeof(string) });
                _held = pt.GetMethod("GetButton", new System.Type[] { typeof(string) });

                // 注意：Type/MethodInfo 的 == 是 .NET 4.0 才有的运算符，游戏 mscorlib 2.0 没有 → 一律用 ReferenceEquals
                if (object.ReferenceEquals(_down, null) || object.ReferenceEquals(_held, null))
                {
                    _failed = true;
                    _player = null;
                }
            }
            catch (System.Exception e)
            {
                _failed = true;
                _player = null;
                Util.Warn("[NewMode] 读取原版 Rewired 输入失败（切队键联动将不可用）：" + e.Message);
            }
        }

        static System.Type FindType(string fullName)
        {
            System.Reflection.Assembly[] asms = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                System.Type t = asms[i].GetType(fullName, false);
                if (!object.ReferenceEquals(t, null)) return t;
            }
            return null;
        }

        /// <summary>原版动作这一帧是否按下（动作名如 "SelectNextSquad"）。</summary>
        internal static bool Down(string action)
        {
            return Invoke(_down, action);
        }

        /// <summary>原版动作是否按住（动作名如 "ManualSlomo"）。</summary>
        internal static bool Held(string action)
        {
            return Invoke(_held, action);
        }

        /// <summary>原版动作这一帧是否抬起。</summary>
        internal static bool Up(string action)
        {
            return Invoke(_up, action);
        }

        static bool Invoke(MethodInfo m, string action)
        {
            Ensure();
            if (object.ReferenceEquals(_player, null) || object.ReferenceEquals(m, null)) return false;

            try { return (bool)m.Invoke(_player, new object[] { action }); }
            catch { _player = null; _failed = false; return false; }   // Rewired 重初始化后自愈
        }
    }
}
