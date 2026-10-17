using System.Collections.Generic;
using System.Reflection;
using I2.Loc;
using UnityEngine;
using UnityEngine.UI;
using Voxels.TowerDefense.UI;

namespace BadNorthNewMode
{
    /// <summary>把三行开关挂进**原版设置界面**（v1.7.5 两行 + v1.7.6「船只可堆叠」）。见 PROJECT_SPEC §11.12 / §11.13。
    ///
    /// 原版结构（反编译 `BadNorthDatabase` 核实）：
    ///   `UserSettingsMenu : GeneratedMenu : UIMenu`；`GeneratedMenu.Initialize()`（由 `Awake` / `IGameSetup.OnGameAwake`
    ///   经 `MaybeInitialize()` 只调一次）里用 `AddBoolWidget(词条, getter, setter, 分类Transform)` 建出各项；
    ///   分类容器挂 `VerticalLayoutGroup` → **后加的子项会自动排版**，所以只要等它初始化完成再追加即可。
    ///
    /// 三条设计约束：
    ///   ① **不引入 Harmony / MMHOOK**（本模组一贯"零补丁"）：改为每秒轮询一次私有 `_instance` + `initialized`，
    ///      初始化完成后追加；结构不符就**一次性放弃并 Warn**，F1 菜单与 cfg 照旧可用。
    ///   ② 标签走游戏的 **I2 本地化**（原版控件只认"词条"，缺词条 = 空白）→ 运行期把三条词条注入
    ///      `LocalizationManager.Sources`（中/英各一份）并 `UpdateDictionary(true)`。
    ///   ③ 每行的 getter/setter **直接读写 cfg**（`[General] Enabled` / `[Native] ControlNativeUnits` /
    ///      `[Landing] AllowShipStack`）⇒ 原版设置、F1 菜单、cfg 文件三处天然同步，不需要任何额外状态。</summary>
    internal static class VanillaSettings
    {
        // 注入游戏本地化表的词条名（不是我们 Loc 的词条；中英文本各写一份）
        const string TermEnabled = "BADNORTHNEWMODE/SETTINGS/ENABLED";
        const string TermNative = "BADNORTHNEWMODE/SETTINGS/NATIVE";
        const string TermStack = "BADNORTHNEWMODE/SETTINGS/STACK";
        // v1.7.7：我们那个分类的**标题词条**（标题可能是"艺术字"组件而不是 uGUI Text → 换词条最稳）
        const string TermCategory = "BADNORTHNEWMODE/SETTINGS/CATEGORY";
        const string TextCategoryZh = "BadNorthNewMode 模组";
        const string TextCategoryEn = "BadNorthNewMode";
        const string TextEnabledZh = "BadNorthNewMode：启用模组";
        const string TextEnabledEn = "BadNorthNewMode: mod enabled";
        const string TextNativeZh = "原生单位：可遥控";
        const string TextNativeEn = "Native units: controllable";
        const string TextStackZh = "船只可堆叠（影响观感）";
        const string TextStackEn = "Ships may stack (visual only)";

        const float ScanInterval = 1f;         // 每秒找一次菜单；挂上后只做一次"实例还在不在"的比较

        static FieldInfo _instanceField;       // UserSettingsMenu._instance（private static）
        static FieldInfo _initializedField;    // GeneratedMenu.initialized（private bool）
        static FieldInfo _controlsField;       // UserSettingsMenu.categoryControls（private Transform）
        static FieldInfo _displayField;        // categoryDisplay（v1.7.7：克隆模板 + 分类排序）
        static FieldInfo _graphicsField;       // categoryGraphics
        static FieldInfo _audioField;          // categoryAudio
        static bool _probed;
        static bool _failed;                   // 结构不符 / 出错 → 永久放弃（不刷日志、不每帧重试）
        static UserSettingsMenu _installedOn;  // 已挂好的实例（换场景换了实例就重挂）
        static Widget _wEnabled, _wNative, _wStack;  // 我们那几行的控件（菜单开着时按原版节奏重刷显示用）
        static Transform _category;            // v1.7.7：我们克隆出来的分类容器（= 控件列表）
        static Transform _categoryTitle;       // v1.7.7：我们的标题节点（克隆原版标题，或自建 Text）
        static Transform _categoryList;        // 容器里"放控件"的那个子节点
        static Text _categoryText;             // 分类标题（语言切换时由 RefreshRows 跟着改）
        static UserSettingsMenu _categoryOwner;// 这个克隆属于哪个菜单实例（换实例必须重克隆，否则会把新控件挂到旧菜单下）
        static float _nextScan;

