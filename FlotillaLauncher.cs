using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.RaidGeneration;

namespace BadNorthNewMode
{
    /// <summary>编队发射器：窗口内连投合并进同一个 Wave（只播一条接近音乐、一次 BeginWave，见 PROJECT_SPEC §5）。</summary>
    internal sealed class FlotillaLauncher : MonoBehaviour
    {
        static FlotillaLauncher _instance;
        static Island _island;
        static Wave _wave;
        static ShipGroup _group;
        static float _openedAt;
        static float _deadline;
        static float _spread;

        internal static FlotillaLauncher Get()
        {
            if (_instance != null) return _instance;
            GameObject go = new GameObject("BadNorthNewMode.Flotilla");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<FlotillaLauncher>();
            return _instance;
        }

        /// <summary>为本次投放取一个 ShipGroup：窗口内命中则复用上一编队，否则新建 Wave+Group。</summary>
        internal static ShipGroup Reserve(Island island, Raid raid, float delay, float spread, out Wave wave, out bool createdNew)
        {
            Get();
            float now = Time.time;
            bool join = (_wave != null) && object.ReferenceEquals(_island, island) && (_group != null) &&
                        (now <= _deadline) && (delay > 0f) &&
                        (_group.landings.Count < Util.V(ModConfig.FlotillaMaxShips, 6));

            if (join)
            {
                wave = _wave;
                createdNew = false;
                _deadline = Mathf.Min(_deadline + delay * 0.5f, _openedAt + delay * 3f);   // 续窗，但不超过 3 倍
                return _group;
            }

            GameObject waveGo = new GameObject("ModWave");
            wave = waveGo.AddComponent<Wave>();
            wave.raid = raid;
            // v1.7.1-b4：自建波显式写 0（= 原版第 0 波的语义），不要留字段默认的 -1f。
            // `waveStartTime` 经 `ShipGroup.startTime` → `Landing.startTime` 参与两处：
            //   ① `Landing.TryPlace` 用它当重叠盒的 Y（`startTime * 0.1f`）——负值会让船从海平面以下进场；
            //   ② `Wave.BeginWave` 用各 landing 的 `startTime` **差值**错开出发时刻 —— 写成 float.MaxValue 会让
            //      所有差值饱和成 0（同一编队同一帧全出发，见 LevelTools.TryBlockWaves 为何必须跳过自家 Wave）。
            wave.waveStartTime = 0f;
            wave.transform.SetParent(raid.landingContainer, false);

            GameObject groupGo = new GameObject("Group");
            ShipGroup newGroup = groupGo.AddComponent<ShipGroup>();
            wave.AddShipGroup(newGroup);

            _island = island;
            _wave = wave;
            _group = newGroup;
            _openedAt = now;
            _deadline = now + Mathf.Max(0f, delay);
            _spread = spread;
            createdNew = true;
            return newGroup;
        }

        internal static void FlushNow()
        {
            if (_instance != null) _instance.Launch();
        }

        /// <summary>v1.7.6：**放弃还没发射的编队窗口**（换岛 / 回地图 / 关总开关时）。
        /// 旧实现只靠 `Update` 里"wave 被销毁了才知道"—— 但窗口期内岛屿还在、wave 也还活着，
        /// 于是关掉总开关后它仍会照常 `BeginWave()`（"完全静默"就断了一半）。</summary>
        internal static void Cancel()
        {
            Wave wave = _wave;
            GameObject waveGo = (wave != null) ? wave.gameObject : null;
            Clear();
            if (waveGo != null) UnityEngine.Object.Destroy(waveGo);
        }

        /// <summary>插件卸载：销毁这个常驻辅助对象（先取消挂起的编队）。</summary>
        internal static void Destroy()
        {
            Cancel();
            if (_instance != null) UnityEngine.Object.Destroy(_instance.gameObject);
            _instance = null;
        }

        void Update()
        {
            if (_wave == null) return;
            if (_wave.gameObject == null) { Clear(); return; }
            if (Time.time < _deadline) return;
            Launch();
        }

        void Launch()
        {
            Wave wave = _wave;
            ShipGroup group = _group;
            float spread = _spread;
            Clear();
            if (wave == null || wave.gameObject == null) return;
            if (group == null || group.landings == null || group.landings.Count == 0)
            {
                UnityEngine.Object.Destroy(wave.gameObject);
                return;
            }

            wave.timeSpreadGroup = spread;      // 覆盖 Wave.Awake 的随机散布，避免编队拖到十几秒
            wave.timeSpreadShip = spread;
            wave.RefreshLandings();
            if (wave.raid != null) wave.raid.StartCoroutine(wave.BeginWave());
            Util.Log(Loc.F("[NewMode] 编队出发：{0} 艘（一条接近音乐）", group.landings.Count));
        }

        static void Clear()
        {
            _wave = null;
            _group = null;
            _island = null;
            _openedAt = 0f;
            _deadline = 0f;
        }
    }
}
