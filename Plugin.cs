using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>
    /// 战局内按热键进入投放模式 → 点击水面 → 该处按原版流程来一艘敌舰。
    /// 无 Harmony 补丁：只"构造原版对象 + 触发原版协程"，不改游戏文件。
    /// </summary>
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "badnorth.newmode";
        public const string NAME = "Bad North - New Mode";
        public const string VERSION = "0.1.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        bool _armed;
        string _hud = "";
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
            Log.LogInfo(string.Format("[NewMode] v{0} 已加载：{1} 开关投放模式，点击水面投放敌舰。",
                VERSION, (hk != null) ? hk.Value.ToString() : "(热键未绑定)"));
        }

        void Update()
        {
            if (ModConfig.Hotkey == null) return;

            if (ModConfig.Hotkey.Value.IsDown())
            {
                _armed = !_armed;
                Say(_armed ? "投放模式：点击水面放置敌舰；右键 / Esc 取消" : "已退出投放模式");
                Log.LogInfo("[NewMode] " + _hud);
                return;
            }
            if (!_armed) return;

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) { _armed = false; Say("已取消投放"); return; }
            if (!Input.GetMouseButtonDown(0)) return;

            EventSystem es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject()) return;   // 点在 UI 上，不算投放

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            string why;
            if (!InBattle(gm, out why)) { Say(why); return; }

            Vector3 water;
            if (!TryWaterPoint(out water)) { Say("这一点取不到海面（视角太斜）"); return; }

            string info;
            if (LandingInjector.TrySpawn(gm.island, water, out info)) { Say(info); Log.LogInfo("[NewMode] " + info); }
            else { Say("投放失败：" + info); Log.LogWarning("[NewMode] 投放失败：" + info); }
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

        /// <summary>鼠标射线 × 海平面 = 水面点（原版水面 ≈ y0，可用 cfg WaterLevelY 覆盖）。</summary>
        static bool TryWaterPoint(out Vector3 point)
        {
            point = Vector3.zero;
            LevelCamera lc = Singleton<LevelCamera>.instance;
            Camera cam = (lc != null) ? lc.cameraRef : Camera.main;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            float planeY = ModConfig.WaterLevelY.Value;
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return false;
            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return false;
            point = ray.origin + ray.direction * t;
            point.y = planeY;

            if (ModConfig.VerboseLog.Value && Log != null)
                Log.LogInfo(string.Format("[NewMode] 鼠标 {0} → 水面点 ({1:F2},{2:F2},{3:F2})",
                    Input.mousePosition, point.x, point.y, point.z));
            return true;
        }

        /// <summary>极简 HUD（无资源）：显示模式状态与上一次结果。</summary>
        void OnGUI()
        {
            if (!ModConfig.ShowHud.Value) return;
            if (!_armed && Time.unscaledTime > _hudUntil) return;

            string text = _armed
                ? "BadNorthNewMode · 投放模式（左键点水面 / 右键或 Esc 取消）\n" + _hud
                : "BadNorthNewMode\n" + _hud;

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(8f, 8f, 560f, 52f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16f, 12f, 560f, 48f), text);
        }

        void Say(string msg)
        {
            _hud = msg;
            _hudUntil = Time.unscaledTime + 5f;
        }
    }
}