        static MethodInfo _addBoolWidget;      // GeneratedMenu.AddBoolWidget(string, Func<bool>, Func<bool,bool>, Transform)
        static System.Type _funcBool;          // 游戏侧的 System.Func<bool> —— ⚠️ 在 System.Core 里，**不在 mscorlib**
        static System.Type _funcBoolBool;      // 游戏侧的 System.Func<bool,bool>
        static System.Delegate _getEnabled, _setEnabled, _getNative, _setNative;   // 按游戏侧委托类型现造的委托
        static System.Delegate _getStack, _setStack;

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

        /// <summary>菜单开着时按 15 帧节奏重刷这几行：`Widget.ForceUpdate()` → `ValueWidget.onForceUpdate(GetValue())`
        /// ⇒ 读我们的 getter（cfg）重新显示。设置界面没开着就什么都不做。</summary>
        static void RefreshRows()
        {
            if (Time.frameCount % 15 != 0) return;
            if (!_installedOn.gameObject.activeInHierarchy) return;

            if (_wEnabled != null) _wEnabled.ForceUpdate();
            if (_wNative != null) _wNative.ForceUpdate();
            if (_wStack != null) _wStack.ForceUpdate();
            if (_categoryText != null) _categoryText.text = Loc.T("BadNorthNewMode 模组");   // v1.7.7：分类标题跟着语言走
        }

