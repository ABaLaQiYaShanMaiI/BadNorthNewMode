using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;

namespace BadNorthNewMode
{
    /// <summary>左键从"非原生单位附近"起拖 = 框选（框选期间相机拖拽让位）；别处左键拖动仍由游戏平移相机。见 PROJECT_SPEC §4/§5。</summary>
    internal static class MarqueeSelect
    {
        static bool _held;
        static bool _grab;                 // 本次按下是否落在非原生单位附近（是 → 拖动即框选）
        static bool _dragging;
        static Vector2 _start;             // 屏幕坐标（原点左下，与 Input.mousePosition 一致）
        static Rect _screenRect;
        static CursorManager.IDragListener _detached;
        static CameraController _camera;
        static Camera _cam;
        static List<ForeignUnit> _pending;      // 已框选、等待"点地块"成队的目标
        static Vector2 _pendingCenter;

        internal static bool Dragging { get { return _dragging; } }
        internal static bool ConsumedClick;      // 起点在单位上的单击 → 不作移动命令
        internal static List<ForeignUnit> Pending { get { return _pending; } }

        /// <summary>取走待成队的框选结果（点地块时调用；取走即清空）。</summary>
        internal static List<ForeignUnit> TakePending(out Vector2 center)
        {
            List<ForeignUnit> p = _pending;
            center = _pendingCenter;
            _pending = null;
            return p;
        }

        internal static void Tick(IslandGameplayManager gm)
        {
            ConsumedClick = false;
            if (!Util.V(ModConfig.RemoteControl, true)) { Cancel(gm); return; }
            if (IngameMenu.IsOpen) { Cancel(gm); return; }

            string why;
            if (!Plugin.InBattle(gm, out why)) { Cancel(gm); return; }

            if (Input.GetMouseButtonDown(0))
            {
                _held = true;
                _dragging = false;
                _start = Input.mousePosition;
                _grab = NearForeignUnit(_start);
                if (_grab) DetachCamera(gm);     // 按下的瞬间就接管相机：这一次拖动不平移，避免"先平移一点再被接管"
            }

            if (_held && Input.GetMouseButton(0))
            {
                Vector2 now = Input.mousePosition;
                if (_grab && !_dragging && (now - _start).magnitude >= Util.V(ModConfig.RemoteMarqueePixels, 8))
                {
                    _dragging = true;
                    if (Util.V(ModConfig.VerboseLog, false)) Util.Log("[NewMode][遥控] 开始框选");
                }
                if (_dragging) _screenRect = RectFrom(_start, now);
            }

            if (_held && (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)))   // 也兜底"按住时切窗口丢 Up 事件"
            {
                bool wasDrag = _dragging;
                bool wasGrab = _grab;
                _held = false;
                _dragging = false;
                _grab = false;
                AttachCamera(gm);

                if (wasDrag) ApplySelection();
                else ConsumedClick = wasGrab;         // 点在单位上的单击：这一下不当"移动命令"
            }
        }

        /// <summary>框选结果先记为"待成队"；点地块时才由 RemoteGroup 按兵种分队并前进。</summary>
        static void ApplySelection()
        {
            _pending = Pick(_screenRect);
            _pendingCenter = _screenRect.center;

            string msg = (_pending.Count == 0)
                ? "框里没有非原生单位"
                : string.Format("已框选 {0} 个非原生单位（左键点地块 = 成队并前进）", _pending.Count);
            IngameMenu.Say(msg);
            Util.Log("[NewMode][遥控] " + msg + string.Format("（矩形 {0:F0}×{1:F0}）", _screenRect.width, _screenRect.height));
        }

