using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>投放对象登记册：我们的 Wave 不在 raid.waves，原版清场不会动它（幽灵船，见 §4）；另附 F2 手动清场。</summary>
    internal sealed class SpawnLedger : MonoBehaviour
    {
        sealed class Item
        {
            internal GameObject root;    // 我们创建的 Wave 根节点（船是其子孙）
            internal Squad squad;        // ShipGroup 懒加载出的维京 squad（**单位挂在它下面**，不在 root 下）
            internal Island island;
        }

        static SpawnLedger _instance;
        readonly List<Item> _items = new List<Item>();
        bool _subscribedLevelEnd;

        internal static SpawnLedger Get()
        {
            if (_instance != null) return _instance;
            GameObject go = new GameObject("BadNorthNewMode.Ledger");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<SpawnLedger>();
            return _instance;
        }

        /// <summary>登记一次投放的根节点（在 LandingInjector 建树后立即调用）。</summary>
        internal static void Track(GameObject root, Island island)
        {
            if (root == null) return;
            Item it = new Item();
            it.root = root;
            it.island = island;
            Get()._items.Add(it);
        }

        /// <summary>
        /// 登记单位所在的维京 squad：`ShipGroup.squad` 懒加载 → `SpawnGetFromPrefab(..., island.runContainer)`，
        /// 所以**登岛单位不在本 Wave 树里**，只销毁 Wave 会留下它们（见 §4 坑表）。同 squad 只登记一次。
        /// </summary>
        internal static void TrackSquad(Squad squad, Island island)
        {
            if (squad == null) return;

            List<Item> items = Get()._items;
            for (int i = 0; i < items.Count; i++)
                if (object.ReferenceEquals(items[i].squad, squad)) return;

            Item it = new Item();
            it.squad = squad;
            it.island = island;
            items.Add(it);
        }

        void Update()
        {
            TrySubscribeLevelEnd();
            if (_items.Count == 0) return;

            if (ModConfig.CleanupHotkey != null && ModConfig.CleanupHotkey.Value.IsDown())
            {
                int n = DestroyAll();
                Util.Log("[NewMode][清理] 手动清场：销毁 " + n + " 组投放对象");
            }

            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (gm != null) ? gm.island : null;

            // 原版语义补刀：离开战局 / 岛屿未就绪 / Raid 失效 → 我们的对象也一起走
            bool leaving = (island == null) || (island.state != Island.State.Playing) || (island.raid == null);
            if (leaving)
            {
                int n = DestroyAll();
                if (n > 0)
                    Util.Log("[NewMode][清理] 离开战局：已清除本 mod 投放的 " + n + " 组残留（对齐原版 IIslandWipe）");
                return;
            }

            // 换岛：同一 Island 实例复用时不触发
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                Item it = _items[i];
                if (it.root == null) { _items.RemoveAt(i); continue; }
                if (!object.ReferenceEquals(it.island, island))
                {
                    UnityEngine.Object.Destroy(it.root);
                    _items.RemoveAt(i);
                }
            }
        }

        int DestroyAll()
        {
            int n = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                Item it = _items[i];
                if (it.root != null) { UnityEngine.Object.Destroy(it.root); n++; }
                if (it.squad != null) { UnityEngine.Object.Destroy(it.squad.gameObject); n++; }   // 单位挂这里
                _items.RemoveAt(i);
            }
            n += ForeignUnit.DestroyAll();        // 兜底：已登记的非原生单位（含不在 Wave 树里的）
            if (n > 0) { DropPlanner.InvalidateOccupancy(); RemoteGroup.Clear(); }
            return n;
        }

        /// <summary>订阅 EndOfLevel.postProcess（Action&lt;Island&gt;，可直接订阅，见 §4 坑表）。</summary>
        void TrySubscribeLevelEnd()
        {
            if (_subscribedLevelEnd) return;
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            if (gm == null) return;
            EndOfLevel eol = gm.endOfLevel;
            if (eol == null) return;

            eol.postProcess += OnLevelPostProcess;
            _subscribedLevelEnd = true;
            Util.Log("[NewMode] 已订阅原版战局结束事件 EndOfLevel.postProcess（用于自动清场）。");
        }

        /// <summary>战局结束（结算完成）→ 立刻清掉本 mod 投放的对象。</summary>
        void OnLevelPostProcess(Island island)
        {
            try
            {
                int n = DestroyAll();
                Util.Log(string.Format("[NewMode][清理] 战局结束（{0}）：已清除本 mod 投放的 {1} 组对象", ReasonName(), n));
            }
            catch (System.Exception e)
            {
                Util.Error("[NewMode][清理] 战局结束清理异常：" + e);
            }
        }

        static string ReasonName()
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            if (gm == null || gm.endOfLevel == null) return "?";
            return gm.endOfLevel.reason.ToString();     // None / Won / Wiped / Fled
        }
    }
}