        static bool Probe()
        {
            if (_probed) return true;
            _probed = true;

            _instanceField = typeof(UserSettingsMenu).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            _controlsField = typeof(UserSettingsMenu).GetField("categoryControls", BindingFlags.NonPublic | BindingFlags.Instance);
            _initializedField = typeof(GeneratedMenu).GetField("initialized", BindingFlags.NonPublic | BindingFlags.Instance);
            // v1.7.7：另外三个分类字段只用于"克隆模板 + 排序"，缺了只降级（仍可塞进「控制」列）
            _displayField = typeof(UserSettingsMenu).GetField("categoryDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
            _graphicsField = typeof(UserSettingsMenu).GetField("categoryGraphics", BindingFlags.NonPublic | BindingFlags.Instance);
            _audioField = typeof(UserSettingsMenu).GetField("categoryAudio", BindingFlags.NonPublic | BindingFlags.Instance);

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

            // v1.7.7：三行放进**我们自己的分类**（标题 = "BadNorthNewMode 模组"），不再塞进「控制」列。
            // 克隆失败才退回旧行为（column），保证任何原版 UI 改版都不会让设置项消失。
            Transform list = EnsureCategory(menu);
            if (list == null)
            {
                Util.Warn(Loc.T("[NewMode] 没能建独立分类（原版结构不符）→ 三行退回「控制」列；功能不受影响"));
                list = column;
            }

            Widget w1 = _addBoolWidget.Invoke(menu, new object[] { TermEnabled, _getEnabled, _setEnabled, list }) as Widget;
            Widget w2 = _addBoolWidget.Invoke(menu, new object[] { TermNative, _getNative, _setNative, list }) as Widget;
            Widget w3 = _addBoolWidget.Invoke(menu, new object[] { TermStack, _getStack, _setStack, list }) as Widget;

            // 组内顺序：`AddBoolWidget` 内部是 `InstantiateChild` → `SetParent(parent, false)`（只追加、不动 siblingIndex），
            // 所以这里直接定死 0 / 1 / 2（顺序 = 启用模组 → 原生单位 → 船只可堆叠）。
            if (w1 != null) { w1.transform.SetSiblingIndex(0); w1.ForceUpdate(); }
            if (w2 != null) { w2.transform.SetSiblingIndex(1); w2.ForceUpdate(); }
            if (w3 != null) { w3.transform.SetSiblingIndex(2); w3.ForceUpdate(); }

            _wEnabled = w1;
            _wNative = w2;
            _wStack = w3;
            _installedOn = menu;
            Util.Log(Loc.T("[NewMode] 已把三行开关挂进原版设置界面（独立分类「BadNorthNewMode 模组」）：启用模组 / 原生单位可遥控 / 船只可堆叠"));
        }

        /// <summary>v1.7.7：在**原版设置界面**里给我们自己建一个分类（= 标题 + 控件列表），按 `[UI] SettingsCategoryIndex`
        /// 插到第 N 个标题的位置（默认 2）。
        ///
        /// 做法不是"凭空造一个面板"，而是**克隆原版一个现成分类容器** —— 字体、行距、标题样式、竖排布局、
        /// 甚至以后原版改版带来的新装饰都自动跟着走。克隆后：
        ///   ① 把"放控件"的那个子节点里的原版控件全清掉；
        ///   ② 标题的 `Localize` 停用并写入我们自己的文本（语言切换由 `RefreshRows` 跟着改）；
        ///   ③ 按原版四个分类的**层级顺序**决定插到谁前面（不依赖写死的场景路径）。
        /// 任何一步失败都返回 null —— 调用方退回"塞进控制列"的旧行为。</summary>
        static Transform EnsureCategory(UserSettingsMenu menu)
        {
            if (_categoryList != null && object.ReferenceEquals(_categoryOwner, menu)) return _categoryList;   // 同一菜单已建且还活着 → 复用

            // 换菜单实例：旧克隆不再需要（它挂在旧菜单下，重复挂会把新控件塞到旧菜单里）
            if (_categoryTitle != null) UnityEngine.Object.Destroy(_categoryTitle.gameObject);
            if (_category != null) UnityEngine.Object.Destroy(_category.gameObject);
            _categoryTitle = null;
            _category = null;
            _categoryList = null;
            _categoryText = null;
            _categoryOwner = null;

            Transform display = ReadColumn(_displayField, menu);
            Transform controls = ReadColumn(_controlsField, menu);
            Transform graphics = ReadColumn(_graphicsField, menu);
            Transform audio = ReadColumn(_audioField, menu);

            // 克隆模板：优先「显示」分类（它一定有一个标题 + 一个列表子节点）
            Transform src = null;
            Transform srcList = null;
            if (display != null && display.parent != null) { src = display.parent; srcList = display; }
            else if (controls != null && controls.parent != null) { src = controls.parent; srcList = controls; }
            if (src == null || srcList == null) return null;

            List<Transform> containers = new List<Transform>();
            AddContainer(containers, display);
            AddContainer(containers, controls);
            AddContainer(containers, graphics);
            AddContainer(containers, audio);
            if (containers.Count == 0) return null;

            containers.Sort(CompareHierarchy);

            int index = Mathf.Clamp(Util.V(ModConfig.SettingsCategoryIndex, 2), 1, containers.Count + 1);
            Transform anchor = (index - 1 < containers.Count) ? containers[index - 1] : null;
            Transform parent = (anchor != null) ? anchor.parent : containers[containers.Count - 1].parent;
            if (parent == null) return null;

            // v1.7.7 三次修订（实机：三行跑到了「图像」标题**下面**）：
            //   原版每个分类 = "标题块（标题 + 装饰/留白若干节点）" + "分类容器"，
            //   标题与容器之间还夹着装饰节点 ⇒ "紧邻的前一个兄弟"不一定就是标题。
            //   把它当标题，插入点就落在标题**之后**（正是实机看到的错位）。
            //   改为插到**锚点标题块的最前面**（= 上一个分类容器之后的第一个节点），见 FindInsertIndex。
            int at;
            Transform titleSrc;
            at = FindInsertIndex(parent, anchor, containers, out titleSrc);
            int listIndex = srcList.GetSiblingIndex();

            // 原版行标签的字体/字号/行高：自建标题按它对齐（实机反馈"自建标题比原版标题小一号"）
            Font labelFont; int labelSize; float labelHeight;
            SampleListStyle(srcList, out labelFont, out labelSize, out labelHeight);

            Transform clone = null;
            try
            {
                GameObject go = UnityEngine.Object.Instantiate(src.gameObject) as GameObject;
                if (go != null) clone = go.transform;
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 克隆原版设置分类失败（{0}）→ 三行退回「控制」列", e.Message));
                return null;
            }
            if (clone == null) return null;

            Transform list = (listIndex >= 0 && listIndex < clone.childCount) ? clone.GetChild(listIndex) : null;
            if (list == null) { UnityEngine.Object.Destroy(clone.gameObject); return null; }

            // 清掉克隆时带过来的原版控件（这一刻设置菜单还没显示，Destroy 延迟到帧末也无妨）
            for (int i = list.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(list.GetChild(i).gameObject);

            clone.name = "BadNorthNewMode.SettingsCategory";
            clone.SetParent(parent, false);
            clone.SetSiblingIndex(at);

            // 标题：① 原版标题节点**可改写**（有 Localize/Text）才克隆并换词条（保留原样式）
            //       → ② 否则在容器内找 → ③ 都没有就自建 Text（按原版标题的高度/观感对齐）
            bool titled = false;
            bool titleClonable = (titleSrc != null) &&
                                 (titleSrc.GetComponentInChildren<Localize>(true) != null ||
                                  titleSrc.GetComponentInChildren<Text>(true) != null);
            if (titleClonable)
            {
                Transform titleClone = null;
                try
                {
                    GameObject tgo = UnityEngine.Object.Instantiate(titleSrc.gameObject) as GameObject;
                    if (tgo != null) titleClone = tgo.transform;
                }
                catch (System.Exception) { titleClone = null; }

                if (titleClone != null)
                {
                    titleClone.name = "BadNorthNewMode.SettingsTitle";
                    titleClone.SetParent(parent, false);
                    titleClone.SetSiblingIndex(at);        // 插到我们的容器前面
                    titled = ApplyCategoryTitle(titleClone, null);
                    if (titled) _categoryTitle = titleClone;
                    else UnityEngine.Object.Destroy(titleClone.gameObject);
                }
            }

            if (!titled) titled = ApplyCategoryTitle(clone, list);     // 有些结构把标题放在容器里

            if (!titled)
            {
                _categoryTitle = CreateTitleNode(parent, at, menu, titleSrc, labelFont, labelSize, labelHeight);
                try { Util.Log(Loc.F("[NewMode][UI] 设置分类结构（标题不是 Localize/Text，已自建标题）：{0}", DescribeTree(clone, list))); }
                catch (System.Exception) { }   // silent-ok: 诊断日志失败不影响功能
            }

            _category = clone;
            _categoryList = list;
            _categoryOwner = menu;

            // v1.7.7 三次修订：把"到底插在哪"打成一行（下次实机如果还错位，这一行就能定案）
            try
            {
                Util.Log(Loc.F("[NewMode][UI] 分类定位：锚点='{0}'@{1} · 标题参照='{2}'{3} · 插入={4}（父='{5}' 共{6}个子）",
                    anchor != null ? anchor.name : "-", anchor != null ? anchor.GetSiblingIndex() : -1,
                    titleSrc != null ? titleSrc.name : "-", DescribeTitleRef(titleSrc), at,
                    parent.name, parent.childCount));
                Util.Log(Loc.F("[NewMode][UI] 设置分类层级：{0}", DescribeLevel(parent, anchor, containers, at)));
            }
            catch (System.Exception) { }       // silent-ok: 诊断日志失败不影响功能

            Util.Log(Loc.T("[NewMode] 已在原版设置里建好独立分类「BadNorthNewMode 模组」（标题 + 三行开关；按层级顺序插在第 N 个标题位置）"));
            return list;
        }

        /// <summary>决定"插到哪" + 顺手挑一个标题节点当样式参照。
        ///
        /// v1.7.7 三次修订（实机事故：三行开关跑到「图像」标题下面）：
        /// 原版每个分类 = **标题块**（标题 + 装饰/留白若干节点）+ **分类容器**，标题与容器之间还夹着
        /// 装饰节点 ⇒ "紧邻的前一个兄弟"并不一定是标题；把它当标题，插入点就落在标题**之后**。
        /// 这里改为：从锚点容器往前退，退到"上一个分类容器之后的第一个节点"（= 锚点标题块的最前面），
        /// 这样我们的整块一定排在锚点标题**之前** ⇒ 成为第 N 个标题。
        ///
        /// `titleSrc` 只用于取样式（颜色/字号/行高/左边距），**不参与定位**；挑"标题块里最高的那个"
        /// （标题比装饰条高），拿不到就返回 null，调用方按行标签的字体兜底。</summary>
        static int FindInsertIndex(Transform parent, Transform anchor, List<Transform> containers, out Transform titleSrc)
        {
            titleSrc = null;
            if (parent == null) return 0;

            int ai = (anchor != null) ? anchor.GetSiblingIndex() : parent.childCount;

            int prev = -1;                                              // 同父下、锚点前面最近的分类容器
            for (int i = ai - 1; i >= 0; i--)
            {
                if (IsCategoryContainer(parent.GetChild(i), containers)) { prev = i; break; }
            }
            int start = (prev >= 0) ? prev + 1 : 0;                      // 锚点标题块的第一节点

            float best = 0f;
            for (int i = start; i < ai; i++)
            {
                Transform c = parent.GetChild(i);
                float h = NodeHeight(c);
                if (h > best + 0.5f) { best = h; titleSrc = c; }
            }
            return start;
        }

        static bool IsCategoryContainer(Transform t, List<Transform> containers)
        {
            for (int i = 0; i < containers.Count; i++)
                if (object.ReferenceEquals(containers[i], t)) return true;
            return false;
        }

        /// <summary>节点当前高度（布局已跑过才有值；量不到返回 0）。</summary>
        static float NodeHeight(Transform t)
        {
            RectTransform rt = (t != null) ? t as RectTransform : null;
            if (rt == null) return 0f;
            float h = rt.rect.height;
            if (h < 1f) h = Mathf.Abs(rt.sizeDelta.y);
            return h;
        }

        /// <summary>量一行原版开关标签的字体 / 字号 / 行高（自建标题按它对齐字号）。</summary>
        static void SampleListStyle(Transform list, out Font font, out int size, out float height)
        {
            font = null;
            size = 0;
            height = 0f;
            try
            {
                if (list == null) return;
                Text t = list.GetComponentInChildren<Text>(true);
                if (t == null) return;
                font = t.font;
                size = t.fontSize;
                height = NodeHeight(t.transform);
            }
            catch (System.Exception) { }        // silent-ok: 量不到就用调用方的默认值
        }

        static string ComponentNames(Transform t)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            UnityEngine.Component[] cs = t.GetComponents<UnityEngine.Component>();
            for (int i = 0; i < cs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append((cs[i] != null) ? cs[i].GetType().Name : "null");
            }
            return sb.ToString();
        }

        /// <summary>标题参照的组件与尺寸（诊断用，一行）。</summary>
        static string DescribeTitleRef(Transform t)
        {
            if (t == null) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("'{").Append(ComponentNames(t)).Append('}');
            RectTransform rt = t as RectTransform;
            if (rt != null)
                sb.Append(" h=").Append(Mathf.RoundToInt(rt.rect.height)).Append(" w=").Append(Mathf.RoundToInt(rt.rect.width));
            return sb.ToString();
        }

        /// <summary>父节点下每个直接子节点（名称 + 组件 + 是否分类容器/锚点/插入点）；每次安装只打一行，便于实机定案。</summary>
        static string DescribeLevel(Transform parent, Transform anchor, List<Transform> containers, int at)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("父='").Append(parent.name).Append("' 共 ").Append(parent.childCount).Append(" 个：");
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (i > 0) sb.Append(" · ");
                sb.Append(i).Append(':').Append(c.name).Append('{').Append(ComponentNames(c)).Append('}');
                if (IsCategoryContainer(c, containers)) sb.Append("[分类容器]");
                if (object.ReferenceEquals(c, anchor)) sb.Append("[锚点]");
                if (i == at) sb.Append("[插入点]");
            }
            return sb.ToString();
        }

