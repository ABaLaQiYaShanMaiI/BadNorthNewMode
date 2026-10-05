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

        /// <summary>立刻发射当前编队（`FlotillaDelay = 0` 时用）。</summary>
        internal static void FlushNow()
        {
            if (_instance != null) _instance.Launch();
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
