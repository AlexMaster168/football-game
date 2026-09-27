using System.Collections.Generic;
using UnityEngine;

namespace FD
{
    /// <summary>
    /// Стадион: газон — одна процедурная текстура (полосы покоса, шум, сглаженная разметка),
    /// двухъярусные трибуны с крышей и «живой» толпой, прожекторы, ворота с сеткой, конфетти.
    /// </summary>
    public static class Stadium
    {
        const float L = MatchManager.HalfL, W = MatchManager.HalfW;

        static readonly List<Material> crowdMats = new List<Material>();
        static Texture2D crowdTex;
        static readonly Transform[] nets = new Transform[2];
        static readonly float[] netShake = new float[2];
        static ParticleSystem confetti;
        static float excitement, crowdPhase;

        public static Transform Build()
        {
            var root = new GameObject("Stadium").transform;

            U.Prim(PrimitiveType.Cube, root, new Vector3(0, -0.15f, 0), new Vector3(220, 0.2f, 200), new Color(0.16f, 0.36f, 0.14f), "Ground");
            BuildPitch(root);

            for (int e = -1; e <= 1; e += 2)
            {
                Goal(root, e);
                for (int s = -1; s <= 1; s += 2)
                {
                    U.Prim(PrimitiveType.Cylinder, root, new Vector3(s * W, 0.75f, e * L), new Vector3(0.05f, 0.75f, 0.05f), Color.white, "Flag");
                    U.Prim(PrimitiveType.Cube, root, new Vector3(s * W, 1.32f, e * L - e * 0.22f), new Vector3(0.02f, 0.32f, 0.42f), new Color(1f, 0.78f, 0.1f), "FlagCloth");
                }
            }

            AdBoards(root);

            crowdTex = CrowdTexture(new Color(0.9f, 0.9f, 0.9f), new Color(0.2f, 0.3f, 0.8f));
            // дальняя трибуна (её видно с телевизионной камеры) — самая богатая
            LongStand(root, -1, true);
            LongStand(root, 1, false);
            EndStand(root, 1);
            EndStand(root, -1);

            // мачты освещения
            var lamp = U.Emissive(new Color(1f, 0.97f, 0.85f), 2.2f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var bp = new Vector3(sx * (W + 40f), 0, sz * (L + 34f));
                    U.Prim(PrimitiveType.Cylinder, root, bp + Vector3.up * 24f, new Vector3(1.2f, 24f, 1.2f), new Color(0.55f, 0.57f, 0.62f), "Mast");
                    var head = new GameObject("LampHead").transform;
                    head.SetParent(root, false);
                    head.position = bp + Vector3.up * 49f;
                    head.rotation = Quaternion.LookRotation(-bp.normalized + Vector3.down * 0.6f);
                    U.Prim(PrimitiveType.Cube, head, Vector3.zero, new Vector3(9f, 4.5f, 0.8f), new Color(0.3f, 0.32f, 0.36f), "Frame");
                    for (int i = 0; i < 4; i++)
                        for (int j = 0; j < 2; j++)
                        {
                            var l = U.Prim(PrimitiveType.Cube, head, new Vector3(-3.3f + i * 2.2f, -1f + j * 2f, 0.45f), new Vector3(1.8f, 1.6f, 0.1f), Color.white, "Lamp");
                            l.GetComponent<Renderer>().sharedMaterial = lamp;
                        }
                }

            BuildConfetti(root);
            return root;
        }

        // ================================================================ газон

