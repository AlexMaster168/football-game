using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FD
{
    public enum MatchState { Intro, Kickoff, Playing, Restart, Goal, HalfTime, FullTime, Done }

    /// <summary>Матч 11 на 11: правила, ввод, камера и HUD.</summary>
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager I;
        public const float HalfL = 50f, HalfW = 32f, GoalHalfW = 3.66f, GoalH = 2.44f;

        public MatchConfig cfg;
        public Ball ball;
        public TeamController home, away;
        public readonly List<FootballPlayer> all = new List<FootballPlayer>();
        public MatchState state;
        public float elapsed;
        public int homeGoals, awayGoals;
        public PadInput input;
        public float shotCharge;
        public FootballPlayer passReceiver;
        public bool paused;
        /// <summary>Автотест: без человека, ускоренно.</summary>
        public bool autoplay;

        readonly List<string> scorers = new List<string>();
        float stateTimer, bannerTimer, switchTimer, receiverTimer, homePossTime, awayPossTime;
        int homeShots, awayShots;
        string banner = "", bannerSub = "";
        bool secondHalf, showHelp = true;
        TeamController kickoffTeam;
        Camera cam;
        Vector3 camVel, lookPos;
        Action<MatchResult> onFinished;

        public static MatchManager Create(MatchConfig cfg, Action<MatchResult> done)
        {
            var go = new GameObject("Match");
            var m = go.AddComponent<MatchManager>();
            m.cfg = cfg;
            m.onFinished = done;
            m.Setup();
            return m;
        }

        // ---------------------------------------------------------------- подготовка

        void Setup()
        {
            I = this;
            cam = Camera.main;
            home = MakeTeam(cfg.home, 1, cfg.homeHuman);
            away = MakeTeam(cfg.away, -1, false);
            home.opp = away;
            away.opp = home;

            // конфликт цветов формы
            if (U.ColorDist(home.kit, away.kit) < 0.45f)
            {
                away.kit = cfg.away.kit2;
                if (U.ColorDist(home.kit, away.kit) < 0.45f) away.kit = U.ColorDist(home.kit, Color.white) > 0.6f ? Color.white : new Color(0.1f, 0.1f, 0.1f);
            }
            home.shorts = Shade(home.kit);
            away.shorts = Shade(away.kit);
            home.gkKit = PickGk(home.kit, away.kit, new Color(0.15f, 0.85f, 0.35f), new Color(1f, 0.55f, 0.1f));
            away.gkKit = PickGk(home.kit, away.kit, new Color(0.95f, 0.85f, 0.1f), new Color(0.95f, 0.35f, 0.75f));

            // сложность действует только на ИИ-соперника
            int diff = Settings.Difficulty;
            foreach (var t in new[] { home, away })
            {
                if (t.human) continue;
                if (cfg.homeHuman)
                {
                    t.speedMul = diff == 0 ? 0.92f : diff == 2 ? 1.06f : 1f;
                    t.reaction = diff == 0 ? 1.4f : diff == 2 ? 0.75f : 1f;
                    t.tackleBonus = diff == 0 ? -0.1f : diff == 2 ? 0.08f : 0f;
                    t.gkBonus = diff == 0 ? -0.08f : diff == 2 ? 0.06f : 0f;
                    t.doublePress = diff >= 1;
                }
                else t.doublePress = true;
            }

            SpawnTeam(home, cfg.home);
            SpawnTeam(away, cfg.away);
            ball = Ball.Create(transform);

            PlaceForKickoff(home);
            state = MatchState.Intro;
            stateTimer = introDur = autoplay ? 0.5f : 4f;
            ShowBanner(home.name + "  —  " + away.name, cfg.title, 3.5f);
            Stadium.SetCrowdColors(home.kit, away.kit);

            if (cam != null)
            {
                cam.transform.position = new Vector3(30f, 21f, 0f);
                lookPos = Vector3.zero;
                cam.transform.LookAt(lookPos);
                cam.fieldOfView = 40f;
            }
            Sfx.SetCrowd(true);
        }

        static Color Shade(Color kit)
        {
            float lum = kit.r * 0.3f + kit.g * 0.59f + kit.b * 0.11f;
            return lum > 0.6f ? new Color(0.12f, 0.14f, 0.3f) : Color.white;
        }

        static Color PickGk(Color a, Color b, Color first, Color second)
        {
            return (U.ColorDist(first, a) > 0.5f && U.ColorDist(first, b) > 0.5f) ? first : second;
        }

        TeamController MakeTeam(TeamSheet s, int sign, bool human)
        {
            return new TeamController { name = s.name, code = s.code, sign = sign, human = human, kit = s.kit, rating = s.Rating };
        }

        void SpawnTeam(TeamController t, TeamSheet s)
        {
            for (int i = 0; i < 11 && i < s.players.Count; i++)
            {
                var p = FootballPlayer.Spawn(transform, s.players[i], t, i);
                t.players.Add(p);
                all.Add(p);
            }
        }

        // ---------------------------------------------------------------- цикл

        void Update()
        {
            if (state == MatchState.Done) return;
            if (!autoplay && Input.GetKeyDown(KeyCode.Escape)) paused = !paused;
            if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;
            if (paused) return;

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            ReadInput();
            bannerTimer -= dt;

            switch (state)
            {
                case MatchState.Intro:
                    stateTimer -= dt;
                    if (stateTimer <= 0f) { Sfx.Whistle(); DoKickoff(home); }
                    break;
                case MatchState.Kickoff:
                case MatchState.Restart:
                    stateTimer -= dt;
                    foreach (var p in all) p.Animate(dt);
                    ball.Tick(dt);
                    if (stateTimer <= 0f) state = MatchState.Playing;
                    break;
                case MatchState.Playing:
                    elapsed += dt;
                    Sim(dt);
                    CheckTime();
                    break;
                case MatchState.Goal:
                    stateTimer -= dt;
                    ball.Tick(dt);
                    CelebrationTick(dt);
                    if (stateTimer <= 0f) DoKickoff(kickoffTeam);
                    break;
                case MatchState.HalfTime:
                    stateTimer -= dt;
                    if (stateTimer <= 0f) { secondHalf = true; Sfx.Whistle(); DoKickoff(away); }
                    break;
                case MatchState.FullTime:
                    stateTimer -= dt;
                    if (stateTimer <= 0f) Finish(false);
                    break;
            }
        }

        void ReadInput()
        {
            var inp = new PadInput();
            if (home.human && !autoplay)
            {
                float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
                // камера стоит сбоку (со стороны +x): вправо на экране = +z, вверх = -x
                Vector3 mv = new Vector3(-v, 0f, h);
                if (mv.sqrMagnitude > 1f) mv.Normalize();
                inp.move = mv;
                inp.sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.JoystickButton5);
                inp.passDown = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.JoystickButton0);
                inp.lobDown = Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.JoystickButton2);
                inp.shootHeld = Input.GetKey(KeyCode.F) || Input.GetKey(KeyCode.K) || Input.GetKey(KeyCode.JoystickButton1);
                inp.shootDown = Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.JoystickButton1);
                inp.switchDown = Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.JoystickButton4);
            }
            input = inp;
        }

        void Sim(float dt)
        {
            if (home.human) UpdateHumanSwitch(home, dt);
            AssignChasers(home);
            AssignChasers(away);
            if (passReceiver != null && (receiverTimer -= dt) <= 0f) passReceiver = null;

            foreach (var p in all) p.Think(dt);
            foreach (var p in all) p.Move(dt);
            Separate();
            ball.Tick(dt);
            if (CheckGoalOrOut()) return;
            CheckPossession();

            if (ball.owner != null)
            {
                if (ball.owner.team == home) homePossTime += dt; else awayPossTime += dt;
            }
        }

        void CheckTime()
        {
            if (state != MatchState.Playing) return;
            float half = cfg.durationSeconds * 0.5f;
            if (!secondHalf && elapsed >= half)
            {
                elapsed = half;
                state = MatchState.HalfTime;
                stateTimer = autoplay ? 0.5f : 2.5f;
                ShowBanner("Перерыв", home.code + " " + homeGoals + " : " + awayGoals + " " + away.code, 2.5f);
                Sfx.Whistle(true);
            }
            else if (elapsed >= cfg.durationSeconds)
            {
                elapsed = cfg.durationSeconds;
                state = MatchState.FullTime;
                stateTimer = autoplay ? 0.5f : 3f;
                ShowBanner("Финальный свисток", home.code + " " + homeGoals + " : " + awayGoals + " " + away.code, 3f);
                Sfx.Whistle(true);
            }
        }

        // ---------------------------------------------------------------- управление и ИИ команды

        void UpdateHumanSwitch(TeamController t, float dt)
        {
            if (ball.owner != null && ball.owner.team == t)
            {
                if (!ball.owner.isGK) SetControlled(t, ball.owner);
                return;
            }
            switchTimer -= dt;
            if (input.switchDown)
            {
                SetControlled(t, NearestToBall(t, t.controlled));
                switchTimer = 0.8f;
                return;
            }
            if (passReceiver != null && passReceiver.team == t) return;
            if (switchTimer <= 0f)
            {
                var n = NearestToBall(t, null);
                float threshold = input.move.sqrMagnitude > 0.1f ? 12f : 5f;
                if (t.controlled == null) SetControlled(t, n);
                else if (n != null && n != t.controlled &&
                         U.FlatDist(t.controlled.Pos, ball.Pos) - U.FlatDist(n.Pos, ball.Pos) > threshold)
                    SetControlled(t, n);
                switchTimer = 0.35f;
            }
        }

        public void SetControlled(TeamController t, FootballPlayer p)
        {
            if (!t.human || p == null || p.isGK || t.controlled == p) return;
            if (t.controlled != null) t.controlled.SetMarker(false);
            t.controlled = p;
            p.SetMarker(true);
            shotCharge = 0f;
        }

        FootballPlayer NearestToBall(TeamController t, FootballPlayer exclude)
        {
            FootballPlayer best = null;
            float bd = float.MaxValue;
            foreach (var p in t.players)
            {
                if (p.isGK || p == exclude) continue;
                float d = U.FlatDist(p.Pos, ball.Pos);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        FootballPlayer NearestTo(TeamController t, Vector3 pos)
        {
            FootballPlayer best = null;
            float bd = float.MaxValue;
            foreach (var p in t.players)
            {
                if (p.isGK) continue;
                float d = U.FlatDist(p.Pos, pos);
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        void AssignChasers(TeamController t)
        {
            t.chaser = t.chaser2 = null;
            if (ball.owner != null && ball.owner.team == t) return;
            FootballPlayer n1 = null, n2 = null;
            float d1 = float.MaxValue, d2 = float.MaxValue;
            Vector3 bp = ball.owner != null ? ball.owner.Pos : ball.Pos;
            foreach (var p in t.players)
            {
                if (p.isGK || p == t.controlled && t.human) continue;
                float d = U.FlatDist(p.Pos, bp);
                if (d < d1) { d2 = d1; n2 = n1; d1 = d; n1 = p; }
                else if (d < d2) { d2 = d; n2 = p; }
            }
            t.chaser = n1;
            bool oppHas = ball.owner != null && ball.owner.team != t;
            if (!t.human && oppHas && (t.doublePress || t.Progress(bp) < 0.4f)) t.chaser2 = n2;
        }

        public Vector3 PredictBall(FootballPlayer p)
        {
            Vector3 bp = U.Flat(ball.Pos);
            Vector3 bv = U.Flat(ball.vel);
            float d = Vector3.Distance(p.Pos, bp);
            float t = Mathf.Clamp(d / (p.MaxSpeed * 1.2f), 0f, 1.2f);
            return ClampInside(bp + bv * t * 0.7f, 0.5f);
        }

        public Vector3 ClampInside(Vector3 p, float margin)
        {
            p.x = Mathf.Clamp(p.x, -HalfW + margin, HalfW - margin);
            p.z = Mathf.Clamp(p.z, -HalfL + margin, HalfL - margin);
            p.y = 0f;
            return p;
        }

        void Separate()
        {
            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    Vector3 d = all[j].Pos - all[i].Pos;
                    d.y = 0f;
                    float m = d.sqrMagnitude;
                    if (m < 0.81f && m > 0.0001f)
                    {
                        float dist = Mathf.Sqrt(m);
                        Vector3 push = d / dist * (0.9f - dist) * 0.5f;
                        all[i].Pos -= push;
                        all[j].Pos += push;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- мяч

        public void Kick(FootballPlayer p, Vector3 vel)
        {
            ball.owner = null;
            ball.vel = vel;
            ball.lastTouch = p.team;
            ball.lastKicker = p;
            ball.isShot = false;
            ball.gkTried = false;
            passReceiver = null;
            p.OnKicked();
            Sfx.Kick(vel.magnitude / 30f);
        }

        public void SetReceiver(FootballPlayer t)
        {
            passReceiver = t;
            receiverTimer = 3f;
            if (t.team.human) SetControlled(t.team, t);
        }

        public void RegisterShot(TeamController t)
        {
            if (t == home) homeShots++; else awayShots++;
            Stadium.Excite(0.35f);
        }

        public void GiveBall(FootballPlayer p)
        {
            var prev = ball.owner;
            ball.owner = p;
            ball.vel = Vector3.zero;
            ball.isShot = false;
            ball.lastTouch = p.team;
            p.holdTime = 0f;
            p.decisionTimer = Random.Range(0.15f, 0.35f) * p.team.reaction;
            passReceiver = null;
            if (p.team.human && !p.isGK) SetControlled(p.team, p);
            if (prev == null || prev.team != p.team)
            {
                var o = p.team.opp;
                if (o.human) SetControlled(o, NearestToBall(o, null));
            }
        }

        void CheckPossession()
        {
            if (ball.owner != null) return;
            if (ball.isShot && ball.vel.magnitude < 8f) ball.isShot = false;
            if (GKTryHandle(home.GK) || GKTryHandle(away.GK)) return;

            Vector3 bp = ball.Pos;
            if (bp.y > 1.3f) return;
            FootballPlayer best = null;
            float bd = 0.85f;
            foreach (var p in all)
            {
                if (p.kickCooldown > 0f || p.stun > 0f) continue;
                float d = U.FlatDist(p.Pos, bp);
                if (d < bd) { bd = d; best = p; }
            }
            if (best != null) GiveBall(best);
        }

        bool GKTryHandle(FootballPlayer g)
        {
            if (g.kickCooldown > 0f) return false;
            Vector3 bp = ball.Pos;
            if (!g.team.InOwnBox(bp)) return false;
            float hd = U.FlatDist(g.Pos, bp);

            if (ball.isShot && ball.shotTeam != g.team)
            {
                float reach = 1.0f + g.data.keeping * 0.016f;
                if (ball.gkTried || hd > reach || bp.y > 2.8f) return false;
                ball.gkTried = true;
                float spd = ball.vel.magnitude;
                float chance = 0.3f + g.data.keeping * 0.0075f - Mathf.Max(0f, spd - 17f) * 0.018f
                               - Mathf.Max(0f, hd - 0.8f) * 0.14f + g.team.gkBonus;
                chance = Mathf.Clamp(chance, 0.08f, 0.93f);
                if (hd > 0.9f) g.Dive(bp);
                if (Random.value < chance)
                {
                    if (spd < 20f && hd < 1.5f) { GiveBall(g); ShowBanner("Сейв!", g.data.name, 1.2f); }
                    else { Parry(g); ShowBanner("Отбил!", g.data.name, 1.2f); }
                    return true;
                }
                return false;
            }
            if (hd < 1.3f && bp.y < 2.5f)
            {
                GiveBall(g);
                return true;
            }
            return false;
        }

        void Parry(FootballPlayer g)
        {
            Vector3 v = ball.vel;
            float side = Mathf.Sign(ball.Pos.x - g.Pos.x);
            if (side == 0f) side = 1f;
            ball.vel = new Vector3(side * Random.Range(3f, 8f), Random.Range(1.5f, 4.5f), -v.z * Random.Range(0.15f, 0.35f));
            ball.isShot = false;
            ball.lastTouch = g.team;
            ball.lastKicker = g;
            g.kickCooldown = 0.5f;
            Sfx.Kick(0.5f);
        }

        // ---------------------------------------------------------------- правила

        bool CheckGoalOrOut()
        {
            Vector3 p = ball.Pos;
            if (Mathf.Abs(p.z) > HalfL + Ball.R)
            {
                int end = p.z > 0f ? 1 : -1;
                if (Mathf.Abs(p.x) < GoalHalfW && p.y < GoalH)
                {
                    OnGoal(home.sign == end ? home : away);
                    return true;
                }
                TeamController defending = home.sign == end ? away : home;
                TeamController lt = ball.lastTouch ?? defending.opp;
                float sx = p.x >= 0f ? 1f : -1f;
                if (lt == defending)
                    Restart(defending.opp, new Vector3(sx * (HalfW - 0.6f), 0f, end * (HalfL - 0.6f)), "Угловой", false);
                else
                    Restart(defending, new Vector3(sx * 5f, 0f, end * (HalfL - 5.5f)), "Удар от ворот", true);
                return true;
            }
            if (Mathf.Abs(p.x) > HalfW + Ball.R)
            {
                TeamController t = ball.lastTouch == home ? away : home;
                var spot = new Vector3(Mathf.Sign(p.x) * (HalfW - 0.4f), 0f, Mathf.Clamp(p.z, -HalfL + 1f, HalfL - 1f));
                Restart(t, spot, "Аут", false);
                return true;
            }
            return false;
        }

        void OnGoal(TeamController scorer)
        {
            if (scorer == home) homeGoals++; else awayGoals++;
            var k = ball.lastKicker;
            string name = k != null ? k.data.name : "?";
            bool own = k != null && k.team != scorer;
            int minute = CurrentMinute();
            scorers.Add(minute + "'  " + name + (own ? " (автогол)" : "") + "  [" + scorer.code + "]");
            ShowBanner("ГОООЛ!", name + (own ? " (автогол)" : "") + "  " + minute + "'", 3f);
            ball.owner = null;
            ball.inNet = true;
            ball.isShot = false;
            kickoffTeam = scorer.opp;
            state = MatchState.Goal;
            stateTimer = autoplay ? 1f : 4.5f;
            Sfx.Goal();

            // празднование
            int end = ball.Pos.z > 0f ? 1 : -1;
            hero = own || k == null || k.isGK ? NearestTo(scorer, ball.Pos) : k;
            celebrateSpot = new Vector3(W3(), 0f, end * (HalfL - 4f));
            foreach (var p in scorer.players) p.celebrate = Celebration.Jump;
            foreach (var p in scorer.opp.players) p.celebrate = Celebration.Despair;
            hero.celebrate = Random.value < 0.5f ? Celebration.Airplane : Celebration.FistPump;
            if (scorer.human) SetControlled(scorer, hero);
            Stadium.ShakeNet(end);
            Stadium.Confetti(end, scorer.kit, Color.white);
            shake = 0.6f;
        }

        /// <summary>Для автотеста: засчитать гол хозяев, чтобы проверить празднование.</summary>
        public void TestGoal()
        {
            ball.owner = null;
            ball.lastKicker = home.players[9];
            ball.Pos = new Vector3(0.5f, 1f, home.sign * (HalfL + 0.6f));
            ball.vel = Vector3.zero;
            OnGoal(home);
        }

        // угол ближе к камере — празднование видно лучше
        static float W3() => HalfW - 5f;

        FootballPlayer hero;
        Vector3 celebrateSpot;
        float shake, introDur = 1f;

        void CelebrationTick(float dt)
        {
            foreach (var p in all)
            {
                Vector3 target;
                float speed;
                if (p == hero) { target = celebrateSpot; speed = p.MaxSpeed * 1.1f; }
                else if (p.celebrate == Celebration.Jump && !p.isGK && hero != null) { target = hero.Pos + U.Flat(p.Pos - hero.Pos).normalized * 1.6f; speed = p.MaxSpeed; }
                else { target = p.Pos; speed = 0f; }
                Vector3 to = U.Flat(target - p.Pos);
                float d = to.magnitude;
                p.desired = d > 0.4f ? to / d * Mathf.Min(speed, d * 2f) : Vector3.zero;
                p.sprint = p == hero;
                p.Move(dt);
            }
            Separate();
        }

        void Restart(TeamController t, Vector3 spot, string label, bool gk)
        {
            state = MatchState.Restart;
            stateTimer = autoplay ? 0.3f : 0.9f;
            ShowBanner(label, "", 1.2f);
            var taker = gk ? t.GK : NearestTo(t, spot);
            taker.Teleport(spot);
            Vector3 face = U.Flat(-spot);
            if (face.sqrMagnitude > 0.01f) taker.transform.rotation = Quaternion.LookRotation(face);
            ball.inNet = false;
            ball.Pos = spot + taker.transform.forward * 0.62f + Vector3.up * Ball.R;
            GiveBall(taker);
            taker.protectedTimer = 2.5f;
            taker.setPiece = true;
            taker.throwIn = label == "Аут";
            foreach (var p in t.opp.players)
            {
                Vector3 d = U.Flat(p.Pos - spot);
                if (d.magnitude < 7f)
                {
                    if (d.sqrMagnitude < 0.01f) d = -spot;
                    p.Pos = ClampInside(spot + d.normalized * 7f, 1f);
                }
            }
            if (t.human && !taker.isGK) SetControlled(t, taker);
        }

        void PlaceForKickoff(TeamController kicking)
        {
            foreach (var t in new[] { home, away })
            {
                foreach (var p in t.players)
                {
                    float z = Mathf.Min(Formation.Slots[p.slot].y * 0.9f, t == kicking ? 0.46f : 0.4f);
                    p.Teleport(t.SlotWorld(p.slot, z, 0f));
                    p.setPiece = false;
                    p.protectedTimer = 0f;
                }
            }
            var st = kicking.players[9];
            st.Teleport(new Vector3(0f, 0f, -kicking.sign * 0.7f));
            kicking.players[6].Teleport(new Vector3(-kicking.sign * 1.5f, 0f, -kicking.sign * 6f));
            ball.inNet = false;
            ball.owner = null;
            ball.vel = Vector3.zero;
            ball.Pos = new Vector3(0f, Ball.R, 0f);
        }

        void DoKickoff(TeamController kicking)
        {
            PlaceForKickoff(kicking);
            var st = kicking.players[9];
            GiveBall(st);
            st.protectedTimer = 2f;
            st.setPiece = true;
            if (home.human)
            {
                if (kicking == home) SetControlled(home, st);
                else SetControlled(home, NearestToBall(home, null));
            }
            state = MatchState.Kickoff;
            stateTimer = autoplay ? 0.3f : 1f;
        }

        int CurrentMinute()
        {
            int m = Mathf.FloorToInt(elapsed / cfg.durationSeconds * 90f) + 1;
            return Mathf.Clamp(m, 1, secondHalf ? 90 : 45);
        }

        public void Finish(bool forfeit)
        {
            if (state == MatchState.Done) return;
            var r = new MatchResult
            {
                homeName = home.name,
                awayName = away.name,
                homeGoals = homeGoals,
                awayGoals = awayGoals,
                homeShots = homeShots,
                awayShots = awayShots,
                forfeit = forfeit,
            };
            float tot = homePossTime + awayPossTime;
            r.homePossession = tot > 0f ? homePossTime / tot : 0.5f;
            r.scorers.AddRange(scorers);
            if (forfeit && home.human)
            {
                r.homeGoals = 0;
                r.awayGoals = Mathf.Max(3, awayGoals);
            }
            if (cfg.knockout && r.homeGoals == r.awayGoals)
            {
                WorldCup.Penalties(home.rating, away.rating, out r.homePens, out r.awayPens);
                r.pens = true;
            }
            state = MatchState.Done;
            Sfx.SetCrowd(false);
            if (I == this) I = null;
            Destroy(gameObject);
            onFinished?.Invoke(r);
        }

        void ShowBanner(string title, string sub, float time)
        {
            banner = title;
            bannerSub = sub;
            bannerTimer = time;
        }

        // ---------------------------------------------------------------- камера

        void LateUpdate()
        {
            if (cam == null || ball == null || state == MatchState.Done) return;
            float dt = Time.deltaTime;
            Vector3 b = ball.Pos;
            Vector3 target, look;
            float fov = 40f;

            if (state == MatchState.Intro && !autoplay)
            {
                // облёт стадиона перед стартовым свистком
                float k = Mathf.SmoothStep(0f, 1f, 1f - stateTimer / introDur);
                float ang = Mathf.Lerp(-150f, 0f, k) * Mathf.Deg2Rad;
                Vector3 orbit = new Vector3(Mathf.Cos(ang) * 70f, Mathf.Lerp(45f, 21f, k), Mathf.Sin(ang) * 70f);
                cam.transform.position = Vector3.Lerp(orbit, new Vector3(30f, 21f, 0f), k * k);
                cam.transform.LookAt(Vector3.zero);
                cam.fieldOfView = Mathf.Lerp(50f, 40f, k);
                lookPos = Vector3.zero;
                return;
            }
            if (state == MatchState.Goal && hero != null && !autoplay)
            {
                // крупный план автора гола
                Vector3 h = hero.Pos;
                target = new Vector3(h.x + 13f, 7.5f, h.z - Mathf.Sign(h.z) * 5f);
                look = h + Vector3.up * 1.2f;
                fov = 34f;
            }
            else
            {
                target = new Vector3(b.x * 0.3f + 30f, 21f, Mathf.Clamp(b.z, -37f, 37f));
                look = new Vector3(b.x * 0.6f, 0f, Mathf.Clamp(b.z, -42f, 42f));
                // сильный удар — камера чуть «подтягивается»
                if (ball.isShot) fov = 37f;
            }
            cam.transform.position = Vector3.SmoothDamp(cam.transform.position, target, ref camVel, 0.3f);
            lookPos = Vector3.Lerp(lookPos, look, 1f - Mathf.Exp(-6f * dt));
            cam.transform.LookAt(lookPos);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-3f * dt));
            if (shake > 0f)
            {
                shake -= dt;
                cam.transform.position += Random.insideUnitSphere * shake * 0.5f;
            }
        }

        // ---------------------------------------------------------------- HUD

        void OnGUI()
        {
            if (state == MatchState.Done) return;
            UI.Begin();
            float vw = UI.VW;

            // табло
            var sb = new Rect(16, 14, 470, 50);
            UI.Box(sb, new Color(0.03f, 0.05f, 0.12f, 0.88f));
            UI.Box(new Rect(sb.x + 10, sb.y + 12, 12, 26), home.kit);
            GUI.Label(new Rect(sb.x + 28, sb.y, 150, 50), home.code, UI.scoreTeam);
            GUI.Label(new Rect(sb.x + 150, sb.y, 120, 50), homeGoals + " : " + awayGoals, UI.scoreBig);
            GUI.Label(new Rect(sb.x + 250, sb.y, 110, 50), away.code, UI.scoreTeam);
            UI.Box(new Rect(sb.x + 350, sb.y + 12, 12, 26), away.kit);
            GUI.Label(new Rect(sb.x + 372, sb.y, 90, 50), CurrentMinute() + "'", UI.scoreTime);

            // игрок под управлением
            var c = home.controlled;
            if (home.human && c != null && cam != null && state != MatchState.Goal)
            {
                Vector3 sp = cam.WorldToScreenPoint(c.Pos + Vector3.up * 2.35f);
                if (sp.z > 0f)
                {
                    float gx = sp.x / UI.Scale, gy = (Screen.height - sp.y) / UI.Scale;
                    GUI.Label(new Rect(gx - 110, gy - 42, 220, 24), c.data.name, UI.nameTag);
                    GUI.Label(new Rect(gx - 20, gy - 22, 40, 22), "▼", UI.arrow);
                    if (shotCharge > 0f)
                    {
                        UI.Box(new Rect(gx - 42, gy - 52, 84, 9), new Color(0, 0, 0, 0.7f));
                        UI.Box(new Rect(gx - 41, gy - 51, 82 * shotCharge, 7), Color.Lerp(Color.green, Color.red, shotCharge));
                    }
                }
            }

            // баннер
            if (bannerTimer > 0f && !string.IsNullOrEmpty(banner))
            {
                float a = Mathf.Clamp01(bannerTimer * 2f);
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, a);
                UI.Box(new Rect(0, 120, vw, 110), new Color(0.02f, 0.04f, 0.1f, 0.6f * a));
                GUI.Label(new Rect(0, 122, vw, 70), banner, UI.banner);
                GUI.Label(new Rect(0, 186, vw, 36), bannerSub, UI.h2c);
                GUI.color = old;
            }

            DrawRadar(new Rect(vw / 2 - 125, UI.VH - 170, 250, 160));

            if (showHelp && home.human)
            {
                GUI.Label(new Rect(16, UI.VH - 118, 520, 110),
                    "WASD/стрелки — бег   Shift — спринт\nПробел/J — пас, в защите — отбор\nE/L — навес/пас на ход   F/K (держать) — удар\nQ/Tab — смена игрока   Esc — пауза   H — скрыть",
                    UI.hint);
            }

            if (paused) DrawPause(vw);
        }

        void DrawRadar(Rect r)
        {
            UI.Box(r, new Color(0.05f, 0.3f, 0.1f, 0.75f));
            UI.Box(new Rect(r.x + r.width / 2 - 1, r.y, 2, r.height), new Color(1, 1, 1, 0.5f));
            UI.Box(new Rect(r.x, r.y + r.height / 2 - 18, 6, 36), new Color(1, 1, 1, 0.6f));
            UI.Box(new Rect(r.xMax - 6, r.y + r.height / 2 - 18, 6, 36), new Color(1, 1, 1, 0.6f));
            foreach (var p in all)
            {
                Vector2 q = RadarPoint(r, p.Pos);
                Color col = p.isGK ? p.team.gkKit : p.team.kit;
                if (p == home.controlled && home.human) UI.Box(new Rect(q.x - 6, q.y - 6, 12, 12), Color.yellow);
                UI.Box(new Rect(q.x - 4, q.y - 4, 8, 8), Color.black);
                UI.Box(new Rect(q.x - 3, q.y - 3, 6, 6), col);
            }
            Vector2 b = RadarPoint(r, ball.Pos);
            UI.Box(new Rect(b.x - 3, b.y - 3, 6, 6), Color.white);
        }

        static Vector2 RadarPoint(Rect r, Vector3 p)
        {
            float x = r.x + (p.z + HalfL) / (2f * HalfL) * r.width;
            float y = r.y + (p.x + HalfW) / (2f * HalfW) * r.height;
            return new Vector2(Mathf.Clamp(x, r.x, r.xMax), Mathf.Clamp(y, r.y, r.yMax));
        }

        void DrawPause(float vw)
        {
            UI.Box(new Rect(0, 0, vw, UI.VH), new Color(0, 0, 0, 0.6f));
            var pr = new Rect(vw / 2 - 200, 200, 400, 300);
            UI.Box(pr, new Color(0.05f, 0.08f, 0.16f, 0.95f));
            GUI.Label(new Rect(pr.x, pr.y + 20, pr.width, 50), "Пауза", UI.h1c);
            if (GUI.Button(new Rect(pr.x + 50, pr.y + 100, 300, 60), "Продолжить", UI.bigButton)) paused = false;
            if (GUI.Button(new Rect(pr.x + 50, pr.y + 180, 300, 60), "Сдаться и выйти", UI.button))
            {
                paused = false;
                Finish(true);
            }
        }
    }
}
