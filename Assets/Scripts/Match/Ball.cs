using UnityEngine;

namespace FD
{
    /// <summary>Своя простая физика мяча: гравитация, отскок, качение, штанги, сетка.</summary>
    public class Ball : MonoBehaviour
    {
        public const float R = 0.2f, G = 9.81f;

        public Vector3 vel;
        public FootballPlayer owner, lastKicker;
        public TeamController lastTouch, shotTeam;
        public bool isShot, gkTried, inNet;
        Transform model;
        TrailRenderer trail;

        public Vector3 Pos { get => transform.position; set => transform.position = value; }

        public static Ball Create(Transform parent)
        {
            var go = new GameObject("Ball");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<Ball>();
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(go.transform, false);
            sphere.transform.localScale = Vector3.one * (R * 2f);
            // шахматная текстура — чтобы было видно вращение
            var tex = new Texture2D(8, 8) { filterMode = FilterMode.Point };
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    tex.SetPixel(x, y, ((x / 2 + y / 2) % 2 == 0) ? Color.white : new Color(0.1f, 0.1f, 0.1f));
            tex.Apply();
            sphere.GetComponent<Renderer>().sharedMaterial = U.TexMat(tex, Vector2.one);
            b.model = sphere.transform;
            b.Pos = new Vector3(0f, R, 0f);

            b.trail = go.AddComponent<TrailRenderer>();
            b.trail.sharedMaterial = U.Unlit();
            b.trail.time = 0.22f;
            b.trail.startWidth = R * 1.8f;
            b.trail.endWidth = 0f;
            b.trail.endColor = new Color(1f, 1f, 1f, 0f);
            b.trail.minVertexDistance = 0.2f;
            b.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.trail.emitting = false;
            return b;
        }

        public void MarkShot(TeamController t)
        {
            isShot = true;
            shotTeam = t;
            gkTried = false;
        }

        public void Tick(float dt)
        {
            if (trail != null)
            {
                float spd = vel.magnitude;
                trail.emitting = owner == null && spd > 17f;
                trail.startColor = new Color(1f, 1f, 1f, Mathf.Clamp01((spd - 17f) / 12f) * 0.7f);
            }
            if (owner != null)
            {
                Vector3 target;
                if (owner.isGK) target = owner.Pos + owner.transform.forward * 0.42f + Vector3.up * 1.15f;          // в руках
                else if (owner.throwIn) target = owner.Pos + owner.transform.forward * 0.05f + Vector3.up * 2.15f;  // над головой
                else
                {
                    // дриблинг: мяч чуть «гуляет» перед ногой в такт шагу
                    float sp = U.Flat(owner.velocity).magnitude;
                    float touch = Mathf.Abs(Mathf.Sin(Time.time * (4f + sp))) * Mathf.Min(1f, sp / 6f) * 0.35f;
                    target = owner.Pos + owner.transform.forward * (0.55f + touch);
                    target.y = R;
                }
                Pos = Vector3.Lerp(Pos, target, 1f - Mathf.Exp(-25f * dt));
                vel = owner.velocity;
                Spin(dt);
                return;
            }

            Vector3 p = Pos;
            vel.y -= G * dt;
            vel *= 1f - 0.08f * dt;
            p += vel * dt;

            if (p.y < R)
            {
                p.y = R;
                if (vel.y < -2f)
                {
                    vel.y = -vel.y * 0.5f;
                    vel.x *= 0.85f;
                    vel.z *= 0.85f;
                }
                else
                {
                    vel.y = 0f;
                    Vector3 h = new Vector3(vel.x, 0f, vel.z);
                    float s = h.magnitude;
                    if (s > 0.001f)
                    {
                        float ns = Mathf.Max(0f, s - (1.5f + 0.25f * s) * dt);
                        h *= ns / s;
                        vel.x = h.x;
                        vel.z = h.z;
                    }
                }
            }

            CollideGoal(ref p);
            Pos = p;
            Spin(dt);
        }

        void CollideGoal(ref Vector3 p)
        {
            const float postR = 0.06f;
            for (int e = -1; e <= 1; e += 2)
            {
                float gz = e * MatchManager.HalfL;
                if (Mathf.Abs(p.z - gz) > 2.5f) continue;

                for (int s = -1; s <= 1; s += 2)
                {
                    float px = s * MatchManager.GoalHalfW;
                    if (p.y > MatchManager.GoalH + R) continue;
                    var d = new Vector2(p.x - px, p.z - gz);
                    float m = d.magnitude;
                    if (m < R + postR && m > 0.0001f)
                    {
                        var n = d / m;
                        p.x = px + n.x * (R + postR);
                        p.z = gz + n.y * (R + postR);
                        Reflect(new Vector3(n.x, 0f, n.y));
                        Sfx.Post();
                    }
                }

                if (Mathf.Abs(p.x) < MatchManager.GoalHalfW)
                {
                    var d = new Vector2(p.y - MatchManager.GoalH, p.z - gz);
                    float m = d.magnitude;
                    if (m < R + postR && m > 0.0001f)
                    {
                        var n = d / m;
                        p.y = MatchManager.GoalH + n.x * (R + postR);
                        p.z = gz + n.y * (R + postR);
                        Reflect(new Vector3(0f, n.x, n.y));
                        Sfx.Post();
                    }
                }

                // сетка — только после забитого гола, чтобы не «затягивать» мячи, летящие мимо
                if (inNet && e * (p.z - gz) > 0f)
                {
                    float depth = e * (p.z - gz);
                    if (depth > 1.8f)
                    {
                        p.z = gz + e * 1.8f;
                        if (vel.z * e > 0f) vel.z = -vel.z * 0.1f;
                        vel.x *= 0.5f;
                    }
                    float lim = MatchManager.GoalHalfW - R;
                    if (Mathf.Abs(p.x) > lim) { p.x = Mathf.Sign(p.x) * lim; vel.x *= -0.2f; }
                    if (p.y > MatchManager.GoalH - R) { p.y = MatchManager.GoalH - R; vel.y = -Mathf.Abs(vel.y) * 0.2f; }
                }
            }
        }

        void Reflect(Vector3 n)
        {
            float vn = Vector3.Dot(vel, n);
            if (vn < 0f) vel -= 1.6f * vn * n;
        }

        void Spin(float dt)
        {
            Vector3 h = new Vector3(vel.x, 0f, vel.z);
            float s = h.magnitude;
            if (s < 0.05f) return;
            Vector3 axis = Vector3.Cross(Vector3.up, h / s);
            model.Rotate(axis, s / R * Mathf.Rad2Deg * dt, Space.World);
        }
    }
}