        /// <summary>给我们的分类做标题。
        /// 用于"原版标题既不是 Localize 也不是 Text"的情况（坏北的标题是**场景里的美术节点**，改不了文字）。
        ///
        /// v1.7.7 三次修订（两处实机反馈）：① 自建标题比原版标题小一号、颜色偏亮；② 左边距对不上。
        /// 做法改为：**先克隆原版标题节点**（矩形/锚点/左边距/行高/布局元素全部照抄），把它原本的美术层
        /// （`Graphic` / `Renderer`）整片关掉，再在同一个矩形上放我们自己的 `Text`：</summary>
        static Transform CreateTitleNode(Transform parent, int at, UserSettingsMenu menu, Transform likeTitle,
                                         Font labelFont, int labelSize, float labelHeight)
        {
            Transform node = null;
            try
            {
                if (likeTitle != null)
                {
                    GameObject tgo = UnityEngine.Object.Instantiate(likeTitle.gameObject) as GameObject;
                    if (tgo != null) node = tgo.transform;
                }
            }
            catch (System.Exception) { node = null; }        // silent-ok: 克隆不了就退回裸节点

            if (node != null)
            {
                node.name = "BadNorthNewMode.SettingsTitle";
                node.SetParent(parent, false);
                node.SetSiblingIndex(at);
                HideArt(node);                              // 只留形状/留白，不要原版标题的图
            }
            else
            {
                GameObject go = new GameObject("BadNorthNewMode.SettingsTitle");
                go.transform.SetParent(parent, false);
                go.transform.SetSiblingIndex(at);
                node = go.transform;
            }

            try
            {
                _categoryText = PutTitleText(node, menu, labelFont, labelSize, likeTitle, labelHeight);
                return node;
            }
            catch (System.Exception e)
            {
                Util.Warn(Loc.F("[NewMode] 自建设置分类标题失败（{0}）", e.Message));
                if (node != null) UnityEngine.Object.Destroy(node.gameObject);
                return null;
            }
        }

