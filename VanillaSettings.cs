using System.Collections.Generic;
using System.Reflection;
using I2.Loc;
using UnityEngine;
using Voxels.TowerDefense.UI;

namespace BadNorthNewMode
{
    /// <summary>把两行开关挂进**原版设置界面**（v1.7.5）。见 PROJECT_SPEC §11.12。
    ///
    /// 原版结构（反编译 `BadNorthDatabase` 核实）：
    ///   `UserSettingsMenu : GeneratedMenu : UIMenu`；`GeneratedMenu.Initialize()`（由 `Awake` / `IGameSetup.OnGameAwake`
    ///   经 `MaybeInitialize()` 只调一次）里用 `AddBoolWidget(词条, getter, setter, 分类Transform)` 建出各项；
    ///   分类容器挂 `VerticalLayoutGroup` → **后加的子项会自动排版**，所以只要等它初始化完成再追加两行即可。
    ///
    /// 三条设计约束：
    ///   ① **不引入 Harmony / MMHOOK**（本模组一贯"零补丁"）：改为每秒轮询一次私有 `_instance` + `initialized`，
    ///      初始化完成后追加；结构不符就**一次性放弃并 Warn**，F1 菜单与 cfg 照旧可用。
    ///   ② 标签走游戏的 **I2 本地化**（原版控件只认"词条"，缺词条 = 空白）→ 运行期把两条词条注入
    ///      `LocalizationManager.Sources`（中/英各一份）并 `UpdateDictionary(true)`。
    ///   ③ 两行的 getter/setter **直接读写 cfg**（`[General] Enabled` / `[Native] ControlNativeUnits`）⇒
    ///      原版设置、F1 菜单、cfg 文件三处天然同步，不需要任何额外状态。</summary>
    internal static class VanillaSettings
    {
        // 注入游戏本地化表的词条名（不是我们 Loc 的词条；中英文本各写一份）
        const string TermEnabled = "BADNORTHNEWMODE/SETTINGS/ENABLED";
        const string TermNative = "BADNORTHNEWMODE/SETTINGS/NATIVE";
        const string TextEnabledZh = "BadNorthNewMode：启用模组";
        const string TextEnabledEn = "BadNorthNewMode: mod enabled";
        const string TextNativeZh = "原生单位：可遥控";
        const string TextNativeEn = "Native units: controllable";

        const float ScanInterval = 1f;         // 每秒找一次菜单；挂上后只做一次"实例还在不在"的比较

        static FieldInfo _instanceField;       // UserSettingsMenu._instance（private static）
        static FieldInfo _initializedField;    // GeneratedMenu.initialized（private bool）
        static FieldInfo _controlsField;       // UserSettingsMenu.categoryControls（private Transform）
        static bool _probed;
        static bool _failed;                   // 结构不符 / 出错 → 永久放弃（不刷日志、不每帧重试）
        static UserSettingsMenu _installedOn;  // 已挂好的实例（换场景换了实例就重挂）
        static Widget _wEnabled, _wNative;     // 我们那两行的控件（菜单开着时按原版节奏重刷显示用）
        static float _nextScan;

        static MethodInfo _addBoolWidget;      // GeneratedMenu.AddBoolWidget(string, Func<bool>, Func<bool,bool>, Transform)
        static System.Type _funcBool;          // 游戏侧的 System.Func<bool> —— ⚠️ 在 System.Core 里，**不在 mscorlib**
        static System.Type _funcBoolBool;      // 游戏侧的 System.Func<bool,bool>
        static System.Delegate _getEnabled, _setEnabled, _getNative, _setNative;   // 按游戏侧委托类型现造的委托

        /// <summary>由 `Plugin.Update` 每帧调用（内部自己节流）。**总开关关掉时也要调** ——
        /// 关掉模组后还得靠这一项从原版设置里再打开它。</summary>
        internal static void Tick()
        {
            if (_failed) return;

            // 已经挂上：菜单开着时按**原版节奏**（每 15 帧，同 `UserSettingsMenu.UpdateOccasionally`）重刷两行 ——
            // 这样在 F1 菜单 / cfg 里改了值，设置界面里的显示也立刻跟上（控件本身只在 OnEnable 时自刷一次）。
            if (_installedOn != null)
            {
                RefreshRows();
                return;
            }

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanInterval;

            try
            {
                if (!Probe()) return;

                UserSettingsMenu cur = _instanceField.GetValue(null) as UserSettingsMenu;
                if (cur == null) return;                                       // 菜单还没建出来 → 下一轮再看
                if (cur == _installedOn) return;                               // 这个实例已经挂过
                if (!Initialized(cur)) return;                                 // 还没初始化完（Initialize 里会 ClearWidgets）→ 再等
                Install(cur);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Util.Warn(Loc.F("[NewMode] 挂原版设置项失败（{0}）→ 跳过；F1 菜单与 cfg 仍可用", e.Message));
            }
        }

