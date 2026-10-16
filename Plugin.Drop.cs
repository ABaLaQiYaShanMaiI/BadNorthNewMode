using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>投放半边（v1.7.3 / P2 拆分自 `Plugin.cs`，纯搬动、IL 级零行为变更）：F1 菜单模式下的悬停预览与轮询兜底、
    /// `DoDrop`（订阅回调与轮询共用的唯一投放入口）、以及屏幕坐标 → 地面点的射线取点。</summary>
    public partial class Plugin : BaseUnityPlugin
    {
        void HandleDropMode(IslandGameplayManager gm)
        {
            if (VanillaUI.ModalShowing) { PlacementMarker.Get().Hide(); return; }   // 确认框在前台：投放输入让位

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                IngameMenu.Close();
                IngameMenu.Say(Loc.T("已取消投放"));
                return;
            }

            string why;
            if (!InBattle(gm, out why)) { PlacementMarker.Get().Hide(); IngameMenu.Hover = why; return; }

            IngameMenu.EnsureUnits(gm.island);

            // 悬停预览（点击本身由游戏事件负责）
            Vector2 screenPos = Input.mousePosition;
            if (IngameMenu.AnyPanelContains(screenPos) || SquadBar.Contains(screenPos))   // 菜单 / 提示框 / 头像条上不做投放（点击归它们）
            {
                PlacementMarker.Get().Hide();
                IngameMenu.Hover = IngameMenu.Contains(screenPos) ? Loc.T("（指针在菜单上）") : "";
                return;
            }

            Vector3 land;
            string diag;
            bool hasLand = TryGetLandPoint(gm.island, screenPos, out land, out diag);

            DropTarget target = default(DropTarget);
            string reason = null;
            bool ok = hasLand && DropPlanner.TryResolve(gm.island, land, out target, out reason);

            if (Util.V(ModConfig.ShowHoverPreview, true))
            {
                if (!hasLand) PlacementMarker.Get().Hide();
                else PlacementMarker.Get().Show(ok ? target.beach.navPos.pos : land, ok, 0.35f);
            }

            IngameMenu.Hover = !hasLand
                ? Loc.F("指针不在陆地上（{0}）", diag)
                : (ok ? Loc.F("滩头可用：落差 {0:F2}m，距点击处 {1:F1}m",
                        target.beach.navPos.pos.y - Util.V(ModConfig.WaterLevelY, 0f), target.shoreDist)
                      : reason);

            // 兜底：未订阅成功才轮询（避免一次点击投两艘）
            if (!_subscribed && Input.GetMouseButtonDown(0))
                DoDrop(gm, screenPos, Loc.T("轮询兜底"));
        }

        /// <summary>一次投放尝试：全程打日志（排查"点了没反应"的唯一依据）。</summary>
        void DoDrop(IslandGameplayManager gm, Vector2 screenPos, string source)
        {
            // v1.7.1：让路（Together 联机中 / 整合包终岛无尽接管本关）—— 这条兜底防的是"菜单在让路前就开着"的旧状态。
            string blockWhy;
            if (Compat.DropBlocked(gm, out blockWhy))
            {
                Util.Warn(Loc.F("[NewMode][兼容] 投放已让路：{0}", blockWhy));
                IngameMenu.Say(blockWhy);
                return;
            }

            // v1.7.1-b3：能走到这里说明联机中的让路被 cfg 关掉/强制开了 → 提示一次"对方看不到这些单位"。
            if (Compat.TogetherOnline) Compat.NoteDropInSession();

            Util.Log(Loc.F("[NewMode][点击] 屏幕 ({0:F0},{1:F0}) 来源={2}", screenPos.x, screenPos.y, source));

            Vector3 land;
            string diag;
            if (!TryGetLandPoint(gm.island, screenPos, out land, out diag))
            {
                Util.Warn(Loc.F("[NewMode][点击] 地形未命中：{0}", diag));
                IngameMenu.Say(Loc.F("这里不是陆地地块：{0}", diag));
                return;
            }
            Util.Log(Loc.F("[NewMode][点击] 命中地形：{0}，点 {1}，海拔 {2:F2}m",
                diag, Util.Fmt(land), land.y - Util.V(ModConfig.WaterLevelY, 0f)));

            DropTarget t;
            string reason;
            if (!DropPlanner.TryResolve(gm.island, land, out t, out reason))
            {
                Util.Warn(Loc.F("[NewMode][点击] 无法投放：{0}", reason));
                IngameMenu.Say(Loc.F("无法投放：{0}", reason));
                return;
            }

            string info;
            if (LandingInjector.TrySpawn(gm.island, land, out info))
            {
                Util.Log(Loc.T("[NewMode][点击] ") + info);
                IngameMenu.Say(info);
            }
            else
            {
                Util.Warn(Loc.F("[NewMode][点击] 投放失败：{0}", info));
                IngameMenu.Say(Loc.F("投放失败：{0}", info));
            }
        }

        /// <summary>只在"正常战局、岛屿处于 Playing"时可投放 / 可下令。</summary>
        internal static bool InBattle(IslandGameplayManager gm, out string why)
        {
            why = null;
            if (gm == null) { why = Loc.T("不在战局中"); return false; }
            if (gm.levelPauser != null && gm.levelPauser.isPaused) { why = Loc.T("已暂停"); return false; }
            Island island = gm.island;
            if (island == null || !island.generated) { why = Loc.T("岛屿未就绪"); return false; }
            if (island.state != Island.State.Playing) { why = Loc.T("岛屿未进入 Playing 状态"); return false; }
            if (island.raid == null) { why = Loc.T("Raid 未就绪"); return false; }
            return true;
        }

        /// <summary>屏幕坐标 → 地面点（首选 NavSpotter.NavSpotCast，兜底 viewport 射线 × Voxels 层）。</summary>
        internal static bool TryGetLandPoint(Island island, Vector2 screenPos, out Vector3 point, out string diag)
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
                        diag = Loc.F("NavSpotCast 命中 {0}", HitName(hit));
                        return true;
                    }
                    diag = Loc.T("NavSpotCast 未命中");
                }
                catch (System.Exception e)
                {
                    diag = Loc.F("NavSpotCast 异常 {0}", e.GetType().Name);
                }
            }
            else
            {
                diag = Loc.T("navSpotter 不可用");
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
                diag += Loc.F("；Voxels 层命中 {0}", HitName(h));
                return true;
            }
            if (Physics.Raycast(ray, out h, 500f))
            {
                point = h.point;
                diag += Loc.F("；任意碰撞体命中 {0}", HitName(h));
                return true;
            }
            diag += Loc.F("；射线仍未命中（mask={0}，vp={1:F3},{2:F3}）", mask, vp.x, vp.y);
            return false;
        }

        static string HitName(RaycastHit hit)
        {
            GameObject go = hit.collider.gameObject;
            string layer = LayerMask.LayerToName(go.layer);
            return go.name + "@" + (string.IsNullOrEmpty(layer) ? go.layer.ToString() : layer);
        }

    }
}