using System.Collections.Generic;
using UnityEngine;

namespace FD
{
    /// <summary>Мелкие утилиты: материалы, примитивы, цвета, случайности.</summary>
    public static class U
    {
        static Material baseMat;
        static readonly Dictionary<Color, Material> cache = new Dictionary<Color, Material>();

        public static Material BaseMaterial
        {
            get
            {
                if (baseMat == null)
                {
                    // ассет из Resources гарантирует, что шейдер Standard попадёт в билд (иначе всё фиолетовое)
                    var res = Resources.Load<Material>("FD_Base");
                    if (res != null) return baseMat = new Material(res);
                    var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    baseMat = new Material(tmp.GetComponent<Renderer>().sharedMaterial);
                    Object.Destroy(tmp);
                }
                return baseMat;
            }
        }

        public static Material Mat(Color c)
        {
            if (cache.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(BaseMaterial) { color = c };
            m.SetFloat("_Glossiness", 0.15f);
            cache[c] = m;
            return m;
        }

        static Material emissiveBase, unlitBase;

        /// <summary>Светящийся материал (прожекторы, экраны).</summary>
        public static Material Emissive(Color c, float intensity)
        {
            if (emissiveBase == null) emissiveBase = Resources.Load<Material>("FD_Emissive") ?? BaseMaterial;
            var m = new Material(emissiveBase) { color = c };
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            return m;
        }

        /// <summary>Неосвещённый материал с вершинными цветами (шлейф, частицы).</summary>
        public static Material Unlit()
        {
            if (unlitBase == null) unlitBase = Resources.Load<Material>("FD_Unlit");
            return unlitBase != null ? new Material(unlitBase) : new Material(BaseMaterial);
        }

        public static Material TexMat(Texture2D tex, Vector2 tiling)
        {
            var m = new Material(BaseMaterial) { mainTexture = tex, color = Color.white };
            m.mainTextureScale = tiling;
            m.SetFloat("_Glossiness", 0.05f);
            return m;
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color, string name = null)
        {
            var g = GameObject.CreatePrimitive(type);
            Object.Destroy(g.GetComponent<Collider>());
            if (name != null) g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return g;
        }

        public static Color Hex(string h)
        {
            return ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.magenta;
        }

        public static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        public static float Gauss()
        {
            float u1 = 1f - Random.value, u2 = Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public static float FlatDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static float ColorDist(Color a, Color b)
        {
            return Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));
        }

        public static int Poisson(float lambda)
        {
            float l = Mathf.Exp(-lambda), p = 1f;
            int k = 0;
            do { k++; p *= Random.value; } while (p > l && k < 12);
            return k - 1;
        }
    }
}