        /// <summary>菜单开着时按 15 帧节奏重刷两行：`Widget.ForceUpdate()` → `ValueWidget.onForceUpdate(GetValue())`
        /// ⇒ 读我们的 getter（cfg）重新显示。设置界面没开着就什么都不做。</summary>
        static void RefreshRows()
        {
            if (Time.frameCount % 15 != 0) return;
            if (!_installedOn.gameObject.activeInHierarchy) return;

            if (_wEnabled != null) _wEnabled.ForceUpdate();
            if (_wNative != null) _wNative.ForceUpdate();
        }

        static bool Probe()
        {
            if (_probed) return true;
            _probed = true;

            _instanceField = typeof(UserSettingsMenu).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            _controlsField = typeof(UserSettingsMenu).GetField("categoryControls", BindingFlags.NonPublic | BindingFlags.Instance);
            _initializedField = typeof(GeneratedMenu).GetField("initialized", BindingFlags.NonPublic | BindingFlags.Instance);

            // 反射成员一律 ReferenceEquals 判空（`FieldInfo == null` 会编成 MemberInfo.op_Equality，游戏 mscorlib 2.0 没有）
            if (object.ReferenceEquals(_instanceField, null) || object.ReferenceEquals(_controlsField, null) ||
                object.ReferenceEquals(_initializedField, null))
            {
                _failed = true;
                Util.Warn(Loc.T("[NewMode] 原版设置菜单结构不符（找不到 _instance / categoryControls / initialized）→ 不挂设置项；F1 菜单与 cfg 仍可用"));
                return false;
            }
            return true;
        }

        static bool Initialized(UserSettingsMenu menu)
        {
            object v = _initializedField.GetValue(menu);
            return (v is bool) && (bool)v;
        }

        static void Install(UserSettingsMenu menu)
        {
            Transform column = _controlsField.GetValue(menu) as Transform;     // 「控制」分类（与周边布尔项同一列）
            if (column == null)
            {
                _failed = true;
                Util.Warn(Loc.T("[NewMode] 原版设置菜单的连接分类为空 → 不挂设置项；F1 菜单与 cfg 仍可用"));
                return;
            }

            EnsureTerms();

            // 控件创建走**反射**：`AddBoolWidget` 的第 2/3 个参数是游戏侧的 `System.Func<bool>` / `System.Func<bool,bool>`，
            // 而 net472 会把这些 Func 解析到 **mscorlib**（游戏 mscorlib 2.0 里没有）→ 直接传 lambda / 方法组会编出
            // `[mscorlib]System.Func` 并在运行期抛 MissingMethodException（`tools/check-api.ps1` 当场抓到过）。
            // 所以：按游戏侧参数的实际委托类型，用 `Delegate.CreateDelegate` 现造委托，再 `Invoke` 调用。
            if (!PrepareWidgetApi()) return;

            Widget w1 = _addBoolWidget.Invoke(menu, new object[] { TermEnabled, _getEnabled, _setEnabled, column }) as Widget;
            Widget w2 = _addBoolWidget.Invoke(menu, new object[] { TermNative, _getNative, _setNative, column }) as Widget;

            // 位置：**显式**排到该分类列表的**最前面**（作者要求"在所有选项的第一位"）。
            // `AddBoolWidget` 内部是 `InstantiateChild` → `SetParent(parent, false)`（只追加、不动 siblingIndex），
            // 所以位置不能靠创建顺序碰运气 —— 这里直接定死 0 / 1（顺序 = 启用模组 → 原生单位）。
            if (w1 != null) { w1.transform.SetSiblingIndex(0); w1.ForceUpdate(); }
            if (w2 != null) { w2.transform.SetSiblingIndex(1); w2.ForceUpdate(); }

            _wEnabled = w1;
            _wNative = w2;
            _installedOn = menu;
            Util.Log(Loc.T("[NewMode] 已把两行开关挂进原版设置界面（控制 分类末尾）：启用模组 / 原生单位可遥控"));
        }

        /// <summary>反射拿到 `AddBoolWidget` 与它那两个委托参数的类型，并现造四个委托（只做一次）。</summary>
        static bool PrepareWidgetApi()
        {
            if (!object.ReferenceEquals(_addBoolWidget, null)) return true;

            MethodInfo[] methods = typeof(GeneratedMenu).GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo mi = methods[i];
                if (!string.Equals(mi.Name, "AddBoolWidget", System.StringComparison.Ordinal)) continue;

                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length != 4) continue;                                  // (string, Func<bool>, Func<bool,bool>, Transform)