        /// <summary>把克隆体里"画东西"的组件关掉（只关渲染、不动 RectTransform ⇒ 行高/留白保持原样）。</summary>
        static void HideArt(Transform node)
        {
            Graphic[] gs = node.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < gs.Length; i++)
            {
                if (gs[i] == null) continue;
                if (gs[i] is Text) continue;                // 万一原版用的是 Text，留给调用方改写
                gs[i].enabled = false;
            }

            // 极少见但存在：美术节点用 SpriteRenderer / MeshRenderer 而不是 uGUI Graphic
            Renderer[] rs = node.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
                if (rs[i] != null) rs[i].enabled = false;
        }

        /// <summary>把标题文字放到 `node` 上（有 Graphic 时退到子节点，避免两个 Graphic 抢同一个 CanvasRenderer）。</summary>
        static Text PutTitleText(Transform node, UserSettingsMenu menu, Font labelFont, int labelSize,
                                 Transform likeTitle, float labelHeight)
        {
            Transform host = node;
            if (node.GetComponent<Graphic>() != null)
            {
                GameObject go = new GameObject("Text");
                RectTransform rt = go.AddComponent<RectTransform>();
                rt.SetParent(node, false);
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                host = go.transform;
            }

            Text t = host.GetComponent<Text>();
            if (t == null) t = host.gameObject.AddComponent<Text>();

            t.font = (labelFont != null) ? labelFont : BorrowFont(menu);
            t.fontSize = TitleFontSize(labelSize, likeTitle, labelHeight);
            t.fontStyle = FontStyle.Normal;                       // 原版标题不是粗体
            t.color = TitleColor(likeTitle);
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = Loc.T("BadNorthNewMode 模组");

            // 行高兜底：克隆到的可能是"标题块里的装饰条"（很薄），字会被上下挤住 ⇒ 不够高就补到
            // 字号 × 1.3（裸节点更是必须给高度，否则竖排布局会把它挤成 0）。
            float wantH = Mathf.Clamp(t.fontSize * 1.3f, 22f, 56f);
            if (NodeHeight(node) < wantH - 0.5f)
            {
                LayoutElement le = node.GetComponent<LayoutElement>();
                if (le == null) le = node.gameObject.AddComponent<LayoutElement>();
                le.minHeight = wantH;
                le.preferredHeight = wantH;
            }
            return t;
        }

        /// <summary>标题字号：优先照抄原版开关行的字号（实机对比：两者几乎同号）；量不到才按高度估。</summary>
        static int TitleFontSize(int labelSize, Transform likeTitle, float labelHeight)
        {
            int size = labelSize;
            if (size <= 0)
            {
                float h = NodeHeight(likeTitle);
                if (h < 8f) h = (labelHeight > 8f) ? labelHeight : 30f;
                size = Mathf.RoundToInt(h * 0.72f);
            }
            return Mathf.Clamp(size, 14, 40);
        }

        /// <summary>标题颜色：原版标题本身是染色的（max 通道 &lt; 0.85）就照抄；否则用同观感的暖灰
        /// （艺术字常见"白染 + 灰色图"，直接取到的是白色，用它会比原版亮一档）。</summary>
        static Color TitleColor(Transform likeTitle)
        {
            try
            {
                if (likeTitle != null)
                {
                    Graphic g = likeTitle.GetComponent<Graphic>();
                    if (g == null) g = likeTitle.GetComponentInChildren<Graphic>(true);
                    if (g != null)
                    {
                        Color c = g.color;
                        if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.85f) return new Color(c.r, c.g, c.b, 1f);
                    }
                }
            }
            catch (System.Exception) { }        // silent-ok: 取不到颜色就用常量
            return new Color(0.45f, 0.44f, 0.39f, 1f);
        }


        static Font BorrowFont(UserSettingsMenu menu)
        {
            try
            {
                Text t = (menu != null) ? menu.GetComponentInChildren<Text>(true) : null;
                return (t != null) ? t.font : null;
            }
            catch (System.Exception) { return null; }
        }

        static Transform ReadColumn(FieldInfo f, UserSettingsMenu menu)
        {
            if (object.ReferenceEquals(f, null)) return null;
            try { return f.GetValue(menu) as Transform; }
            catch (System.Exception) { return null; }
        }

        /// <summary>把分类列表（`categoryXxx`）的父节点加进候选表（去重）。</summary>
        static void AddContainer(List<Transform> list, Transform column)
        {
            if (column == null || column.parent == null) return;
            Transform c = column.parent;
            for (int i = 0; i < list.Count; i++)
                if (object.ReferenceEquals(list[i], c)) return;
            list.Add(c);
        }

        /// <summary>按**场景层级顺序**比较两个分类容器（同层比 siblingIndex；跨列比它们共同祖先下的顺序）。</summary>
        static int CompareHierarchy(Transform a, Transform b)
        {
            int da = Depth(a);
            int db = Depth(b);
            Transform x = a;
            Transform y = b;

            while (da > db) { x = x.parent; da--; }
            while (db > da) { y = y.parent; db--; }
            while (!object.ReferenceEquals(x.parent, y.parent))
            {
                if (x.parent == null || y.parent == null) return 0;
                x = x.parent;
                y = y.parent;
            }
            if (object.ReferenceEquals(x, y)) return 0;
            return x.GetSiblingIndex().CompareTo(y.GetSiblingIndex());
        }

        static int Depth(Transform t)
        {
            int d = 0;
            while (t.parent != null) { t = t.parent; d++; }
            return d;
        }

        /// <summary>在克隆里找标题用的 I2 词条组件（容器自身或"非列表"子树）。</summary>
        static Localize FindTitleLocalize(Transform clone, Transform list)
        {
            Localize own = clone.GetComponent<Localize>();
            if (own != null) return own;

            for (int i = 0; i < clone.childCount; i++)
            {
                Transform c = clone.GetChild(i);
                if (object.ReferenceEquals(c, list)) continue;
                Localize lz = c.GetComponentInChildren<Localize>(true);
                if (lz != null) return lz;
            }
            return null;
        }

        static Text FindTitleText(Transform clone, Transform list)
        {
            Text own = clone.GetComponent<Text>();
            if (own != null) return own;

            for (int i = 0; i < clone.childCount; i++)
            {
                Transform c = clone.GetChild(i);
                if (object.ReferenceEquals(c, list)) continue;
                Text t = c.GetComponentInChildren<Text>(true);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>v1.7.7（修订）：尝试把 `node` 的标题换成我们的。返回是否成功。
        /// 顺序：① 换 I2 词条（`Localize` 会刷新它渲染的目标，艺术字也适用）→ ② 写 `Text`。
        /// `list` 非空时跳过"列表"子树（避免误改控件标签）。</summary>
        static bool ApplyCategoryTitle(Transform node, Transform list)
        {
            if (node == null) return false;

            Localize lz = FindTitleLocalize(node, list);
            if (lz != null)
            {
                lz.Term = TermCategory;
                lz.enabled = false;
                lz.enabled = true;          // 触发一次重新本地化（组件在未激活菜单里时，等打开时 OnEnable 也会再刷）
                _categoryText = null;
                return true;
            }

            Text txt = FindTitleText(node, list);
            if (txt != null)
            {
                txt.text = Loc.T("BadNorthNewMode 模组");
                _categoryText = txt;
                return true;
            }
            return false;
        }

        /// <summary>只读诊断：容器自身 + 每个子节点的组件名（含 Localize 的词条），一行打完。</summary>
        static string DescribeTree(Transform clone, Transform list)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[NewMode][UI] 设置分类结构：");
            AppendNode(sb, clone, "容器", false);
            for (int i = 0; i < clone.childCount; i++)
            {
                Transform c = clone.GetChild(i);
                sb.Append(" · ");
                AppendNode(sb, c, "子" + i, object.ReferenceEquals(c, list));
            }
            return sb.ToString();
        }

        static void AppendNode(System.Text.StringBuilder sb, Transform t, string tag, bool isList)
        {
            sb.Append(tag).Append("'").Append(t.name).Append("'").Append(isList ? "(列表)" : "").Append('{');
            UnityEngine.Component[] cs = t.GetComponents<UnityEngine.Component>();
            for (int i = 0; i < cs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                UnityEngine.Component c = cs[i];
                sb.Append((c != null) ? c.GetType().Name : "null");

                Localize lz = c as Localize;
                if (lz != null) sb.Append("(Term=").Append(lz.Term).Append(')');
            }
            sb.Append('}');
        }


        /// <summary>反射拿到 `AddBoolWidget` 与它那两个委托参数的类型，并现造六个委托（只做一次）。</summary>
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
            _getStack = MakeDelegate(_funcBool, "GetStack");
            _setStack = MakeDelegate(_funcBoolBool, "SetStack");

            if (object.ReferenceEquals(_getEnabled, null) || object.ReferenceEquals(_setEnabled, null) ||
                object.ReferenceEquals(_getNative, null) || object.ReferenceEquals(_setNative, null) ||
                object.ReferenceEquals(_getStack, null) || object.ReferenceEquals(_setStack, null))
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
                AddTerm(src, TermStack, TextStackZh, TextStackEn);
                AddTerm(src, TermCategory, TextCategoryZh, TextCategoryEn);
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

        // ---- 各行的取值与落值：直接读写 cfg（与原版设置、F1 菜单、cfg 文件同步）----
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

        static bool GetStack() { return Util.V(ModConfig.AllowShipStack, false); }

        static bool SetStack(bool v)
        {
            bool changed = (Util.V(ModConfig.AllowShipStack, false) != v);
            if (ModConfig.AllowShipStack != null) ModConfig.AllowShipStack.Value = v;

            // 只在真的变了时打一行：这行日志是"玩家说船叠起来了，是不是我们干的"的第一手证据
            //（只影响**之后**的投放，已经在场的船不会被挪动）。
            if (changed) Util.Log(Loc.T("[NewMode][投放] 船只堆叠已切换（只影响之后的投放；见原版设置 → 控制）"));
            return true;                                                       // 同上：返回"设置成功"，不是新值
        }

        /// <summary>v1.7.6：插件卸载时把三行从原版设置里摘掉（热重载后不会出现两份，见 `SessionCleanup` / §11.14）。
        /// 关总开关**不**调 —— 那三行是"再打开模组"的逃生门，必须留着。</summary>
        internal static void DestroyWidgets()
        {
            DestroyWidget(_wEnabled);
            DestroyWidget(_wNative);
            DestroyWidget(_wStack);
            _wEnabled = null;
            _wNative = null;
            _wStack = null;

            // v1.7.7：整个分类容器一起销毁（三行是它的子节点，重复 Destroy 无副作用）；标题可能是单独节点
            if (_categoryTitle != null) UnityEngine.Object.Destroy(_categoryTitle.gameObject);
            if (_category != null) UnityEngine.Object.Destroy(_category.gameObject);
            _categoryTitle = null;
            _category = null;
            _categoryList = null;
            _categoryText = null;
            _categoryOwner = null;

            _installedOn = null;
        }

        static void DestroyWidget(Widget w)
        {
            if (w != null) UnityEngine.Object.Destroy(w.gameObject);
        }
    }
}
