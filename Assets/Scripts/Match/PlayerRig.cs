using UnityEngine;

namespace FD
{
    public enum RigAction { None, Kick, Throw, StandTackle, Slide, Fall, Dive }
    public enum Celebration { None, Airplane, FistPump, Jump, Despair }

    /// <summary>Параметры позы на кадр — заполняет FootballPlayer.</summary>
    public struct RigContext
    {
        public float speed;          // горизонтальная скорость, м/с
        public bool sprint;
        public bool gkReady;         // вратарь в стойке
        public bool holdBall;        // вратарь держит мяч в руках
        public bool throwIn;         // вбрасывание из-за головы
        public Vector3 lookAt;       // куда смотрит голова (мир)
        public Celebration celebrate;
    }

    /// <summary>
    /// Скелет из примитивов: таз → корпус → голова/руки (плечо-локоть), ноги (бедро-колено-стопа).
    /// Вся анимация процедурная: бег, удар, подкат, падение, прыжок вратаря, празднования.
    /// </summary>
    public class PlayerRig
    {
        public Transform root;
        Transform pelvis, spine, head, shL, shR, elL, elR, hipL, hipR, knL, knR;
        float phase, t, actionTime, actionDur, idleSeed;
        RigAction action;
        float diveSide;

        public RigAction Action => action;
        public bool Busy => action != RigAction.None;

        public static PlayerRig Build(Transform parent, PlayerData data, Color kit, Color shorts, Color socks, bool gk)
        {
            var rig = new PlayerRig();
            var rng = new System.Random(data.id * 31 + 7);
            Color[] skins = { new Color(0.98f, 0.82f, 0.68f), new Color(0.88f, 0.67f, 0.5f), new Color(0.66f, 0.46f, 0.31f), new Color(0.42f, 0.28f, 0.18f), new Color(0.3f, 0.2f, 0.13f) };
            Color[] hairs = { new Color(0.08f, 0.06f, 0.05f), new Color(0.3f, 0.18f, 0.09f), new Color(0.75f, 0.6f, 0.32f), new Color(0.04f, 0.04f, 0.04f), new Color(0.55f, 0.25f, 0.1f) };
            Color skin = skins[rng.Next(skins.Length)];
            Color hair = hairs[rng.Next(hairs.Length)];
            Color boots = new[] { new Color(0.08f, 0.08f, 0.08f), new Color(0.95f, 0.95f, 0.95f), new Color(0.9f, 0.3f, 0.1f), new Color(0.2f, 0.6f, 1f) }[rng.Next(4)];
            Color gloves = new Color(0.95f, 0.95f, 0.95f);
            rig.idleSeed = (float)rng.NextDouble() * 10f;

            rig.root = new GameObject("rig").transform;
            rig.root.SetParent(parent, false);

            rig.pelvis = Node("pelvis", rig.root, new Vector3(0f, 0.95f, 0f));
            U.Prim(PrimitiveType.Cube, rig.pelvis, new Vector3(0f, -0.03f, 0f), new Vector3(0.38f, 0.24f, 0.25f), shorts, "shorts");

            rig.spine = Node("spine", rig.pelvis, new Vector3(0f, 0.05f, 0f));
            U.Prim(PrimitiveType.Capsule, rig.spine, new Vector3(0f, 0.3f, 0f), new Vector3(0.46f, 0.32f, 0.28f), kit, "chest");
            U.Prim(PrimitiveType.Cylinder, rig.spine, new Vector3(0f, 0.63f, 0f), new Vector3(0.11f, 0.05f, 0.11f), skin, "neck");

            // номер на спине
            var num = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(num.GetComponent<Collider>());
            num.name = "number";
            num.transform.SetParent(rig.spine, false);
            num.transform.localPosition = new Vector3(0f, 0.36f, -0.146f);
            num.transform.localRotation = Quaternion.identity;   // quad смотрит в -z = назад
            num.transform.localScale = new Vector3(0.24f, 0.24f, 1f);
            num.GetComponent<Renderer>().sharedMaterial = U.TexMat(NumberTexture(data.number, kit), Vector2.one);
            num.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            rig.head = Node("head", rig.spine, new Vector3(0f, 0.78f, 0f));
            U.Prim(PrimitiveType.Sphere, rig.head, Vector3.zero, new Vector3(0.26f, 0.3f, 0.27f), skin, "face");
            switch (rng.Next(4))
            {
                case 0: U.Prim(PrimitiveType.Sphere, rig.head, new Vector3(0f, 0.07f, -0.015f), new Vector3(0.28f, 0.18f, 0.29f), hair, "hair"); break;
                case 1:
                    U.Prim(PrimitiveType.Sphere, rig.head, new Vector3(0f, 0.05f, -0.03f), new Vector3(0.29f, 0.24f, 0.29f), hair, "hair");
                    U.Prim(PrimitiveType.Sphere, rig.head, new Vector3(0f, -0.04f, -0.09f), new Vector3(0.24f, 0.24f, 0.14f), hair, "hairBack");
                    break;
                case 2: U.Prim(PrimitiveType.Cube, rig.head, new Vector3(0f, 0.13f, -0.01f), new Vector3(0.07f, 0.08f, 0.26f), hair, "mohawk"); break;
                default: U.Prim(PrimitiveType.Sphere, rig.head, new Vector3(0f, 0.09f, -0.01f), new Vector3(0.265f, 0.12f, 0.275f), hair, "buzz"); break;
            }

            Color sleeve = kit;
            Color hand = gk ? gloves : skin;
            rig.shL = Node("shL", rig.spine, new Vector3(-0.27f, 0.54f, 0f));
            U.Prim(PrimitiveType.Capsule, rig.shL, new Vector3(0f, -0.14f, 0f), new Vector3(0.13f, 0.16f, 0.13f), sleeve, "upperArm");
            rig.elL = Node("elL", rig.shL, new Vector3(0f, -0.29f, 0f));
            U.Prim(PrimitiveType.Capsule, rig.elL, new Vector3(0f, -0.12f, 0f), new Vector3(0.1f, 0.13f, 0.1f), gk ? kit : skin, "forearm");
            U.Prim(PrimitiveType.Sphere, rig.elL, new Vector3(0f, -0.27f, 0f), Vector3.one * (gk ? 0.14f : 0.1f), hand, "hand");

            rig.shR = Node("shR", rig.spine, new Vector3(0.27f, 0.54f, 0f));
            U.Prim(PrimitiveType.Capsule, rig.shR, new Vector3(0f, -0.14f, 0f), new Vector3(0.13f, 0.16f, 0.13f), sleeve, "upperArm");
            rig.elR = Node("elR", rig.shR, new Vector3(0f, -0.29f, 0f));
            U.Prim(PrimitiveType.Capsule, rig.elR, new Vector3(0f, -0.12f, 0f), new Vector3(0.1f, 0.13f, 0.1f), gk ? kit : skin, "forearm");
            U.Prim(PrimitiveType.Sphere, rig.elR, new Vector3(0f, -0.27f, 0f), Vector3.one * (gk ? 0.14f : 0.1f), hand, "hand");

            rig.hipL = Leg(rig.pelvis, -0.11f, skin, socks, boots, out rig.knL);
            rig.hipR = Leg(rig.pelvis, 0.11f, skin, socks, boots, out rig.knR);
            return rig;
        }

