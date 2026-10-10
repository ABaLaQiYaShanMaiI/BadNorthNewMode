using I2.Loc;
using RTM.UISystem;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense;
using Voxels.TowerDefense.UI;

namespace BadNorthNewMode
{
    /// <summary>借用原版 UI（提示条 / 确认框 / 按钮音 / 字体）；每一项失败都只是返回 false，由调用方回退，见 PROJECT_SPEC §9。</summary>
    internal static class VanillaUI
    {
        /// <summary>原版模态框是否正开着：开着的期间我们的输入与 IMGUI 一律让位（否则点确认框会顺带指挥单位）。</summary>
        internal static bool ModalShowing
        {
            get
            {
                try
                {
                    UIManager mgr = Singleton<UIManager>.instance;
                    if (mgr == null) return false;

                    UIMenu top = mgr.activeMenu;
                    if (top == null || !top.isOpen) return false;
                    return top is ModalOverlay;
                }
                catch { return false; }
            }
        }

        /// <summary>把一句话发到**原版通知条**（自带音效 / 淡入淡出 / 排队）。取不到原版对象返回 false。</summary>
        internal static bool Toast(string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!Util.V(ModConfig.UseVanillaUI, true)) return false;

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            IslandUINotificationManager nm = (gm != null) ? gm.notificationManager : null;
            if (nm == null) return false;

            try
            {
                IslandUINotification n = nm.PostMessage(text, IslandUINotification.Priority.None, seconds);
                if (n == null) return false;

                SetNonLocalized(n, text);
                return true;
            }
            catch (System.Exception e)
            {
                Util.LogOnce("toast:" + e.GetType().Name,
                    Loc.F("[NewMode] 原版提示条不可用（{0}）→ 回退到 HUD 文本", e.GetType().Name));
                return false;
            }
        }

        /// <summary>确认框的"确定"回调：用自己的委托类型 —— net472 的 `System.Action` / `Func` 在游戏 mscorlib 2.0 里不存在，会 MissingMethodException（见 §4 坑表）。</summary>
        internal delegate void ConfirmAction();

        static ConfirmAction _onOk;
        static System.Reflection.MethodInfo _okMethod;

        /// <summary>**原版确认框**（ModalOverlay：标题 + 正文 + 确定/取消，键盘手柄都能点）。返回 false = 没弹（调用方直接执行）。</summary>
        internal static bool Confirm(string title, string message, ConfirmAction onOk)
        {
            if (!Util.V(ModConfig.ConfirmDestructive, true)) return false;
            if (ModalShowing) return false;                     // 已经有原版模态框在 → 不再叠一个（直接执行由调用方兜底）

            try
            {
                ModalOverlay mo = ModalOverlay.GetInstance();
                if (mo == null) return false;

                System.Reflection.MethodInfo add = typeof(ModalOverlay).GetMethod("AddOKButton");
                if (object.ReferenceEquals(add, null)) return false;

                System.Reflection.ParameterInfo[] ps = add.GetParameters();
                if (ps.Length != 1) return false;

                if (object.ReferenceEquals(_okMethod, null))
                {
                    const System.Reflection.BindingFlags flags =
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
                    _okMethod = typeof(VanillaUI).GetMethod("OnModalOk", flags);
                }
                if (object.ReferenceEquals(_okMethod, null)) return false;

                _onOk = onOk;
                System.Delegate d = System.Delegate.CreateDelegate(ps[0].ParameterType, _okMethod);
                if (object.ReferenceEquals(d, null)) return false;

                mo.InitializeNonLocalized(title, message, true);      // 非本地化：中英都直接显示我们的话
                add.Invoke(mo, new object[] { d });                   // 原版会在动作返回 true 后自己 Dismiss
                return true;
            }
            catch (System.Exception e)
            {
                Util.LogOnce("confirm:" + e.GetType().Name,
                    Loc.F("[NewMode] 原版确认框不可用（{0}）→ 直接执行", e.GetType().Name));
                return false;
            }
        }

        /// <summary>原版确认框的"确定"回调（无参、返回 bool：签名必须与它要的委托类型一致）。</summary>
        static bool OnModalOk()
        {
            ConfirmAction a = _onOk;
            _onOk = null;
            if (object.ReferenceEquals(a, null)) return true;

            try { a(); }
            catch (System.Exception e) { Util.Warn("[NewMode] confirm action failed: " + e); }
            return true;
        }

