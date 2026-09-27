using UnityEngine;

namespace FD
{
    /// <summary>Процедурные звуки — без аудиофайлов: свисток, удар, штанга, гул трибун, рёв после гола.</summary>
    public static class Sfx
    {
        const int Rate = 44100;
        static AudioSource oneShot, crowd;
        static AudioClip whistle, whistleLong, kick, post, roar, crowdClip;
        static float lastPost;

        public static void Init(GameObject host)
        {
            oneShot = host.AddComponent<AudioSource>();
            oneShot.playOnAwake = false;
            crowd = host.AddComponent<AudioSource>();
            crowd.loop = true;
            crowd.volume = 0.12f;

            whistle = Whistle(0.35f);
            whistleLong = Whistle(0.9f);
            kick = Make("kick", 0.12f, (t, i) => Mathf.Sin(2 * Mathf.PI * (150f - 500f * t) * t) * Mathf.Exp(-t * 35f) + (Random.value * 2 - 1) * 0.3f * Mathf.Exp(-t * 70f));
            post = Make("post", 0.4f, (t, i) => (Mathf.Sin(2 * Mathf.PI * 880f * t) * 0.6f + Mathf.Sin(2 * Mathf.PI * 1320f * t) * 0.3f) * Mathf.Exp(-t * 9f));
            crowdClip = Noise("crowd", 4f, 0.5f, t => 0.8f + 0.2f * Mathf.Sin(t * 1.7f));
            roar = Noise("roar", 3.5f, 1f, t => Mathf.Clamp01(t * 3f) * Mathf.Clamp01((3.5f - t) / 1.5f));
            crowd.clip = crowdClip;
        }

        static AudioClip Whistle(float len)
        {
            return Make("whistle", len, (t, i) =>
            {
                float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((len - t) / 0.05f);
                float f = 2900f + 120f * Mathf.Sin(2 * Mathf.PI * 38f * t);
                return Mathf.Sin(2 * Mathf.PI * f * t) * 0.35f * env;
            });
        }

        static AudioClip Make(string name, float len, System.Func<float, int, float> fn)
        {
            int n = Mathf.CeilToInt(len * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = fn(i / (float)Rate, i);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Noise(string name, float len, float gain, System.Func<float, float> env)
        {
            int n = Mathf.CeilToInt(len * Rate);
            var data = new float[n];
            float b = 0f, lp = 0f;
            var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float w = (float)rng.NextDouble() * 2f - 1f;
                b = (b + 0.02f * w) / 1.02f;       // «коричневый» шум
                lp += (w - lp) * 0.08f;             // немного «шипения» толпы
                data[i] = (b * 6f + lp * 0.8f) * gain * env(i / (float)Rate);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static void Whistle(bool longer = false) { if (oneShot) oneShot.PlayOneShot(longer ? whistleLong : whistle, 0.6f); }
        public static void Kick(float strength) { if (oneShot) oneShot.PlayOneShot(kick, Mathf.Clamp(0.3f + strength, 0.3f, 1f)); }
        public static void Goal() { if (oneShot) oneShot.PlayOneShot(roar, 0.9f); }

        public static void Post()
        {
            if (!oneShot || Time.time - lastPost < 0.2f) return;
            lastPost = Time.time;
            oneShot.PlayOneShot(post, 0.7f);
        }

        public static void SetCrowd(bool on)
        {
            if (!crowd) return;
            if (on && !crowd.isPlaying) crowd.Play();
            else if (!on) crowd.Stop();
        }
    }
}
