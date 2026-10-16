using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>MarqueeSelect 的**绘制半边**（v1.7.3 / P2 拆分自 `MarqueeSelect.cs`，纯搬动、IL 级零行为变更）：
    /// 框选矩形、选中/受控单位的十字与目标环、鼠标落点光标，以及 IMGUI 坐标换算（`ToGui`/`RectFrom`）。
    /// 只读 `_dragging` / `_screenRect` / 注册表，不写任何状态。</summary>
    internal static partial class MarqueeSelect
    {
        static Rect RectFrom(Vector2 a, Vector2 b)
        {
            float x = Mathf.Min(a.x, b.x);
            float y = Mathf.Min(a.y, b.y);
            return new Rect(x, y, Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        static Rect ToGui(Rect r)
        {
            return new Rect(r.x, Screen.height - r.y - r.height, r.width, r.height);   // IMGUI 的 y 轴翻转
        }

        /// <summary>框选矩形 + 受控单位 / 目标点标记（由 Plugin.OnGUI 调用）。</summary>
        internal static void DrawOverlay()
        {
            if (!Util.V(ModConfig.RemoteHighlight, true)) return;

            if (_dragging)
            {
                Rect r = ToGui(_screenRect);
                GUI.color = new Color(0.4f, 1f, 1f, 0.12f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(0.4f, 1f, 1f, 0.9f);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            Camera cam = Cam();
            if (cam == null) return;

            if (_pending != null && _pending.Count > 0)                  // 待成队/已选中：亮青十字（船上=琥珀色）
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    ForeignUnit f = _pending[i];
                    Agent a = (f != null) ? f.agent : null;
                    if (!Usable(a)) continue;

                    Color c = ForeignUnit.Commandable(a)
                        ? new Color(0.4f, 1f, 1f, 0.9f)
                        : new Color(1f, 0.85f, 0.2f, 0.9f);             // 船上：只记了集结点，还没法移动
                    DrawCross(cam, a.wPos, c, 9f);
                }
            }

            if (RemoteGroup.Any || HasPending) DrawHoverCursor(cam);      // 原版同款：选中后鼠标下的地块亮起

            for (int ri = 0; ri < RemoteGroup.RallyCount; ri++)           // 船上单位的"登陆后集结点"
            {
                NavSpot rs = RemoteGroup.RallySpot(ri);
                if (rs != null) DrawRing(cam, rs.navPos.wPos, new Color(1f, 0.85f, 0.2f, 0.65f), 22f);
            }

            if (!RemoteGroup.Any) return;

            for (int gi = 0; gi < RemoteGroup.GroupCount; gi++)
            {
                RemoteGroup.Group g = RemoteGroup.At(gi);
                if (g == null) continue;

                if (g.target != null)
                    DrawRing(cam, g.target.navPos.wPos,
                        g.selected ? new Color(0.4f, 1f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.25f), 20f);

                Color color = g.selected ? new Color(1f, 0.85f, 0.2f, 0.8f) : new Color(1f, 1f, 1f, 0.35f);
                for (int i = 0; i < g.orders.Count; i++)
                {
                    GroupOrder o = g.orders[i];
                    Agent a = (o != null) ? o.agent : null;
                    if (!Usable(a)) continue;
                    DrawCross(cam, a.wPos, color, g.selected ? 7f : 5f);
                }
            }
        }

        /// <summary>十字标记（比小方块更容易看见：选中/受控单位用）。</summary>
        static void DrawCross(Camera cam, Vector3 world, Color c, float size)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            float x = sp.x;
            float y = Screen.height - sp.y;
            float h = size * 0.5f;

            GUI.color = c;
            GUI.DrawTexture(new Rect(x - h, y - 0.5f, size, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 0.5f, y - h, 1f, size), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static void DrawRing(Camera cam, Vector3 world, Color c, float size)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            float x = sp.x;
            float y = Screen.height - sp.y;
            float h = size * 0.5f;

            GUI.color = c;
            GUI.DrawTexture(new Rect(x - h, y - h, size, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - h, y + h - 1f, size, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - h, y - h, 1f, size), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + h - 1f, y - h, 1f, size), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>鼠标落点光标：指针处的地面点吸附到最近的 NavSpot 再画框（原版选中我队时也用这个提示）。</summary>
        static void DrawHoverCursor(Camera cam)
        {
            IslandGameplayManager gm = Singleton<IslandGameplayManager>.instance;
            Island island = (gm != null) ? gm.island : null;
            if (island == null) return;

            Vector3 land;
            string diag;
            if (!Plugin.TryGetLandPoint(island, Input.mousePosition, out land, out diag)) return;

            NavSpot spot = NavSpot.GetNavSpot(land, true);
            if (spot == null) return;

            DrawRing(cam, spot.navPos.wPos, new Color(0.4f, 1f, 1f, 0.5f), 22f);
        }

    }
}