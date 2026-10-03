using UnityEngine;

namespace BadNorthNewMode
{
    /// <summary>
    /// 零资源的"光亮"落点标记：运行时生成贴图 + 呼吸缩放/闪烁，平铺在地面上，
    /// 观感对齐原版技能落点的高亮环。没有外部资源文件，不依赖任何游戏预制件。
    /// </summary>
    internal sealed class PlacementMarker : MonoBehaviour
    {
        const int TexSize = 128;

        static PlacementMarker _instance;
        static Texture2D _ringTex;
        static Texture2D _discTex;
        static Shader _shader;

        SpriteRenderer _ring;
        SpriteRenderer _disc;
        Color _ringBase = Color.white;
        Color _discBase = Color.white;
        float _until;
        float _phase;

        /// <summary>取（或懒创建）唯一的标记实例。</summary>
        internal static PlacementMarker Get()
        {
            if (_instance != null) return _instance;

            GameObject go = new GameObject("BadNorthNewMode.Marker");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<PlacementMarker>();
            _instance.Build();
            return _instance;
        }

        void Build()
        {
            if (_shader == null) _shader = PickShader();
            if (_ringTex == null) _ringTex = MakeRingTex();
            if (_discTex == null) _discTex = MakeDiscTex();

            _ring = MakeSprite("Ring", _ringTex, 1.35f);
            _disc = MakeSprite("Disc", _discTex, 1.05f);
            gameObject.SetActive(false);
        }

        SpriteRenderer MakeSprite(string spriteName, Texture2D tex, float worldSize)
        {
            GameObject go = new GameObject(spriteName);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // 法线朝上：平铺在地面
            go.transform.localScale = new Vector3(worldSize, worldSize, 1f);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
            if (_shader != null) sr.sharedMaterial = new Material(_shader);
            sr.sortingOrder = 600;
            return sr;
        }

        /// <summary>在 pos 处显示标记（valid=亮青白，invalid=暗红）。</summary>
        internal void Show(Vector3 pos, bool valid, float seconds)
        {
            Color c = valid ? new Color(0.45f, 1f, 1f, 1f) : new Color(1f, 0.35f, 0.3f, 0.65f);
            _ringBase = c;
            _discBase = new Color(c.r, c.g, c.b, c.a * 0.35f);
            _phase = 0f;
            _until = Time.unscaledTime + seconds;
            transform.position = pos + Vector3.up * 0.03f;                 // 抬高一点，避免与地形 Z-fighting
            transform.localScale = Vector3.one;
            gameObject.SetActive(true);
        }

        internal void Hide()
        {
            _until = 0f;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        void Update()
        {
            if (Time.unscaledTime >= _until) { Hide(); return; }

            _phase += Time.unscaledDeltaTime;
            float t = Mathf.PingPong(_phase * 2.2f, 1f);                   // 呼吸
            float s = Mathf.Lerp(0.92f, 1.14f, t);
            transform.localScale = new Vector3(s, s, s);

            float a = Mathf.Lerp(0.55f, 1f, t);
            if (_ring != null) _ring.color = new Color(_ringBase.r, _ringBase.g, _ringBase.b, _ringBase.a * a);
            if (_disc != null) _disc.color = new Color(_discBase.r, _discBase.g, _discBase.b, _discBase.a * a);
        }

        /// <summary>优先加法混合（更"亮"），拿不到就退回精灵默认着色器。</summary>
        static Shader PickShader()
        {
            string[] names = { "Particles/Additive", "Legacy Shaders/Particles/Additive", "Mobile/Particles/Additive", "Sprites/Default" };
            for (int i = 0; i < names.Length; i++)
            {
                Shader s = Shader.Find(names[i]);
                if (s != null) return s;
            }
            return null;   // 交给 SpriteRenderer 的默认材质
        }

        static Texture2D MakeRingTex()
        {
            Texture2D tex = NewTex();
            Color32[] px = new Color32[TexSize * TexSize];
            float half = TexSize * 0.5f;
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.82f) / 0.2f);
                    px[y * TexSize + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static Texture2D MakeDiscTex()
        {
            Texture2D tex = NewTex();
            Color32[] px = new Color32[TexSize * TexSize];
            float half = TexSize * 0.5f;
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float a = (1f - r) * (1f - r);                         // 柔和内芯
                    px[y * TexSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static Texture2D NewTex()
        {
            Texture2D tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }
    }
}
