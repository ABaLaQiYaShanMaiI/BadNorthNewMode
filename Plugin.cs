using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>
    /// 战局内按热键进入投放模式 → 点击【滩头陆地】→ 该处按原版流程来一艘敌舰。
    /// 输入完全对齐原版：订阅 IslandGameplayManager.pointerRationalizer.onClick（游戏自己的世界点击事件），
    /// 再用 NavSpot.NavSpotCast(screenPos, out hit) 换算地面点——与 Navigator / ConfirmButton 同一套。
    /// 无 Harmony 补丁。
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "badnorth.newmode";
        public const string NAME = "Bad North - New Mode";
        public const string VERSION = "1.2.2";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        bool _armed;
        bool _subscribed;
        bool _subscribeFailed;
        string _hud = "";
        string _hover = "";
        float _hudUntil;

        // ---- 投放菜单（F1 唤起；左键点菜单选兵种）----
        bool _menuOpen;
        List<VikingReference> _menuUnits;
        Island _menuIsland;
        Rect _menuRect;

        void Awake()
        {
            Instance = this;
            Log = Logger;
            try { ModConfig.Bind(Config); }
            catch (System.Exception e)
            {
                // 配置绑定失败不再让整个插件在 Awake 抛异常（否则游戏里完全无反馈），只报错后降级。
                Log.LogError("[NewMode] 配置绑定失败：" + e);
            }

            ConfigEntry<KeyboardShortcut> hk = ModConfig.Hotkey;
            Log.LogInfo(string.Format("[NewMode] v{0} 已加载：{1} 开关投放模式，点击滩头陆地投放敌舰。",
                VERSION, (hk != null) ? hk.Value.ToString() : "(热键未绑定)"));
        }

        void Update()
        {
            if (ModConfig.Hotkey == null) return;

            TrySubscribeGameClick();

            if (ModConfig.Hotkey.Value.IsDown())
            {
                _menuOpen = !_menuOpen;
                _armed = _menuOpen;                       // 菜单即投放模式
                _hover = "";
                PlacementMarker.Get().Hide();
                Say(_menuOpen ? "投放菜单：左键点兵种选择，再点滩头陆地投放（F1 关闭 / F2 强制清场）" : "已关闭投放菜单");
                Log.LogInfo("[NewMode] " + _hud);
                if (!_menuOpen) return;
            }
            if (!_armed) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                _armed = false; _menuOpen = false; _hover = "";
                PlacementMarker.Get().Hide();
                Say("已取消投放");
                return;
            }

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            string why;
            if (!InBattle(gm, out why)) { PlacementMarker.Get().Hide(); _hover = why; return; }

            EnsureMenuUnits(gm.island);

            // ---- 悬停预览：实时把鼠标下的地形算一遍（点击本身由游戏事件负责）----
            Vector2 screenPos = Input.mousePosition;
            if (PointerInMenu(screenPos)) { PlacementMarker.Get().Hide(); _hover = "（指针在菜单上）"; return; }
            Vector3 land;
            string diag;
            bool hasLand = TryGetLandPoint(gm.island, screenPos, out land, out diag);

            DropTarget target = default(DropTarget);
            string reason = null;
            bool ok = hasLand && LandingInjector.TryResolve(gm.island, land, out target, out reason);

            if (ModConfig.ShowHoverPreview.Value)
            {
                if (!hasLand) PlacementMarker.Get().Hide();
                else PlacementMarker.Get().Show(ok ? target.beach.navPos.pos : land, ok, 0.35f);
            }

            _hover = !hasLand
                ? "指针不在陆地上（" + diag + "）"
                : (ok ? string.Format("滩头可用：落差 {0:F2}m，距点击处 {1:F1}m",
                        target.beach.navPos.pos.y - ModConfig.WaterLevelY.Value, target.shoreDist)
                      : reason);

            // 兜底：万一没订阅上游戏的点击事件就用轮询（订阅成功则完全交给事件，避免一次点击投两艘）
            if (!_subscribed && Input.GetMouseButtonDown(0))
                DoDrop(gm, screenPos, "轮询兜底");
        }

        /// <summary>
        /// 订阅原版世界点击事件 pointerRationalizer.onClick。
        /// 必须用反射：该事件的类型是 <c>System.Core 3.5</c> 里的 <c>System.Action`2</c>，
        /// 而 net472 编译时 C# 会把它绑到 mscorlib（.NET 4.x 才把 Action`2 移进 mscorlib），
        /// 游戏运行时的 mscorlib 2.0 没有这个类型 → 直接写 += 会 MissingMethodException。
        /// 反射用运行时自己的那个委托类型来造委托，我们的元数据里完全不引用它。
        /// </summary>
        void TrySubscribeGameClick()
        {
            if (_subscribed || _subscribeFailed) return;
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            if (gm == null) return;
            PointerRationalizer pr = gm.pointerRationalizer;
            if (pr == null) return;

            try
            {
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                System.Reflection.EventInfo ev = pr.GetType().GetEvent("onClick");
                System.Reflection.MethodInfo mi = typeof(Plugin).GetMethod("OnWorldClick", flags);
                // 注意：不能用 == null —— MemberInfo/EventInfo 的 op_Equality 是 .NET 4.0 才有的，
                // 游戏 mscorlib 2.0 没有（ReferenceEquals 走 object，安全）。
                if (object.ReferenceEquals(ev, null) || object.ReferenceEquals(mi, null))
                {
                    _subscribeFailed = true;
                    Log.LogWarning("[NewMode] 找不到 onClick 事件或回调方法 → 改用轮询兜底。");
                    return;
                }

                System.Delegate d = System.Delegate.CreateDelegate(ev.EventHandlerType, this, mi);
                ev.AddEventHandler(pr, d);
                _subscribed = true;
                Log.LogInfo("[NewMode] 已反射订阅原版世界点击事件 pointerRationalizer.onClick（同 Navigator/ConfirmButton 的数据源）。");
            }
            catch (System.Exception e)
            {
                _subscribeFailed = true;
                Log.LogWarning("[NewMode] 订阅 onClick 失败：" + e.Message + " → 改用轮询兜底。");
            }
        }

        /// <summary>原版世界点击回调（在游戏的事件分发里执行，必须自吞异常，不能污染游戏输入链）。</summary>
        void OnWorldClick(PointerEventData.InputButton button, Vector2 screenPos)
        {
            try
            {
                if (!_armed) return;
                if (button != PointerEventData.InputButton.Left) return;
                if (PointerInMenu(screenPos))                                  // 菜单内的左键属于选兵种，不做投放
                {
                    if (ModConfig.VerboseLog.Value && Log != null) Log.LogInfo("[NewMode] 菜单内点击 → 忽略投放");
                    return;
                }

                IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
                string why;
                if (!InBattle(gm, out why)) { Log.LogWarning("[NewMode][点击] " + why); return; }

                DoDrop(gm, screenPos, "游戏事件");
            }
            catch (System.Exception e)
            {
                if (Log != null) Log.LogError("[NewMode][点击] 处理异常：" + e);
            }
        }

        /// <summary>一次投放尝试：全程打日志（排查"点了没反应"的唯一依据）。</summary>
        void DoDrop(IslandGameplayManager gm, Vector2 screenPos, string source)
        {
            Log.LogInfo(string.Format("[NewMode][点击] 屏幕 ({0:F0},{1:F0}) 来源={2}", screenPos.x, screenPos.y, source));

            Vector3 land;
            string diag;
            if (!TryGetLandPoint(gm.island, screenPos, out land, out diag))
            {
                Log.LogWarning("[NewMode][点击] 地形未命中：" + diag);
                Say("这一点不是陆地地块：" + diag);
                return;
            }
            Log.LogInfo(string.Format("[NewMode][点击] 命中地形：{0}，点 {1}，海拔 {2:F2}m",
                diag, Fmt(land), land.y - ModConfig.WaterLevelY.Value));

            DropTarget t;
            string reason;
            if (!LandingInjector.TryResolve(gm.island, land, out t, out reason))
            {
                Log.LogWarning("[NewMode][点击] 无法投放：" + reason);
                Say("无法投放：" + reason);
                return;
            }

            string info;
            if (LandingInjector.TrySpawn(gm.island, land, out info))
            {
                Log.LogInfo("[NewMode][点击] " + info);
                Say(info);
            }
            else
            {
                Log.LogWarning("[NewMode][点击] 投放失败：" + info);
                Say("投放失败：" + info);
            }
        }

        /// <summary>只在"正常战局、岛屿处于 Playing"时可投放。</summary>
        static bool InBattle(IslandGameplayManager gm, out string why)
        {
            why = null;
            if (gm == null) { why = "不在战局中"; return false; }
            Island island = gm.island;
            if (island == null || !island.generated) { why = "岛屿未就绪"; return false; }
            if (island.state != Island.State.Playing) { why = "岛屿未进入 Playing 状态"; return false; }
            if (island.raid == null) { why = "Raid 未就绪"; return false; }
            return true;
        }

        /// <summary>
        /// 屏幕坐标 → 地面点，并带回诊断文本。
        /// ① 首选原版路径 NavSpotter.NavSpotCast（Navigator / ConfirmButton 同款：
        ///    内部 ViewportPointToRay(归一化坐标) + "Voxels"/"Modules" 层，out hit 即地面命中）；
        /// ② 兜底：同一套归一化 viewport 射线 × 原版 "Voxels" 层；再不行不限层。
        /// </summary>
        static bool TryGetLandPoint(Island island, Vector2 screenPos, out Vector3 point, out string diag)
        {
            point = Vector3.zero;
            diag = null;

            if (island != null && island.navSpotter != null)
            {
                try
                {
                    RaycastHit hit;
                    island.navSpotter.NavSpotCast(screenPos, out hit);
                    if (hit.collider != null)
                    {
                        point = hit.point;
                        diag = "NavSpotCast 命中 " + HitName(hit);
                        return true;
                    }
                    diag = "NavSpotCast 未命中";
                }
                catch (System.Exception e)
                {
                    diag = "NavSpotCast 异常 " + e.GetType().Name;
                }
            }
            else
            {
                diag = "navSpotter 不可用";
            }

            LevelCamera lc = Singleton<LevelCamera>.instance;
            Camera cam = (lc != null) ? lc.cameraRef : Camera.main;
            if (cam == null) return false;

            Vector2 vp = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
            Ray ray = cam.ViewportPointToRay(vp);
            int mask = LayerMaster.voxelMask.value;
            RaycastHit h;
            if (mask != 0 && Physics.Raycast(ray, out h, 500f, mask))
            {
                point = h.point;
                diag += "；Voxels 层命中 " + HitName(h);
                return true;
            }
            if (Physics.Raycast(ray, out h, 500f))
            {
                point = h.point;
                diag += "；任意碰撞体命中 " + HitName(h);
                return true;
            }
            diag += string.Format("；射线仍未命中（mask={0}，vp={1:F3},{2:F3}）", mask, vp.x, vp.y);
            return false;
        }

        static string HitName(RaycastHit hit)
        {
            GameObject go = hit.collider.gameObject;
            string layer = LayerMask.LayerToName(go.layer);
            return go.name + "@" + (string.IsNullOrEmpty(layer) ? go.layer.ToString() : layer);
        }

        /// <summary>HUD + 投放菜单（纯 IMGUI，零资源；菜单矩形用于屏蔽"点菜单被当成投放"）。</summary>
        void OnGUI()
        {
            DrawHud();
            if (_menuOpen) DrawMenu();
        }

        void DrawHud()
        {
            if (!ModConfig.ShowHud.Value) return;
            if (!_menuOpen && Time.unscaledTime > _hudUntil) return;

            string text = _menuOpen
                ? "BadNorthNewMode · 投放菜单（左键点兵种 → 再点滩头陆地投放；右键或 Esc 关闭）"
                : "BadNorthNewMode";
            if (_menuOpen && !string.IsNullOrEmpty(_hover)) text += "\n" + _hover;
            if (!string.IsNullOrEmpty(_hud)) text += "\n" + _hud;

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(8f, 8f, 640f, 66f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16f, 12f, 640f, 62f), text);
        }

        /// <summary>兵种菜单：上排选兵种、下排选数量，均为左键点击（写回 cfg，立即生效）。</summary>
        void DrawMenu()
        {
            int n = (_menuUnits != null) ? _menuUnits.Count : 0;
            float rowH = 26f;
            float headH = 74f;                                   // 标题 + 当前 + 数量信息
            float rows = Mathf.Max(1, n);
            float countH = 50f;                                  // "数量（…）" + 按钮行
            _menuRect = new Rect(8f, 82f, 470f, headH + rows * rowH + countH + 40f);

            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(_menuRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float x = _menuRect.x + 10f;
            float w = _menuRect.width - 20f;

            GUI.Label(new Rect(x, _menuRect.y + 6f, w, 20f), "兵种选择（按难度递增，左键点选；括号内为 cfg 内部名）");

            string cur = (ModConfig.EnemyName != null) ? ModConfig.EnemyName.Value : "";
            GUI.Label(new Rect(x, _menuRect.y + 26f, w, 20f),
                "当前：" + (string.IsNullOrEmpty(cur) ? "随机" : UnitNames.Of(cur) + "（" + cur + "）"));

            int curSize = (ModConfig.SquadSize != null) ? ModConfig.SquadSize.Value : 0;
            VikingReference sel = SelectedUnit();
            string sizeInfo;
            if (sel != null && _menuIsland != null)
            {
                int def = LandingInjector.DefaultSquadSize(_menuIsland, sel);
                int cap = LandingInjector.MaxSquadSize(_menuIsland, sel);
                sizeInfo = string.Format("数量：{0}（本兵种默认 {1}，上限 {2}）",
                    (curSize > 0) ? curSize.ToString() : "默认 " + def, def, cap);
            }
            else
            {
                sizeInfo = "数量：" + ((curSize > 0) ? curSize.ToString() : "默认（按兵种原版算法）");
            }
            GUI.Label(new Rect(x, _menuRect.y + 46f, w, 20f), sizeInfo);

            if (n == 0)
            {
                GUI.Label(new Rect(x, _menuRect.y + headH + 2f, w, 20f), "（进入战局后才会列出可用兵种）");
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    VikingReference u = _menuUnits[i];
                    if (u == null) continue;

                    bool isSel = !string.IsNullOrEmpty(cur) &&
                                 string.Equals(u.name, cur, System.StringComparison.OrdinalIgnoreCase);
                    Rect r = new Rect(x, _menuRect.y + headH + i * rowH, w, rowH - 3f);

                    int def = (_menuIsland != null) ? LandingInjector.DefaultSquadSize(_menuIsland, u) : 0;
                    string label = string.Format("{0}{1}. {2}（{3}）   默认 {4} 个",
                        isSel ? "▶ " : "     ", i + 1, UnitNames.Of(u.name), u.name, def);

                    Color old = GUI.color;
                    if (isSel) GUI.color = new Color(0.45f, 1f, 1f, 1f);
                    if (GUI.Button(r, label)) SelectUnit(u);
                    GUI.color = old;
                }
            }

            // ---- 数量预设（0 = 默认：按兵种原版算法）----
            float cy = _menuRect.y + headH + rows * rowH + 2f;
            GUI.Label(new Rect(x, cy, w, 18f), "数量（左键点击；默认 = 按该兵种原版算法）");

            int[] presets = { 0, 1, 2, 3, 4, 6, 8, 10, 12 };
            float bw = 44f, gap = 4f;
            for (int i = 0; i < presets.Length; i++)
            {
                int v = presets[i];
                Rect br = new Rect(x + i * (bw + gap), cy + 20f, bw, 24f);
                Color old = GUI.color;
                if (curSize == v) GUI.color = new Color(0.45f, 1f, 1f, 1f);
                if (GUI.Button(br, (v == 0) ? "默认" : v.ToString())) SelectCount(v);
                GUI.color = old;
            }

            GUI.Label(new Rect(x, cy + countH, w, 34f),
                "F1 关闭菜单 · F2 强制清场（销毁本 mod 投放的全部船与单位）");
        }

        /// <summary>选中兵种：写回 cfg（BepInEx 会自动保存），下一次投放立即生效。</summary>
        void SelectUnit(VikingReference unit)
        {
            if (unit == null || ModConfig.EnemyName == null) return;
            if (string.Equals(ModConfig.EnemyName.Value, unit.name, System.StringComparison.OrdinalIgnoreCase)) return;

            ModConfig.EnemyName.Value = unit.name;
            Say("已选择兵种：" + unit.name);
            if (Log != null) Log.LogInfo("[NewMode] 已选择兵种：" + unit.name);
        }

        /// <summary>菜单是否遮挡该屏幕坐标（IMGUI 的 y 轴自上而下，需翻转）。</summary>
        bool PointerInMenu(Vector2 screenPos)
        {
            if (!_menuOpen) return false;
            Vector2 gui = new Vector2(screenPos.x, Screen.height - screenPos.y);
            return _menuRect.Contains(gui);
        }

        /// <summary>兵种列表随岛屿缓存（同岛复用；换岛重建）。</summary>
        void EnsureMenuUnits(Island island)
        {
            if (object.ReferenceEquals(_menuIsland, island) && _menuUnits != null) return;
            _menuIsland = island;
            _menuUnits = LandingInjector.AvailableUnits(island);
            if (Log != null) Log.LogInfo("[NewMode] 兵种菜单：本关可用 " + _menuUnits.Count + " 种");
        }

        /// <summary>当前 cfg 里选中的兵种对象（用于显示该兵种的默认/上限数量）。</summary>
        VikingReference SelectedUnit()
        {
            if (_menuUnits == null || ModConfig.EnemyName == null) return null;
            string cur = ModConfig.EnemyName.Value;
            if (string.IsNullOrEmpty(cur)) return null;

            for (int i = 0; i < _menuUnits.Count; i++)
            {
                VikingReference u = _menuUnits[i];
                if (u != null && string.Equals(u.name, cur, System.StringComparison.OrdinalIgnoreCase)) return u;
            }
            return null;
        }

        /// <summary>设定装载数量：0 = 默认（按该兵种的原版算法），&gt;0 = 固定数量。</summary>
        void SelectCount(int value)
        {
            if (ModConfig.SquadSize == null) return;
            if (ModConfig.SquadSize.Value == value) return;

            ModConfig.SquadSize.Value = value;
            Say(value > 0 ? ("已设定数量：" + value + "（超出船容量会自动裁剪）") : "数量：按兵种默认（原版算法）");
            if (Log != null) Log.LogInfo("[NewMode] 数量设定：" + ((value > 0) ? value.ToString() : "默认(原版算法)"));
        }

        static readonly List<string> _loggedOnce = new List<string>();

        /// <summary>同一 key 只打一次日志。悬停预览每帧都会走投放解析，这条是给那些"每帧都会命中"的提示用的。</summary>
        internal static void LogOnce(string key, string message)
        {
            if (Log == null || string.IsNullOrEmpty(key)) return;
            if (_loggedOnce.Contains(key)) return;
            _loggedOnce.Add(key);
            Log.LogInfo(message);
        }

        void Say(string msg)
        {
            _hud = msg;
            _hudUntil = Time.unscaledTime + 5f;
        }

        static string Fmt(Vector3 v)
        {
            return string.Format("({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
        }
    }
}