        static List<ForeignUnit> Pick(Rect screenRect)
        {
            ForeignUnit.Prune();
            List<ForeignUnit> res = new List<ForeignUnit>();

            Camera cam = Cam();
            if (cam == null) return res;

            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
                if (sp.z <= 0f) continue;                                    // 相机背后
                if (screenRect.Contains(new Vector2(sp.x, sp.y))) res.Add(f);
            }
            return res;
        }

        /// <summary>按下点是否离某个非原生单位足够近（决定"拖动=框选"还是"拖动=平移相机"）。</summary>
        static bool NearForeignUnit(Vector2 screenPos)
        {
            int radius = Util.V(ModConfig.RemoteGrabRadius, 48);
            if (radius <= 0) return true;                                    // 0 = 任意位置起拖都框选

            ForeignUnit.Prune();
            Camera cam = Cam();
            if (cam == null) return false;

            float r2 = (float)radius * radius;
            for (int i = 0; i < ForeignUnit.All.Count; i++)
            {
                ForeignUnit f = ForeignUnit.All[i];
                Agent a = (f != null) ? f.agent : null;
                if (!Usable(a)) continue;

                Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
                if (sp.z <= 0f) continue;

                float dx = sp.x - screenPos.x;
                float dy = sp.y - screenPos.y;
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }

        internal static float ScreenDist2(Agent a, Vector2 center)
        {
            Camera cam = Cam();
            if (cam == null || a == null) return float.MaxValue;

            Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
            if (sp.z <= 0f) return float.MaxValue;

            float dx = sp.x - center.x;
            float dy = sp.y - center.y;
            return dx * dx + dy * dy;
        }

        /// <summary>可被框选/标记：还活着、已生成、且已踩在岛上（下船完成）。</summary>
        static bool Usable(Agent a)
        {
            return a != null && a.spawned.active && a.aliveState.active && a.navPos.island;
        }

        static void Cancel(IslandGameplayManager gm)
        {
            ConsumedClick = false;
            if (!_held && !_dragging) return;
            _held = false;
            _grab = false;
            _dragging = false;
            AttachCamera(gm);
        }

        /// <summary>框选期间把自己压到拖拽栈顶，等价于"让相机这一会儿不听拖拽"（见 §4 坑表）。</summary>
        static void DetachCamera(IslandGameplayManager gm)
        {
            if (_detached != null || gm == null || gm.cursorManager == null) return;

            CameraController cam = FindCameraController(gm);
            if (cam == null) return;

            CursorManager.IDragListener l = cam;
            if (gm.cursorManager.Contains(l))
            {
                gm.cursorManager.Remove(l);
                _detached = l;
            }
        }

        static void AttachCamera(IslandGameplayManager gm)
        {
            if (_detached == null) return;

            CursorManager cm = (gm != null) ? gm.cursorManager : null;
            if (cm != null && !cm.Contains(_detached)) cm.Add(_detached);
            _detached = null;
        }

        static CameraController FindCameraController(IslandGameplayManager gm)
        {
            if (_camera != null) return _camera;
            if (gm != null) _camera = gm.GetComponentInChildren<CameraController>(true);
            if (_camera == null) _camera = UnityEngine.Object.FindObjectOfType<CameraController>();
            return _camera;
        }

        static Camera Cam()
        {
            if (_cam != null) return _cam;
            LevelCamera lc = Singleton<LevelCamera>.instance;
            _cam = (lc != null) ? lc.cameraRef : Camera.main;
            return _cam;
        }

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

            if (_pending != null && _pending.Count > 0)                  // 待成队的框选：亮青点
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    ForeignUnit f = _pending[i];
                    Agent a = (f != null) ? f.agent : null;
                    if (!Usable(a)) continue;
                    DrawDot(cam, a.wPos, new Color(0.4f, 1f, 1f, 0.85f), 5f);
                }
            }

            if (!RemoteGroup.Any) return;

            for (int gi = 0; gi < RemoteGroup.GroupCount; gi++)
            {
                RemoteGroup.Group g = RemoteGroup.At(gi);
                if (g == null) continue;

                if (g.selected && g.target != null)
                    DrawDot(cam, g.target.navPos.wPos, new Color(0.4f, 1f, 1f, 0.85f), 7f);

                Color color = g.selected ? new Color(1f, 0.85f, 0.2f, 0.7f) : new Color(1f, 1f, 1f, 0.35f);
                for (int i = 0; i < g.orders.Count; i++)
                {
                    GroupOrder o = g.orders[i];
                    Agent a = (o != null) ? o.agent : null;
                    if (!Usable(a)) continue;
                    DrawDot(cam, a.wPos, color, 4f);
                }
            }
        }

        static void DrawDot(Camera cam, Vector3 world, Color c, float size)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            GUI.color = c;
            GUI.DrawTexture(new Rect(sp.x - size * 0.5f, Screen.height - sp.y - size * 0.5f, size, size),
                Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
