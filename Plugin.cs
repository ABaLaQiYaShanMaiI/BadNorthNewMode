using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
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
        public const string VERSION = "0.2.4";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        bool _armed;
        bool _subscribed;
        bool _subscribeFailed;
        string _hud = "";
        string _hover = "";
        float _hudUntil;

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
                _armed = !_armed;
                _hover = "";
                PlacementMarker.Get().Hide();
                Say(_armed ? "投放模式：点击滩头陆地投放敌舰；右键 / Esc 取消" : "已退出投放模式");
                Log.LogInfo("[NewMode] " + _hud);
                return;
            }
            if (!_armed) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                _armed = false; _hover = "";
                PlacementMarker.Get().Hide();
                Say("已取消投放");
                return;
            }

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            string why;
            if (!InBattle(gm, out why)) { PlacementMarker.Get().Hide(); _hover = why; return; }

            // ---- 悬停预览：实时把鼠标下的地形算一遍（点击本身由游戏事件负责）----
            Vector2 screenPos = Input.mousePosition;
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

        /// <summary>极简 HUD（无资源）：模式状态 + 悬停地形判定 + 上一次结果。</summary>
        void OnGUI()
        {
            if (!ModConfig.ShowHud.Value) return;
            if (!_armed && Time.unscaledTime > _hudUntil) return;

            string text = _armed
                ? "BadNorthNewMode · 投放模式（左键点滩头陆地 / 右键或 Esc 取消）"
                : "BadNorthNewMode";
            if (_armed && !string.IsNullOrEmpty(_hover)) text += "\n" + _hover;
            if (!string.IsNullOrEmpty(_hud)) text += "\n" + _hud;

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(8f, 8f, 640f, 66f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16f, 12f, 640f, 62f), text);
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