        static Transform Node(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static Transform Leg(Transform pelvis, float x, Color skin, Color socks, Color boots, out Transform knee)
        {
            var hip = Node("hip", pelvis, new Vector3(x, -0.05f, 0f));
            U.Prim(PrimitiveType.Capsule, hip, new Vector3(0f, -0.2f, 0f), new Vector3(0.16f, 0.23f, 0.16f), skin, "thigh");
            knee = Node("knee", hip, new Vector3(0f, -0.43f, 0f));
            U.Prim(PrimitiveType.Capsule, knee, new Vector3(0f, -0.2f, 0f), new Vector3(0.135f, 0.22f, 0.135f), socks, "shin");
            U.Prim(PrimitiveType.Cube, knee, new Vector3(0f, -0.42f, 0.05f), new Vector3(0.11f, 0.08f, 0.27f), boots, "boot");
            return hip;
        }

        // 5x7 пиксельные цифры для номеров
        static readonly string[] Digits =
        {
            "01110100011001110101110011000101110", "00100011000010000100001000010001110", "01110100010000100010001000100011111",
            "11111000100010000010000011000101110", "00010001100101010010111110001000010", "11111100001111000001000011000101110",
            "00110010001000011110100011000101110", "11111000010001000100010000100001000", "01110100011000101110100011000101110",
            "01110100011000101111000010001001100"
        };

        static Texture2D NumberTexture(int number, Color kit)
        {
            const int w = 32, h = 32;
            var tex = new Texture2D(w, h) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float lum = kit.r * 0.3f + kit.g * 0.59f + kit.b * 0.11f;
            Color ink = lum > 0.55f ? new Color(0.08f, 0.08f, 0.12f) : Color.white;
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = kit;
            string s = Mathf.Clamp(number, 0, 99).ToString();
            int scale = 2, gw = 5 * scale, gap = 2;
            int total = s.Length * gw + (s.Length - 1) * gap;
            int x0 = (w - total) / 2, y0 = (h - 7 * scale) / 2;
            for (int c = 0; c < s.Length; c++)
            {
                string g = Digits[s[c] - '0'];
                for (int gy = 0; gy < 7; gy++)
                    for (int gx = 0; gx < 5; gx++)
                    {
                        if (g[gy * 5 + gx] != '1') continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px0 = x0 + c * (gw + gap) + gx * scale + sx;
                                int py0 = h - 1 - (y0 + gy * scale + sy);
                                px[py0 * w + px0] = ink;
                            }
                    }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ---------------------------------------------------------------- действия

        public void Play(RigAction a, float duration, float side = 1f)
        {
            action = a;
            actionTime = 0f;
            actionDur = duration;
            diveSide = side;
        }

        public void Tick(float dt, RigContext c, Transform body)
        {
            t += dt;
            if (action != RigAction.None)
            {
                actionTime += dt;
                if (actionTime >= actionDur) action = RigAction.None;
            }

            float a = Mathf.Clamp01(c.speed / 6.5f);
            float sp = c.sprint ? Mathf.Clamp01((c.speed - 6f) / 3f) : 0f;
            phase += c.speed * 1.35f * dt;
            float s = Mathf.Sin(phase), co = Mathf.Cos(phase);

            // ---- базовый бег / стойка
            float thL = -s * (26f + 22f * sp) * a;
            float thR = s * (26f + 22f * sp) * a;
            float knLv = a * (8f + (58f + 20f * sp) * Mathf.Max(0f, co)) + 4f;
            float knRv = a * (8f + (58f + 20f * sp) * Mathf.Max(0f, -co)) + 4f;
            float shLx = s * 38f * a, shRx = -s * 38f * a;
            float shLz = -8f, shRz = 8f;
            float elLv = -(15f + 55f * a), elRv = -(15f + 55f * a);
            float lean = 4f + 9f * a + 9f * sp;
            float pelvisY = 0.95f - 0.04f * a + Mathf.Abs(s) * 0.045f * a;
            float breathe = Mathf.Sin((t + idleSeed) * 2.2f) * (1f - a);
            lean += breathe * 1.2f;
            Vector3 rootPos = Vector3.zero;
            Vector3 rootRot = Vector3.zero;

            if (c.gkReady && a < 0.6f)
            {
                float k = 1f - a;
                thL = Mathf.Lerp(thL, -28f, k); thR = Mathf.Lerp(thR, -28f, k);
                knLv = Mathf.Lerp(knLv, 50f, k); knRv = Mathf.Lerp(knRv, 50f, k);
                shLx = Mathf.Lerp(shLx, -45f, k); shRx = Mathf.Lerp(shRx, -45f, k);
                shLz = -28f; shRz = 28f;
                elLv = elRv = -35f;
                lean = Mathf.Lerp(lean, 24f, k);
                pelvisY = Mathf.Lerp(pelvisY, 0.84f, k);
                thL -= 0f;
                // стойка «шире»: небольшое разведение ног через позицию бёдер не делаем — хватает наклона
            }

            if (c.holdBall)
            {
                shLx = shRx = -65f; elLv = elRv = -70f; shLz = -12f; shRz = 12f;
            }
            if (c.throwIn)
            {
                shLx = shRx = -170f; elLv = elRv = -45f; shLz = -10f; shRz = 10f; lean = -8f;
            }

            // ---- празднования
            switch (c.celebrate)
            {
                case Celebration.Airplane:
                    shLx = shRx = 0f; shLz = -85f; shRz = 85f; elLv = elRv = 0f;
                    rootRot.z = Mathf.Sin(t * 3f) * 12f;
                    break;
                case Celebration.FistPump:
                    shLx = -165f + Mathf.Sin(t * 9f) * 15f; shRx = -165f - Mathf.Sin(t * 9f) * 15f;
                    elLv = elRv = -30f; shLz = -15f; shRz = 15f;
                    break;
                case Celebration.Jump:
                {
                    float j = Mathf.Abs(Mathf.Sin((t + idleSeed) * 5f));
                    rootPos.y = j * 0.45f;
                    shLx = shRx = -170f; elLv = elRv = -10f; shLz = -20f; shRz = 20f;
                    knLv = knRv = (1f - j) * 50f; thL = thR = -(1f - j) * 25f;
                    break;
                }
                case Celebration.Despair:
                    shLx = shRx = -150f; elLv = elRv = -115f; shLz = -35f; shRz = 35f; lean = 18f;
                    break;
            }

            // ---- действия поверх
            float u = actionDur > 0f ? Mathf.Clamp01(actionTime / actionDur) : 1f;
            switch (action)
            {
                case RigAction.Kick:
                {
                    float strike = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.35f));
                    float back = Mathf.Clamp01((u - 0.35f) / 0.65f);
                    thR = Mathf.Lerp(Mathf.Lerp(35f, -80f, strike), 0f, back);
                    knRv = Mathf.Lerp(Mathf.Lerp(80f, 5f, strike), knRv, back);
                    thL = Mathf.Lerp(thL, 8f, 1f - back);
                    shLz = Mathf.Lerp(-60f, shLz, back); shRz = Mathf.Lerp(40f, shRz, back);
                    shLx = Mathf.Lerp(-30f, shLx, back);
                    lean = Mathf.Lerp(-10f, lean, back);
                    break;
                }
                case RigAction.Throw:
                {
                    float k = Mathf.SmoothStep(0f, 1f, u);
                    shLx = shRx = Mathf.Lerp(-175f, -40f, k); elLv = elRv = Mathf.Lerp(-60f, -5f, k);
                    lean = Mathf.Lerp(-15f, 15f, k);
                    break;
                }
                case RigAction.StandTackle:
                {
                    float k = Mathf.Sin(u * Mathf.PI);
                    thR = Mathf.Lerp(thR, -60f, k); knRv = Mathf.Lerp(knRv, 5f, k);
                    lean = Mathf.Lerp(lean, 25f, k);
                    shLz = -40f * k; shRz = 40f * k;
                    break;
                }
                case RigAction.Slide:
                {
                    float k = u < 0.15f ? u / 0.15f : u > 0.75f ? 1f - (u - 0.75f) / 0.25f : 1f;
                    k = Mathf.SmoothStep(0f, 1f, k);
                    rootRot.x = -62f * k;
                    rootPos.y = -0.55f * k;
                    thR = Mathf.Lerp(thR, -95f, k); knRv = Mathf.Lerp(knRv, 0f, k);
                    thL = Mathf.Lerp(thL, -40f, k); knLv = Mathf.Lerp(knLv, 95f, k);
                    shLx = Mathf.Lerp(shLx, 40f, k); shRx = Mathf.Lerp(shRx, 20f, k);
                    shLz = -50f * k; shRz = 30f * k;
                    lean = Mathf.Lerp(lean, 10f, k);
                    break;
                }
                case RigAction.Fall:
                {
                    float k = u < 0.35f ? u / 0.35f : 1f - (u - 0.35f) / 0.65f;
                    k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
                    rootRot.x = 75f * k;
                    rootRot.z = 20f * k * diveSide;
                    rootPos.y = -0.25f * k;
                    shLx = shRx = Mathf.Lerp(shLx, -80f, k);
                    thL = thR = Mathf.Lerp(thL, 20f, k);
                    break;
                }
                case RigAction.Dive:
                {
                    float k = Mathf.Sin(Mathf.Clamp01(u * 1.2f) * Mathf.PI * 0.5f);
                    if (u > 0.7f) k *= 1f - (u - 0.7f) / 0.3f;
                    rootRot.z = -diveSide * 82f * k;
                    rootPos = new Vector3(diveSide * 0.75f * k, 0.35f * Mathf.Sin(u * Mathf.PI), 0f);
                    shLx = shRx = Mathf.Lerp(shLx, -178f, k);
                    elLv = elRv = -5f;
                    thL = thR = Mathf.Lerp(thL, 5f, k);
                    knLv = knRv = Mathf.Lerp(knLv, 10f, k);
                    lean = Mathf.Lerp(lean, 0f, k);
                    break;
                }
            }

            // ---- применение
            root.localPosition = rootPos;
            root.localRotation = Quaternion.Euler(rootRot);
            pelvis.localPosition = new Vector3(0f, pelvisY, 0f);
            spine.localRotation = Quaternion.Euler(lean, Mathf.Sin(phase) * 6f * a, 0f);
            hipL.localRotation = Quaternion.Euler(thL, 0f, 0f);
            hipR.localRotation = Quaternion.Euler(thR, 0f, 0f);
            knL.localRotation = Quaternion.Euler(knLv, 0f, 0f);
            knR.localRotation = Quaternion.Euler(knRv, 0f, 0f);
            shL.localRotation = Quaternion.Euler(shLx, 0f, shLz);
            shR.localRotation = Quaternion.Euler(shRx, 0f, shRz);
            elL.localRotation = Quaternion.Euler(elLv, 0f, 0f);
            elR.localRotation = Quaternion.Euler(elRv, 0f, 0f);

            // голова следит за мячом
            Vector3 look = c.lookAt - head.position;
            look.y = 0f;
            float yaw = 0f;
            if (look.sqrMagnitude > 0.5f)
            {
                Vector3 local = body.InverseTransformDirection(look.normalized);
                yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -65f, 65f);
            }
            float pitch = -lean * 0.6f + (c.celebrate == Celebration.Despair ? 30f : 0f);
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(pitch, yaw, 0f), 1f - Mathf.Exp(-8f * dt));
        }
    }
}
