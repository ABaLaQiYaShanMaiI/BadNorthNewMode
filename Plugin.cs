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
    /// 无 Harmony 补丁：只"构造原版对象 + 触发原版协程"，不改游戏文件。
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "badnorth.newmode";
        public const string NAME = "Bad North - New Mode";
        public const string VERSION = "0.2.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        bool _armed;
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

            EventSystem es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject()) { PlacementMarker.Get().Hide(); _hover = "指针在 UI 上"; return; }

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            string why;
            if (!InBattle(gm, out why)) { PlacementMarker.Get().Hide(); _hover = why; return; }

            // ---- 只认陆地：射线打岛体地形（原版 "Voxels" 层），水面/天空都取不到 ----
            Vector3 land;
            if (!TryGetLandPoint(out land))
            {
                PlacementMarker.Get().Hide();
                _hover = "指针不在陆地上（原版只有陆地可交互）";
                if (Input.GetMouseButtonDown(0)) Say("这一点不是陆地地块");
                return;
            }

            DropTarget target;
            string reason;
            bool ok = LandingInjector.TryResolve(gm.island, land, out target, out reason);
            bool clicked = Input.GetMouseButtonDown(0);

            if (ModConfig.ShowHoverPreview.Value)
            {
                float seconds = (ok || clicked) ? Mathf.Max(ModConfig.MarkerSeconds.Value, 0.2f) : 0.35f;
                // 可行 → 亮青标记落在真正的滩头落点；不可行 → 暗红标记落在你点的地方，便于判断地形
                PlacementMarker.Get().Show(ok ? target.beach.navPos.pos : land, ok, seconds);
            }

            _hover = ok
                ? string.Format("滩头可用：落差 {0:F2}m，距点击处 {1:F1}m",
                    target.beach.navPos.pos.y - ModConfig.WaterLevelY.Value, target.shoreDist)
                : reason;

            if (!clicked) return;

            string info;
            if (LandingInjector.TrySpawn(gm.island, land, out info))
            {
                Say(info);
                Log.LogInfo("[NewMode] " + info);
            }
            else
            {
                Say("投放失败：" + info);
                Log.LogWarning("[NewMode] 投放失败：" + info);
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

        /// <summary>鼠标射线打岛体地形，返回命中点的真实世界坐标（含海拔）。</summary>
        static bool TryGetLandPoint(out Vector3 point)
        {
            point = Vector3.zero;
            LevelCamera lc = Singleton<LevelCamera>.instance;
            Camera cam = (lc != null) ? lc.cameraRef : Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            int mask = LayerMaster.voxelMask.value;   // 原版打地面用的就是 "Voxels" 层
            RaycastHit hit;
            if (mask != 0 && Physics.Raycast(ray, out hit, 500f, mask)) { point = hit.point; return true; }
            if (Physics.Raycast(ray, out hit, 500f)) { point = hit.point; return true; }   // 兜底：任意碰撞体

            if (ModConfig.VerboseLog.Value && Log != null)
                Log.LogInfo("[NewMode] 地形射线未命中（mask=" + mask + "）");
            return false;
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
            GUI.DrawTexture(new Rect(8f, 8f, 620f, 66f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16f, 12f, 620f, 62f), text);
        }

        void Say(string msg)
        {
            _hud = msg;
            _hudUntil = Time.unscaledTime + 5f;
        }
    }
}