        static void BuildPitch(Transform root)
        {
            const int TW = 1400, TH = 2048;
            const float X0 = -38f, Z0 = -56f, SX = 76f, SZ = 112f;
            float pxm = TW / SX;
            var px = new Color32[TW * TH];
            var light = new Color(0.22f, 0.46f, 0.16f);
            var dark = new Color(0.185f, 0.40f, 0.135f);
            var worn = new Color(0.42f, 0.45f, 0.24f);

            for (int j = 0; j < TH; j++)
            {
                float z = Z0 + (j + 0.5f) * SZ / TH;
                int stripe = Mathf.FloorToInt((z + L) / (2f * L / 14f));
                Color baseC = (stripe & 1) == 0 ? light : dark;
                for (int i = 0; i < TW; i++)
                {
                    float x = X0 + (i + 0.5f) * SX / TW;
                    float n = Mathf.PerlinNoise(x * 0.16f + 3.1f, z * 0.16f + 7.7f) * 0.12f
                              + Mathf.PerlinNoise(x * 1.3f + 11f, z * 1.3f) * 0.06f
                              + Hash(i, j) * 0.07f - 0.12f;
                    Color c = baseC * (1f + n);
                    c *= ((Mathf.FloorToInt((x + 40f) / 8f) & 1) == 0) ? 1.018f : 0.982f;
                    // вытоптанные вратарские и центр
                    float wg = Mathf.Exp(-(x * x) / 30f - Sq(Mathf.Abs(z) - (L - 3f)) / 12f);
                    float wc = Mathf.Exp(-(x * x + z * z) / 20f) * 0.5f;
                    c = Color.Lerp(c, worn * (1f + n), Mathf.Clamp01((wg + wc) * 0.45f));
                    px[j * TW + i] = c;
                }
            }

            var lineCol = new Color(0.96f, 0.96f, 0.93f);
            void Seg(Vector2 a, Vector2 b) => DrawSegment(px, TW, TH, X0, Z0, SX, SZ, pxm, a, b, 0.065f, lineCol);
            void Arc(Vector2 c, float r, float from, float to)
            {
                int seg = Mathf.Max(8, Mathf.RoundToInt((to - from) / 4f));
                for (int k = 0; k < seg; k++)
                {
                    float a0 = (from + (to - from) * k / seg) * Mathf.Deg2Rad, a1 = (from + (to - from) * (k + 1) / seg) * Mathf.Deg2Rad;
                    Seg(c + new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * r, c + new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * r);
                }
            }
            void Spot(Vector2 c, float r) => DrawSegment(px, TW, TH, X0, Z0, SX, SZ, pxm, c, c, r, lineCol);

            Seg(new Vector2(-W, -L), new Vector2(W, -L));
            Seg(new Vector2(-W, L), new Vector2(W, L));
            Seg(new Vector2(-W, -L), new Vector2(-W, L));
            Seg(new Vector2(W, -L), new Vector2(W, L));
            Seg(new Vector2(-W, 0), new Vector2(W, 0));
            Arc(Vector2.zero, 9.15f, 0f, 360f);
            Spot(Vector2.zero, 0.18f);
            for (int e = -1; e <= 1; e += 2)
            {
                float gz = e * L;
                foreach (var (hw, dep) in new[] { (20.16f, 16.5f), (9.16f, 5.5f) })
                {
                    float z2 = gz - e * dep;
                    Seg(new Vector2(-hw, gz), new Vector2(-hw, z2));
                    Seg(new Vector2(hw, gz), new Vector2(hw, z2));
                    Seg(new Vector2(-hw, z2), new Vector2(hw, z2));
                }
                var spot = new Vector2(0, gz - e * 11f);
                Spot(spot, 0.14f);
                float a = Mathf.Acos(5.5f / 9.15f) * Mathf.Rad2Deg;
                float baseAng = e > 0 ? 180f : 0f;
                Arc(spot, 9.15f, baseAng - a, baseAng + a);
                for (int s = -1; s <= 1; s += 2)
                {
                    // угловые дуги
                    float start = e > 0 ? (s > 0 ? 180f : 90f) : (s > 0 ? 270f : 0f);
                    Arc(new Vector2(s * W, gz), 1f, start, start + 90f);
                }
            }

            var tex = new Texture2D(TW, TH, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Trilinear,
                anisoLevel = 9,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "Pitch";
            q.transform.SetParent(root, false);
            q.transform.position = new Vector3(X0 + SX / 2f, 0.001f, Z0 + SZ / 2f);
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(SX, SZ, 1f);
            var mat = U.TexMat(tex, Vector2.one);
            mat.SetFloat("_Glossiness", 0.12f);
            q.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static float Sq(float v) => v * v;

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }

        static void DrawSegment(Color32[] px, int tw, int th, float x0, float z0, float sx, float sz, float pxm,
                                Vector2 a, Vector2 b, float halfW, Color col)
        {
            float pad = halfW + 2f / pxm;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - pad - x0) / sx * tw));
            int i1 = Mathf.Min(tw - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + pad - x0) / sx * tw));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - pad - z0) / sz * th));
            int j1 = Mathf.Min(th - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + pad - z0) / sz * th));
            Vector2 ab = b - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
            for (int j = j0; j <= j1; j++)
            {
                float z = z0 + (j + 0.5f) * sz / th;
                for (int i = i0; i <= i1; i++)
                {
                    float x = x0 + (i + 0.5f) * sx / tw;
                    var p = new Vector2(x, z);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float d = Vector2.Distance(p, a + ab * t);
                    float cov = Mathf.Clamp01((halfW - d) * pxm + 0.5f);
                    if (cov <= 0f) continue;
                    int k = j * tw + i;
                    px[k] = Color.Lerp(px[k], col, cov * 0.93f);
                }
            }
        }

        // ================================================================ ворота

        static void Goal(Transform root, int e)
        {
            float gz = e * L, gw = MatchManager.GoalHalfW, gh = MatchManager.GoalH, depth = 1.9f;
            var post = Color.white;
            U.Prim(PrimitiveType.Cylinder, root, new Vector3(-gw, gh / 2, gz), new Vector3(0.12f, gh / 2, 0.12f), post, "Post");
            U.Prim(PrimitiveType.Cylinder, root, new Vector3(gw, gh / 2, gz), new Vector3(0.12f, gh / 2, 0.12f), post, "Post");
            var bar = U.Prim(PrimitiveType.Cylinder, root, new Vector3(0, gh, gz), new Vector3(0.12f, gw + 0.06f, 0.12f), post, "Crossbar");
            bar.transform.rotation = Quaternion.Euler(0, 0, 90);
            // задние стойки каркаса
            for (int s = -1; s <= 1; s += 2)
                U.Prim(PrimitiveType.Cylinder, root, new Vector3(s * gw, gh / 2 - 0.3f, gz + e * depth), new Vector3(0.05f, gh / 2 - 0.3f, 0.05f), new Color(0.8f, 0.8f, 0.82f), "BackPost");

            var net = new GameObject("Net").transform;
            net.SetParent(root, false);
            net.position = new Vector3(0, 0, gz);
            nets[e > 0 ? 1 : 0] = net;
            var col = new Color(0.9f, 0.9f, 0.92f);
            float step = 0.3f;
            for (float x = -gw; x <= gw + 0.01f; x += step)
            {
                NetBar(net, new Vector3(x, (gh - 0.6f) / 2, e * depth), new Vector3(0.018f, gh - 0.6f, 0.018f), col);
                NetBar(net, new Vector3(x, gh - 0.3f, e * depth * 0.5f), new Vector3(0.018f, 0.018f, depth), col, new Vector3(-e * 9f, 0, 0));
            }
            for (float y = 0.15f; y <= gh - 0.59f; y += step)
                NetBar(net, new Vector3(0, y, e * depth), new Vector3(gw * 2, 0.018f, 0.018f), col);
            for (float z = 0f; z <= depth + 0.01f; z += step)
            {
                float top = gh - 0.6f * (z / depth);
                NetBar(net, new Vector3(0, top, e * z), new Vector3(gw * 2, 0.018f, 0.018f), col);
                for (int s = -1; s <= 1; s += 2)
                    NetBar(net, new Vector3(s * gw, top / 2, e * z), new Vector3(0.018f, top, 0.018f), col);
            }
            for (float y = 0.15f; y <= gh; y += step)
                for (int s = -1; s <= 1; s += 2)
                    NetBar(net, new Vector3(s * gw, y, e * depth / 2), new Vector3(0.018f, 0.018f, depth), col);
        }

        static void NetBar(Transform root, Vector3 pos, Vector3 scale, Color c, Vector3 euler = default)
        {
            var g = U.Prim(PrimitiveType.Cube, root, pos, scale, c, "n");
            if (euler != default) g.transform.localRotation = Quaternion.Euler(euler);
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ================================================================ трибуны

        static void AdBoards(Transform root)
        {
            Color[] ad =
            {
                new Color(0.08f, 0.25f, 0.75f), new Color(0.92f, 0.92f, 0.95f), new Color(0.85f, 0.1f, 0.2f),
                new Color(0.05f, 0.55f, 0.3f), new Color(0.98f, 0.75f, 0.1f), new Color(0.1f, 0.1f, 0.12f)
            };
            var mats = new Material[ad.Length];
            for (int i = 0; i < ad.Length; i++) mats[i] = U.Emissive(ad[i], 0.35f);
            int n = 0;
            for (float z = -L; z < L; z += 10f, n++)
            {
                Board(root, new Vector3(W + 4.5f, 0.5f, z + 5f), new Vector3(0.25f, 1f, 9.8f), mats[n % mats.Length]);
                Board(root, new Vector3(-W - 4.5f, 0.5f, z + 5f), new Vector3(0.25f, 1f, 9.8f), mats[(n + 3) % mats.Length]);
            }
            for (float x = -W; x < W; x += 8f, n++)
            {
                if (Mathf.Abs(x + 4f) < 6f) continue;   // за воротами проход
                Board(root, new Vector3(x + 4f, 0.5f, L + 5.5f), new Vector3(7.8f, 1f, 0.25f), mats[n % mats.Length]);
                Board(root, new Vector3(x + 4f, 0.5f, -L - 5.5f), new Vector3(7.8f, 1f, 0.25f), mats[(n + 2) % mats.Length]);
            }
        }

        static void Board(Transform root, Vector3 pos, Vector3 scale, Material m)
        {
            var g = U.Prim(PrimitiveType.Cube, root, pos, scale, Color.white, "Ad");
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        /// <summary>Длинная трибуна вдоль бровки. side: -1 дальняя (x&lt;0), +1 ближняя.</summary>
        static void LongStand(Transform root, int side, bool twoTier)
        {
            float len = 2 * L + 24f;
            var concrete = new Color(0.36f, 0.38f, 0.43f);
            var darkC = new Color(0.18f, 0.2f, 0.24f);
            // нижний ярус
            SlopeX(root, side, W + 7f, W + 27f, 1.2f, 12f, len, true);
            U.Prim(PrimitiveType.Cube, root, new Vector3(side * (W + 6.8f), 0.8f, 0), new Vector3(0.4f, 1.6f, len), darkC, "FrontWall");
            if (!twoTier)
            {
                U.Prim(PrimitiveType.Cube, root, new Vector3(side * (W + 29f), 9f, 0), new Vector3(2f, 18f, len + 4), concrete, "BackWall");
                return;
            }
            // проход между ярусами
            U.Prim(PrimitiveType.Cube, root, new Vector3(side * (W + 28.5f), 12.5f, 0), new Vector3(3f, 1.2f, len), darkC, "Concourse");
            U.Prim(PrimitiveType.Cube, root, new Vector3(side * (W + 29.9f), 14.2f, 0), new Vector3(0.3f, 2.2f, len), new Color(0.1f, 0.25f, 0.6f), "Band");
            // верхний ярус
            SlopeX(root, side, W + 30f, W + 50f, 14f, 27f, len, true);
            U.Prim(PrimitiveType.Cube, root, new Vector3(side * (W + 51f), 16f, 0), new Vector3(2f, 32f, len + 4), concrete, "BackWall");
            // крыша с подсветкой
            var roof = U.Prim(PrimitiveType.Cube, root, new Vector3(side * (W + 32f), 33f, 0), new Vector3(42f, 0.8f, len + 6), new Color(0.72f, 0.74f, 0.78f), "Roof");
            roof.transform.rotation = Quaternion.Euler(0, 0, side * -6f);
            var strip = U.Prim(PrimitiveType.Cube, root, new Vector3(side * (W + 12f), 30.6f, 0), new Vector3(1.2f, 0.3f, len), Color.white, "RoofLights");
            strip.GetComponent<Renderer>().sharedMaterial = U.Emissive(new Color(1f, 0.98f, 0.9f), 1.8f);
            for (float z = -len / 2 + 10; z < len / 2; z += 22f)
                U.Prim(PrimitiveType.Cylinder, root, new Vector3(side * (W + 50f), 26f, z), new Vector3(0.8f, 8f, 0.8f), concrete, "Column");
        }

        static void EndStand(Transform root, int end)
        {
            float wid = 2 * W + 20f;
            SlopeZ(root, end, L + 8f, L + 30f, 1.2f, 15f, wid);
            U.Prim(PrimitiveType.Cube, root, new Vector3(0, 0.8f, end * (L + 7.8f)), new Vector3(wid, 1.6f, 0.4f), new Color(0.18f, 0.2f, 0.24f), "FrontWall");
            U.Prim(PrimitiveType.Cube, root, new Vector3(0, 11f, end * (L + 32f)), new Vector3(wid + 30f, 22f, 2f), new Color(0.36f, 0.38f, 0.43f), "BackWall");
            // экран-табло
            var scr = U.Prim(PrimitiveType.Cube, root, new Vector3(-W * 0.4f, 19f, end * (L + 30.8f)), new Vector3(16f, 7f, 0.4f), Color.white, "Screen");
            scr.GetComponent<Renderer>().sharedMaterial = U.Emissive(new Color(0.05f, 0.12f, 0.3f), 1.2f);
            // угловые секторы
            for (int s = -1; s <= 1; s += 2)
            {
                var c = MakeStand(root, "Corner");
                c.position = new Vector3(s * (W + 16f), 7f, end * (L + 16f));
                c.localScale = new Vector3(22f, 0.7f, 22f);
                c.rotation = Quaternion.LookRotation(new Vector3(s, 0, end)) * Quaternion.Euler(-30f, 45f, 0f);
                SetCrowd(c, new Vector2(0.4f, 0.6f));
            }
        }

        static void SlopeX(Transform root, int side, float inner, float outer, float h0, float h1, float len, bool crowd)
        {
            float width = Mathf.Sqrt(Sq(outer - inner) + Sq(h1 - h0));
            float ang = Mathf.Atan2(h1 - h0, outer - inner) * Mathf.Rad2Deg;
            var t = MakeStand(root, "Tier");
            t.position = new Vector3(side * (inner + outer) / 2f, (h0 + h1) / 2f, 0f);
            t.localScale = new Vector3(width, 0.7f, len);
            t.rotation = Quaternion.Euler(0, 0, side * ang);
            if (crowd) SetCrowd(t, new Vector2(width / 34f, len / 48f), true);
        }

        static void SlopeZ(Transform root, int end, float inner, float outer, float h0, float h1, float wid)
        {
            float depth = Mathf.Sqrt(Sq(outer - inner) + Sq(h1 - h0));
            float ang = Mathf.Atan2(h1 - h0, outer - inner) * Mathf.Rad2Deg;
            var t = MakeStand(root, "EndTier");
            t.position = new Vector3(0f, (h0 + h1) / 2f, end * (inner + outer) / 2f);
            t.localScale = new Vector3(wid, 0.7f, depth);
            t.rotation = Quaternion.Euler(-end * ang, 0, 0);
            SetCrowd(t, new Vector2(wid / 48f, depth / 34f));
        }

        static Transform MakeStand(Transform root, string name)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(g.GetComponent<Collider>());
            g.name = name;
            g.transform.SetParent(root, false);
            return g.transform;
        }

        /// <summary>Текстура толпы; rotate — ряды идут вдоль длинной оси (z).</summary>
        static void SetCrowd(Transform t, Vector2 tiling, bool rotate = false)
        {
            var m = U.TexMat(crowdTex, rotate ? new Vector2(tiling.y, tiling.x) : tiling);
            m.SetFloat("_Glossiness", 0f);
            if (rotate)
            {
                // у SlopeX ширина — по локальному x, а ряды должны идти вдоль z: поворачиваем UV через масштаб
                t.GetComponent<Renderer>().sharedMaterial = m;
                var mf = t.GetComponent<MeshFilter>();
                var mesh = Object.Instantiate(mf.sharedMesh);
                var uv = mesh.uv;
                for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(uv[i].y, uv[i].x);
                mesh.uv = uv;
                mf.sharedMesh = mesh;
            }
            else t.GetComponent<Renderer>().sharedMaterial = m;
            crowdMats.Add(m);
        }

        static Texture2D CrowdTexture(Color home, Color away)
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            FillCrowd(tex, home, away);
            return tex;
        }

        static void FillCrowd(Texture2D tex, Color home, Color away)
        {
            int w = tex.width, h = tex.height;
            var px = new Color[w * h];
            Color seat = new Color(0.2f, 0.22f, 0.28f);
            Color[] misc = { Color.white, new Color(0.15f, 0.15f, 0.18f), new Color(0.7f, 0.1f, 0.1f), new Color(0.95f, 0.8f, 0.2f), new Color(0.3f, 0.5f, 0.8f) };
            Color[] skins = { new Color(0.95f, 0.78f, 0.64f), new Color(0.78f, 0.58f, 0.42f), new Color(0.5f, 0.35f, 0.22f) };
            var rng = new System.Random(11);
            for (int i = 0; i < px.Length; i++) px[i] = seat;
            // ряд = 4 пикселя: ступень + тело(2) + голова
            for (int row = 0; row < h / 4; row++)
            {
                for (int x = 0; x < w; x += 2)
                {
                    if (rng.NextDouble() < 0.08) continue;   // пустые места
                    double r = rng.NextDouble();
                    Color shirt = r < 0.45 ? home : r < 0.75 ? away : misc[rng.Next(misc.Length)];
                    shirt *= 0.75f + (float)rng.NextDouble() * 0.3f;
                    shirt.a = 1f;
                    Color skin = skins[rng.Next(skins.Length)];
                    int y = row * 4;
                    int off = rng.Next(0, 2);
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int xx = (x + dx) % w;
                        px[(y + 1) * w + xx] = shirt;
                        px[(y + 2) * w + xx] = shirt;
                        if (dx == off) px[(y + 3) * w + xx] = skin;
                        else px[(y + 3) * w + xx] = shirt * 0.8f;
                    }
                    if (rng.NextDouble() < 0.05) // флаг/шарф
                        for (int dx = 0; dx < 2; dx++) px[Mathf.Min(h - 1, y + 4) * w + (x + dx) % w] = r < 0.5 ? home : away;
                }
                for (int x = 0; x < w; x++) px[row * 4 * w + x] = seat * 0.8f;
            }
            tex.SetPixels(px);
            tex.Apply(true);
        }

        /// <summary>Перекрасить болельщиков в цвета команд матча.</summary>
        public static void SetCrowdColors(Color home, Color away)
        {
            if (crowdTex != null) FillCrowd(crowdTex, home, away);
        }

        // ================================================================ эффекты

        static void BuildConfetti(Transform root)
        {
            var go = new GameObject("Confetti");
            go.transform.SetParent(root, false);
            confetti = go.AddComponent<ParticleSystem>();
            confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = confetti.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.25f;
            main.maxParticles = 1500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = confetti.emission;
            em.enabled = false;
            var shape = confetti.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 3f;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var rot = confetti.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var lim = confetti.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.limit = 3f;
            lim.dampen = 0.08f;
            var noise = confetti.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.4f;
            var ren = go.GetComponent<ParticleSystemRenderer>();
            ren.sharedMaterial = U.Unlit();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
        }

        public static void Confetti(int end, Color a, Color b)
        {
            if (confetti == null) return;
            var main = confetti.main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            for (int k = -1; k <= 1; k++)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    applyShapeToPosition = true,
                    position = new Vector3(k * 14f, 2f, end * (L + 9f))
                };
                confetti.Emit(ep, 180);
            }
            var ep2 = new ParticleSystem.EmitParams { applyShapeToPosition = true, position = new Vector3(-W - 8f, 3f, end * L * 0.5f) };
            confetti.Emit(ep2, 160);
        }

        public static void ShakeNet(int end)
        {
            netShake[end > 0 ? 1 : 0] = 1f;
            excitement = 1f;
        }

        public static void Excite(float v) => excitement = Mathf.Max(excitement, v);

        /// <summary>Анимация толпы и сетки — вызывать каждый кадр.</summary>
        public static void Tick(float dt)
        {
            excitement = Mathf.MoveTowards(excitement, 0f, dt * 0.18f);
            crowdPhase += dt * (3f + excitement * 9f);
            float amp = 0.0025f + excitement * 0.012f;
            var off = new Vector2(0f, Mathf.Abs(Mathf.Sin(crowdPhase)) * amp);
            foreach (var m in crowdMats) m.mainTextureOffset = off;

            for (int i = 0; i < 2; i++)
            {
                if (nets[i] == null) continue;
                if (netShake[i] > 0f)
                {
                    netShake[i] = Mathf.Max(0f, netShake[i] - dt * 0.9f);
                    float k = netShake[i];
                    float wob = Mathf.Sin((1f - k) * 30f) * k;
                    nets[i].localScale = new Vector3(1f + wob * 0.03f, 1f - Mathf.Abs(wob) * 0.04f, 1f + wob * 0.28f);
                }
                else nets[i].localScale = Vector3.one;
            }
        }
    }
}