                _addBoolWidget = mi;
                _funcBool = ps[1].ParameterType;
                _funcBoolBool = ps[2].ParameterType;
                break;
            }

            if (object.ReferenceEquals(_addBoolWidget, null) || object.ReferenceEquals(_funcBool, null) ||
                object.ReferenceEquals(_funcBoolBool, null))
            {
                _failed = true;
                Util.Warn(Loc.T("[NewMode] 原版设置菜单结构不符（找不到 AddBoolWidget(string, Func<bool>, Func<bool,bool>, Transform)）→ 不挂设置项；F1 菜单与 cfg 仍可用"));
                return false;
            }

            _getEnabled = MakeDelegate(_funcBool, "GetEnabled");
            _setEnabled = MakeDelegate(_funcBoolBool, "SetEnabled");
            _getNative = MakeDelegate(_funcBool, "GetNative");
            _setNative = MakeDelegate(_funcBoolBool, "SetNative");

            if (object.ReferenceEquals(_getEnabled, null) || object.ReferenceEquals(_setEnabled, null) ||
                object.ReferenceEquals(_getNative, null) || object.ReferenceEquals(_setNative, null))
            {
                _failed = true;
                Util.Warn(Loc.T("[NewMode] 原版设置项的回调委托创建失败 → 不挂设置项；F1 菜单与 cfg 仍可用"));
                return false;
            }
            return true;
        }

        static System.Delegate MakeDelegate(System.Type delegateType, string methodName)
        {
            MethodInfo mi = typeof(VanillaSettings).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            if (object.ReferenceEquals(mi, null)) return null;
            return System.Delegate.CreateDelegate(delegateType, mi);
        }

        // ---- I2 词条注入（原版控件只认词条；缺词条 = 标签空白）----
        static void EnsureTerms()
        {
            List<LanguageSourceData> sources = LocalizationManager.Sources;
            if (sources == null) return;

            for (int i = 0; i < sources.Count; i++)
            {
                LanguageSourceData src = sources[i];
                if (src == null) continue;
                AddTerm(src, TermEnabled, TextEnabledZh, TextEnabledEn);
                AddTerm(src, TermNative, TextNativeZh, TextNativeEn);
            }
        }

        /// <summary>往一个 I2 源里加词条并写中/英文本；已存在就复用。中英都没认出来 → 整表填英文兜底。</summary>
        static void AddTerm(LanguageSourceData src, string term, string zh, string en)
        {
            TermData td = src.GetTermData(term, false);
            if (td == null) td = src.AddTerm(term);
            if (td == null) return;

            int zhIdx = src.GetLanguageIndex("Chinese (Simplified)", true, false);
            if (zhIdx < 0) zhIdx = src.GetLanguageIndex("Chinese", true, false);
            int enIdx = src.GetLanguageIndex("English", true, false);

            int n = td.Languages.Length;
            if (zhIdx >= 0 && zhIdx < n) td.Languages[zhIdx] = zh;
            if (enIdx >= 0 && enIdx < n) td.Languages[enIdx] = en;
            if ((zhIdx < 0 || zhIdx >= n) && (enIdx < 0 || enIdx >= n))
                for (int i = 0; i < n; i++) td.Languages[i] = en;

            src.UpdateDictionary(true);                       // 立刻可查（否则新词条要等下次重建字典）
        }

        // ---- 两行的取值与落值：直接读写 cfg（与原版设置、F1 菜单、cfg 文件同步）----
        static bool GetEnabled() { return Util.V(ModConfig.Enabled, true); }

        static bool SetEnabled(bool v)
        {
            if (ModConfig.Enabled != null) ModConfig.Enabled.Value = v;

            // ⚠️ **必须 `return true`**：`ValueWidget<T>.SetValueHandler` 只在 setAction 返回 true 时才认这次改动
            // （`if (setAction(newValue)) { ...; onValueChanged(newValue); }`）。返回 false 的语义是"这次设置被拒绝"
            // ⇒ 控件内部状态不更新 → 实机表现为"关掉了，但那个'开'的高亮还亮着，退出设置再进去才显示为关"。
            // 原版所有 setter 也都是 `return true`。写 cfg 不会失败，所以这里如实返回成功。
            return true;
        }

        static bool GetNative() { return Util.V(ModConfig.ControlNativeUnits, true); }

        static bool SetNative(bool v)
        {
            if (ModConfig.ControlNativeUnits != null) ModConfig.ControlNativeUnits.Value = v;
            return true;                                                       // 同上：返回"设置成功"，不是新值
        }
    }
}