        internal static void Click()          // 原版按钮音 / 错误音（键鼠与手柄各一套，原版自己选）
        {
            try { FabricWrapper.PostEvent(FabricID.uiButtonClick); } catch { }
        }

        internal static void Error()
        {
            try { FabricWrapper.PostEvent(FabricID.uiError); } catch { }
        }

        static Font _font;
        static string _fontInfo = "";
        static float _nextFontScan;

        static readonly string[] LatinOnly = { "arial", "liberation", "roboto", "helvetica", "legacyruntime", "segoe ui" };   // 纯拉丁字体：靠系统兜底也能画中文，但观感与原版默认一样 → 不作候选

        // 原版自己的字体资产名（用 UnityPy 从 data.unity3d 里读出来的真名）：Body* = 正文艺术字，Buttons = 按键字
        static readonly string[] BodyCn = { "Body_Chinese_Simp", "Body_Chinese", "Body_Chinese_Trad", "Body", "Buttons" };
        static readonly string[] BodyEn = { "Body", "Buttons", "Names" };

        static readonly string[] CjkNames =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑",
            "Source Han Sans SC", "思源黑体", "Noto Sans CJK SC", "Noto Sans SC",
            "SimHei", "黑体", "Microsoft JhengHei", "PingFang SC", "Hiragino Sans GB",
            "SimSun", "宋体", "NSimSun", "Malgun Gothic", "Meiryo", "MS Gothic", "Yu Gothic UI",
        };

        /// <summary>游戏自带字体：① `[UI] FontName` 指定 → ② 原版语言字体（`UserSettingsMenu.fontMap`）→ ③ 系统里支持中文的字体 → ④ 原版任意 Text 的字体。</summary>
        internal static Font GameFont()
        {
            if (_font != null) return _font;                          // Unity 的 == 能识别已随场景销毁的字体
            if (!Util.V(ModConfig.UseVanillaFont, true)) return null;
            if (Time.unscaledTime < _nextFontScan) return null;        // 找不到时节流重扫，别每帧遍历场景

            _nextFontScan = Time.unscaledTime + 2f;
            _font = PickFont();
            if (_font != null) Util.Log("[NewMode] UI 字体 = " + _fontInfo);
            return _font;
        }

        static Font PickFont()
        {
            string want = Util.V(ModConfig.UiFontName, null);
            if (!string.IsNullOrEmpty(want))
            {
                Font f = OSFont(want);
                if (f != null) { _fontInfo = "cfg:" + want; return f; }
            }

            Font vanilla = LoadedFont(Loc.IsEnglish ? BodyEn : BodyCn);   // 原版自己的艺术字（Body_Chinese_Simp / Body / Buttons）
            if (vanilla != null) { _fontInfo = "game:" + vanilla.name; return vanilla; }

            Font map = VanillaLanguageFont();                             // 原版"语言字体表"（UserSettingsMenu.fontMap）
            if (map != null) { _fontInfo = "vanilla:" + map.name; return map; }

            Font os = OsCjkFont();
            if (os != null) { _fontInfo = "os:" + os.name; return os; }

            Font any = AnyTextFont();
            if (any != null) { _fontInfo = "text:" + any.name; return any; }
            return null;
        }

        /// <summary>按名字从**已加载**的字体里挑原版字体（`Body_Chinese_Simp` 等；名字来自 data.unity3d 资源清单）。</summary>
        static Font LoadedFont(string[] names)
        {
            try
            {
                Object[] all = Resources.FindObjectsOfTypeAll(typeof(Font));
                for (int n = 0; n < names.Length; n++)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        Font f = all[i] as Font;
                        if (f != null && string.Equals(f.name, names[n], System.StringComparison.OrdinalIgnoreCase)) return f;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>原版语言字体：`UserSettingsMenu` 里 `fontMap`（语言 → Font）按当前语言取；取不到用 `defaultLanguageFont`。</summary>
        static Font VanillaLanguageFont()
        {
            object menu = StaticField(typeof(UserSettingsMenu), "_instance");
            if (object.ReferenceEquals(menu, null)) return null;

            System.Array map = Field<object>(menu, "fontMap") as System.Array;
            object lang = LanguageOf(Loc.LanguageCode);

            if (map != null && map.Length > 0)
            {
                for (int i = 0; i < map.Length; i++)
                {
                    object item = map.GetValue(i);
                    Font f = Field<Font>(item, "font");
                    if (f == null || !Usable(f)) continue;

                    object l = Field<object>(item, "language");
                    bool hit = (lang != null) && object.Equals(l, lang);
                    if (!hit && !Loc.IsEnglish && (l != null) &&
                        l.ToString().IndexOf("Chinese", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        hit = true;
                    if (hit) return f;
                }
            }

            Font def = Field<Font>(menu, "defaultLanguageFont");
            return (def != null && Usable(def)) ? def : null;
        }

        /// <summary>I2 语言码 → `UserSettings.Language`（用原版自己的 public static 方法；取不到返回 null）。</summary>
        static object LanguageOf(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            try
            {
                System.Reflection.MethodInfo mi = typeof(UserSettings).GetMethod("GetLanguageEnumFromI2Code",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                return object.ReferenceEquals(mi, null) ? null : mi.Invoke(null, new object[] { code });
            }
            catch { return null; }
        }

        /// <summary>系统里挑一个能画中文的字体。</summary>
        static Font OsCjkFont()
        {
            string[] installed = null;
            try { installed = Font.GetOSInstalledFontNames(); } catch { }
            bool haveList = (installed != null) && (installed.Length > 0);

            for (int i = 0; i < CjkNames.Length; i++)
            {
                bool listed = haveList && HasName(installed, CjkNames[i]);
                if (haveList && !listed) continue;

                Font f = OSFont(CjkNames[i]);
                if (f == null) continue;
                if (Usable(f) || listed) return f;      // 系统自报装了这个中文字体 → 直接信它（动态字体刚建好时 HasCharacter 可能还没热身）
            }
            return null;
        }

        static Font OSFont(string name)
        {
            try { return Font.CreateDynamicFontFromOSFont(name, FontSize()); }
            catch { return null; }
        }

        static bool HasName(string[] names, string want)
        {
            for (int i = 0; i < names.Length; i++)
                if (!string.IsNullOrEmpty(names[i]) && names[i].IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>原版任意 Text 用的字体（最后兜底，等于旧版行为）。</summary>
        static Font AnyTextFont()
        {
            try
            {
                IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
                IslandUINotificationManager nm = (gm != null) ? gm.notificationManager : null;
                Text t = (nm != null) ? nm.GetComponentInChildren<Text>(true) : null;
                if (t == null) t = Object.FindObjectOfType<Text>();
                return (t != null) ? t.font : null;
            }
            catch { return null; }
        }

        /// <summary>可用：不是"纯拉丁字体"，且真的画得出中文（否则等于没换字体）。</summary>
        static bool Usable(Font f)
        {
            if (f == null) return false;

            try
            {
                string n = f.name;
                if (!string.IsNullOrEmpty(n))
                {
                    for (int i = 0; i < LatinOnly.Length; i++)
                        if (n.IndexOf(LatinOnly[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
                }
                return f.HasCharacter('剑') || f.HasCharacter('中');
            }
            catch { return false; }
        }

        static int FontSize()
        {
            return Mathf.Clamp(Util.V(ModConfig.UiFontSize, 13), 9, 20);
        }

        /// <summary>复刻原版 `Widget.SetNonLocalizedLabel`：停掉 I2 组件后直接写 uGUI 文本（中英都正确，不依赖词条表）。</summary>
        static void SetNonLocalized(IslandUINotification n, string text)
        {
            Localize lz = Field<Localize>(n, "localizeTarget");
            if (lz != null) lz.enabled = false;

            Text label = null;
            if (lz != null)
            {
                LocalizeTarget<Text> target = lz.mLocalizeTarget as LocalizeTarget<Text>;    // 公开字段，见 §9
                if (target != null) label = target.mTarget;
            }
            if (label == null) label = n.GetComponentInChildren<Text>(true);                 // 兜底：通知条里唯一的文本
            if (label != null) label.text = text;
        }

        /// <summary>反射读私有字段；注意 `FieldInfo == null` 在 mscorlib 2.0 会 MissingMethodException（见 §4 坑表）→ 一律 ReferenceEquals。</summary>
        static T Field<T>(object obj, string name) where T : class
        {
            if (obj == null) return null;

            try
            {
                System.Reflection.FieldInfo f = obj.GetType().GetField(name,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                return object.ReferenceEquals(f, null) ? null : (f.GetValue(obj) as T);
            }
            catch { return null; }
        }

        /// <summary>反射读私有**静态**字段（同样避免 `== null`）。</summary>
        static object StaticField(System.Type type, string name)
        {
            try
            {
                System.Reflection.FieldInfo f = type.GetField(name,
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                return object.ReferenceEquals(f, null) ? null : f.GetValue(null);
            }
            catch { return null; }
        }
    }
}
