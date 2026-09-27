using UnityEngine;
using Random = UnityEngine.Random;

namespace FD
{
    /// <summary>Футболист на поле: визуал, управление игроком и ИИ.</summary>
    public class FootballPlayer : MonoBehaviour
    {
        public PlayerData data;
        public TeamController team;
        public int slot;
        public bool isGK;

        public Vector3 velocity, desired;
        public bool sprint, setPiece, throwIn;
        public float kickCooldown, stun, tackleCd, protectedTimer, decisionTimer, holdTime;
        public Celebration celebrate;
        float diveTimer, slideTimer;

        PlayerRig rig;
        GameObject marker;

        static MatchManager M => MatchManager.I;
        static Ball B => MatchManager.I.ball;

        public Vector3 Pos { get => transform.position; set => transform.position = value; }
        public bool HasBall => B.owner == this;
        public bool IsHuman => team.human && team.controlled == this;
        public float MaxSpeed => (5.0f + data.pace * 0.032f) * team.speedMul * (isGK ? 1.05f : 1f);

        // ---------------------------------------------------------------- визуал

        public static FootballPlayer Spawn(Transform parent, PlayerData d, TeamController t, int slot)
        {
            var go = new GameObject(d.name);
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<FootballPlayer>();
            p.data = d;
            p.team = t;
            p.slot = slot;
            p.isGK = slot == 0;
            p.BuildModel();
            return p;
        }

        void BuildModel()
        {
            Color kit = isGK ? team.gkKit : team.kit;
            Color shorts = isGK ? new Color(0.12f, 0.12f, 0.12f) : team.shorts;
            Color socks = isGK ? new Color(0.15f, 0.15f, 0.15f) : team.kit;
            rig = PlayerRig.Build(transform, data, kit, shorts, socks, isGK);

            marker = U.Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.02f, 0f), new Vector3(1.4f, 0.01f, 1.4f), new Color(1f, 0.85f, 0.1f), "marker");
            marker.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.SetActive(false);
        }

        /// <summary>Сбили — падает на газон.</summary>
        public void Fall()
        {
            stun = 0.85f;
            kickCooldown = 0.9f;
            rig.Play(RigAction.Fall, 0.85f, Random.value < 0.5f ? -1f : 1f);
        }

        public void SetMarker(bool on) { if (marker != null) marker.SetActive(on); }

        public void Teleport(Vector3 p)
        {
            p.y = 0f;
            Pos = p;
            velocity = desired = Vector3.zero;
            stun = diveTimer = slideTimer = 0f;
            celebrate = Celebration.None;
            throwIn = false;
            transform.rotation = Quaternion.LookRotation(team.AttackDir);
        }

        public void Dive(Vector3 ballPos)
        {
            diveTimer = 0.9f;
            float side = Mathf.Sign(Vector3.Dot(ballPos - Pos, transform.right));
            if (side == 0f) side = 1f;
            velocity = transform.right * side * 5f;
            rig.Play(RigAction.Dive, 0.9f, side);
        }

        // ---------------------------------------------------------------- решения

        public void Think(float dt)
        {
            if (stun > 0f || slideTimer > 0f) { desired = Vector3.zero; return; }
            if (IsHuman) { HumanThink(dt); return; }
            sprint = false;
            if (isGK) { GKThink(); return; }
            if (HasBall) { AIWithBall(dt); return; }

            var owner = B.owner;
            bool teamHas = owner != null && owner.team == team;
            if (teamHas) { desired = Arrive(SupportPosition()); return; }

            if (M.passReceiver == this && owner == null)
            {
                desired = U.Flat(M.PredictBall(this) - Pos).normalized * MaxSpeed * 1.3f;
                sprint = true;
                return;
            }

            if (team.chaser == this || team.chaser2 == this)
            {
                Vector3 target = owner != null ? owner.Pos + owner.velocity * 0.3f : M.PredictBall(this);
                Vector3 to = U.Flat(target - Pos);
                float d = to.magnitude;
                sprint = d > 2.5f;
                desired = d < 0.1f ? Vector3.zero : to / d * MaxSpeed * (sprint ? 1.3f : 1f);
                if (owner != null && owner.team != team && tackleCd <= 0f)
                {
                    float od = U.FlatDist(owner.Pos, Pos);
                    if (od < 1.5f)
                    {
                        TryTackle(owner, team.tackleBonus, false);
                        tackleCd = Random.Range(0.9f, 1.6f) * team.reaction;
                    }
                    else if (od < 2.6f && Random.value < 0.012f)
                    {
                        Slide();
                        tackleCd = 1.8f * team.reaction;
                    }
                }
                return;
            }

            desired = Arrive(DefensivePosition());
        }

        void HumanThink(float dt)
        {
            var inp = M.input;
            sprint = inp.sprint;
            desired = inp.move * MaxSpeed * (sprint ? 1.3f : 1f);

            if (HasBall)
            {
                if (inp.shootHeld)
                {
                    M.shotCharge = Mathf.Min(1f, M.shotCharge + dt * 1.25f);
                }
                else if (M.shotCharge > 0f)
                {
                    float power = Mathf.Max(0.2f, M.shotCharge);
                    M.shotCharge = 0f;
                    Shoot(power, Mathf.Abs(inp.move.x) > 0.2f ? Mathf.Clamp(inp.move.x * 1.2f, -1f, 1f) : Random.Range(-0.6f, 0.6f));
                    return;
                }
                if (inp.passDown) HumanPass(inp.move, false);
                else if (inp.lobDown) HumanPass(inp.move, true);
            }
            else
            {
                M.shotCharge = 0f;
                if (inp.passDown && tackleCd <= 0f)
                {
                    // отбор в ногах
                    tackleCd = 0.45f;
                    var o = B.owner;
                    if (o != null && o.team != team && U.FlatDist(o.Pos, Pos) < 2.0f) TryTackle(o, 0.06f, false);
                    else { rig.Play(RigAction.StandTackle, 0.35f); velocity += transform.forward * 3f; }
                }
                else if (inp.shootDown && tackleCd <= 0f)
                {
                    // подкат: дальше достаёт, но если промазал — лежишь
                    tackleCd = 1.1f;
                    if (inp.move.sqrMagnitude > 0.05f) transform.rotation = Quaternion.LookRotation(inp.move);
                    Slide();
                }
            }
        }

        void HumanPass(Vector3 move, bool lob)
        {
            Vector3 dir = move.sqrMagnitude > 0.05f ? move.normalized : transform.forward;
            var t = BestPassTarget(dir, out _);
            if (t != null)
            {
                PassTo(t, lob);
            }
            else
            {
                Vector3 v = U.Flat(dir) * (lob ? 14f : 17f);
                v.y = lob ? 7f : 0.3f;
                M.Kick(this, v);
            }
        }

        void AIWithBall(float dt)
        {
            if (setPiece)
            {
                desired = Vector3.zero;
                if (holdTime > 0.8f * team.reaction)
                {
                    bool corner = Mathf.Abs(Pos.z) > MatchManager.HalfL - 2f && Mathf.Abs(Pos.x) > MatchManager.HalfW - 2f;
                    var t = corner ? BestBoxTarget() : BestPassTarget(Vector3.zero, out _);
                    if (t != null) PassTo(t, corner || U.FlatDist(t.Pos, Pos) > 28f);
                    else M.Kick(this, team.AttackDir * 16f + Vector3.up * 5f);
                }
                return;
            }

            Vector3 goal = team.OppGoal;
            float dGoal = U.FlatDist(Pos, goal);
            decisionTimer -= dt;
            if (decisionTimer <= 0f)
            {
                decisionTimer = Random.Range(0.2f, 0.4f) * team.reaction;

                if (dGoal < 27f && Mathf.Abs(Pos.x) < 22f && Vector3.Dot(transform.forward, team.AttackDir) > -0.2f)
                {
                    float ch = Mathf.Lerp(0.05f, 0.85f, Mathf.InverseLerp(27f, 9f, dGoal));
                    if (Random.value < ch) { ShootAI(dGoal); return; }
                }

                NearestOpponent(out float pressure);

                if (Mathf.Abs(Pos.x) > 15f && team.Progress(Pos) > 0.84f && Random.value < 0.5f)
                {
                    var box = BestBoxTarget();
                    if (box != null) { PassTo(box, true); return; }
                }

                float pc = pressure < 2.2f ? 0.6f : pressure < 4.5f ? 0.2f : 0.06f;
                if (holdTime > 3f) pc += 0.35f;
                if (Random.value < pc)
                {
                    var t = BestPassTarget(Vector3.zero, out _);
                    if (t != null && (pressure < 2.2f || team.Progress(t.Pos) > team.Progress(Pos) - 0.05f))
                    {
                        PassTo(t, U.FlatDist(t.Pos, Pos) > 30f);
                        return;
                    }
                }
            }

            // дриблинг к воротам с обходом ближайшего соперника
            Vector3 dir = U.Flat(goal - Pos).normalized;
            var opp = NearestOpponent(out float od);
            if (opp != null && od < 5f)
            {
                Vector3 away = U.Flat(Pos - opp.Pos).normalized;
                if (Vector3.Dot(-away, dir) > -0.2f) dir += away * (5f - od) / 5f * 1.3f;
            }
            if (Mathf.Abs(Pos.x) > MatchManager.HalfW - 4f) dir.x -= Mathf.Sign(Pos.x) * 0.8f;
            desired = dir.normalized * MaxSpeed * 0.85f;
        }

        void GKThink()
        {
            Vector3 own = team.OwnGoal;
            float s = team.sign;

            if (HasBall)
            {
                desired = Vector3.zero;
                if (holdTime > 1.3f * team.reaction)
                {
                    var t = BestPassTarget(Vector3.zero, out _);
                    if (t != null) PassTo(t, U.FlatDist(t.Pos, Pos) > 25f);
                    else M.Kick(this, team.AttackDir * 20f + Vector3.up * 9f);
                }
                return;
            }
            if (diveTimer > 0f) { desired = Vector3.zero; return; }

            Vector3 bp = B.Pos;
            bool inBox = team.InOwnBox(bp);
            var owner = B.owner;

            if (owner == null && inBox && !B.isShot && U.FlatDist(bp, Pos) < 12f)
            {
                desired = U.Flat(M.PredictBall(this) - Pos).normalized * MaxSpeed * 1.2f;
                sprint = true;
                return;
            }
            if (owner != null && owner.team != team && inBox && U.FlatDist(bp, own) < 11f)
            {
                desired = U.Flat(bp - Pos).normalized * MaxSpeed;
                if (tackleCd <= 0f && U.FlatDist(owner.Pos, Pos) < 1.6f)
                {
                    TryTackle(owner, 0.1f + team.gkBonus, false);
                    tackleCd = 1.2f;
                }
                return;
            }

            float gx = Mathf.Clamp(bp.x * 0.3f, -MatchManager.GoalHalfW + 0.5f, MatchManager.GoalHalfW - 0.5f);
            float depth = Mathf.Lerp(0.8f, 3f, Mathf.InverseLerp(40f, 12f, U.FlatDist(bp, own)));
            if (owner == null && B.isShot && B.shotTeam != team && B.vel.z * s < -1f)
            {
                float tz = (own.z + s * 0.8f - bp.z) / B.vel.z;
                if (tz > 0f && tz < 2f)
                {
                    gx = Mathf.Clamp(bp.x + B.vel.x * tz, -MatchManager.GoalHalfW - 0.5f, MatchManager.GoalHalfW + 0.5f);
                    depth = 0.8f;
                    sprint = true;
                }
            }
            Vector3 target = new Vector3(gx, 0f, own.z + s * depth);
            Vector3 to = U.Flat(target - Pos);
            float m = to.magnitude;
            desired = m < 0.15f ? Vector3.zero : to / m * Mathf.Min(MaxSpeed * 1.3f, m * 5f);
        }

        // ---------------------------------------------------------------- позиционирование

        Vector3 Arrive(Vector3 target)
        {
            Vector3 to = U.Flat(target - Pos);
            float d = to.magnitude;
            if (d < 0.5f) return Vector3.zero;
            sprint = d > 12f;
            return to / d * Mathf.Min(MaxSpeed * (sprint ? 1.2f : 1f), d * 1.5f);
        }

        Vector3 SupportPosition()
        {
            float prog = team.Progress(B.Pos);
            var s = Formation.Slots[slot];
            float z = s.y * 0.75f + prog * 0.42f + 0.03f;
            if (Formation.SlotPos[slot] == FD.Pos.FWD) z = Mathf.Max(z, prog + 0.05f);
            z = Mathf.Clamp(z, 0.05f, 0.93f);
            Vector3 t = team.SlotWorld(slot, z, B.Pos.x * 0.25f);
            t.x += (Mathf.PerlinNoise(Time.time * 0.2f, slot * 3.1f) - 0.5f) * 6f;
            t.z += (Mathf.PerlinNoise(slot * 1.7f, Time.time * 0.2f) - 0.5f) * 5f * team.sign;
            return M.ClampInside(t, 1.5f);
        }

        Vector3 DefensivePosition()
        {
            float prog = team.Progress(B.Pos);
            var s = Formation.Slots[slot];
            float z = Mathf.Clamp(s.y * 0.7f + prog * 0.35f - 0.04f, 0.04f, 0.8f);
            return team.SlotWorld(slot, z, B.Pos.x * 0.35f);
        }

        // ---------------------------------------------------------------- действия

        public FootballPlayer BestPassTarget(Vector3 dir, out float bestScore)
        {
            FootballPlayer best = null;
            bestScore = -999f;
            bool hasDir = dir.sqrMagnitude > 0.01f;
            Vector3 nd = hasDir ? U.Flat(dir).normalized : Vector3.zero;
            foreach (var mate in team.players)
            {
                if (mate == this) continue;
                Vector3 to = U.Flat(mate.Pos - Pos);
                float d = to.magnitude;
                if (d < 4f || d > 42f) continue;
                Vector3 n = to / d;
                float open = LaneOpenness(mate.Pos);
                float score;
                if (hasDir)
                {
                    float al = Vector3.Dot(nd, n);
                    if (al < 0.35f) continue;
                    score = al * 4f + open * 1.5f - d * 0.02f;
                }
                else
                {
                    float fwd = Vector3.Dot(n, team.AttackDir);
                    float mark = Mathf.Min(mate.NearestOpponentDist(), 6f);
                    score = fwd * 1.2f + open * 2f + mark * 0.25f - d * 0.025f + team.Progress(mate.Pos);
                    if (mate.isGK) score -= 3f;
                }
                if (score > bestScore) { bestScore = score; best = mate; }
            }
            return best;
        }

        FootballPlayer BestBoxTarget()
        {
            FootballPlayer best = null;
            float bs = -999f;
            foreach (var mate in team.players)
            {
                if (mate == this || mate.isGK) continue;
                if (team.Progress(mate.Pos) < 0.78f || Mathf.Abs(mate.Pos.x) > 15f) continue;
                float sc = Mathf.Min(mate.NearestOpponentDist(), 5f) + Random.value;
                if (sc > bs) { bs = sc; best = mate; }
            }
            return best;
        }

        float LaneOpenness(Vector3 target)
        {
            float min = 3f;
            Vector3 a = U.Flat(Pos), b = U.Flat(target);
            Vector3 ab = b - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 0.01f);
            foreach (var o in team.opp.players)
            {
                Vector3 p = U.Flat(o.Pos);
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
                float dist = Vector3.Distance(p, a + ab * t);
                if (dist < min) min = dist;
            }
            return min / 3f;
        }

        public FootballPlayer NearestOpponent(out float dist)
        {
            FootballPlayer best = null;
            dist = 999f;
            foreach (var o in team.opp.players)
            {
                float d = U.FlatDist(o.Pos, Pos);
                if (d < dist) { dist = d; best = o; }
            }
            return best;
        }

        public float NearestOpponentDist()
        {
            NearestOpponent(out float d);
            return d;
        }

        public void PassTo(FootballPlayer t, bool lob)
        {
            Vector3 from = U.Flat(Pos);
            Vector3 tp = U.Flat(t.Pos);
            float d = Vector3.Distance(from, tp);
            float speed = Mathf.Clamp(8.5f + d * 0.5f, 10f, 24f);
            float travel = d / (speed * 0.75f);
            Vector3 target = tp + U.Flat(t.velocity) * Mathf.Min(travel, 1.2f) * 0.8f;
            if (lob) target += team.AttackDir * 2.5f;
            target = M.ClampInside(target, 1.5f);

            Vector3 to = target - from;
            d = Mathf.Max(to.magnitude, 0.5f);
            Vector3 dir = to / d;
            float err = (1f - data.passing / 100f) * 10f * U.Gauss() * (lob ? 1.3f : 1f);
            dir = Quaternion.Euler(0f, err, 0f) * dir;

            Vector3 v;
            if (lob)
            {
                float time = Mathf.Clamp(d / 16f, 0.7f, 1.8f);
                v = dir * (d / time);
                v.y = 0.5f * Ball.G * time;
            }
            else
            {
                v = dir * speed;
                v.y = 0.4f;
            }
            M.Kick(this, v);
            M.SetReceiver(t);
        }

        public void Shoot(float power, float aimSide)
        {
            Vector3 goal = team.OppGoal;
            Vector3 bp = B.Pos;
            float acc = data.shooting / 100f;
            float dist = U.FlatDist(bp, goal);
            float ax = Mathf.Clamp(aimSide, -1f, 1f) * (MatchManager.GoalHalfW - 0.55f);
            float ay = Mathf.Lerp(0.35f, 1.8f, power * 0.8f);
            float spread = (1.2f - acc) * (0.5f + power) * (0.6f + dist / 25f);
            ax += U.Gauss() * spread * 1.5f;
            ay += U.Gauss() * spread * 0.5f;
            if (power > 0.9f) ay += (power - 0.9f) * 22f;   // перебор с силой — мяч улетает выше
            ay = Mathf.Max(0.25f, ay);

            Vector3 target = new Vector3(ax, ay, goal.z);
            float speed = Mathf.Lerp(15f, 31f, power) * (0.85f + acc * 0.2f);
            Vector3 flatTo = U.Flat(target - bp);
            float dh = Mathf.Max(flatTo.magnitude, 1f);
            float t = dh / speed;
            Vector3 v = flatTo / dh * speed;
            v.y = (ay - bp.y + 0.5f * Ball.G * t * t) / t;
            M.Kick(this, v);
            B.MarkShot(team);
            M.RegisterShot(team);
        }

        void ShootAI(float dGoal)
        {
            var gk = team.opp.GK;
            float side = gk.Pos.x > 0.3f ? -1f : gk.Pos.x < -0.3f ? 1f : (Random.value < 0.5f ? -1f : 1f);
            float power = Mathf.Clamp(Mathf.Lerp(0.45f, 0.85f, dGoal / 25f) + Random.Range(-0.1f, 0.1f), 0.35f, 0.9f);
            Shoot(power, side * Random.Range(0.5f, 1f));
        }

        public void TryTackle(FootballPlayer victim, float bonus, bool sliding)
        {
            if (!sliding) rig.Play(RigAction.StandTackle, 0.35f);
            if (victim.protectedTimer > 0f) return;
            float p = Mathf.Clamp(0.32f + (data.defending - victim.data.dribbling) * 0.012f + bonus, 0.1f, 0.85f);
            if (Random.value < p)
            {
                M.GiveBall(this);
                if (sliding || Random.value < 0.35f) victim.Fall();
                else { victim.stun = 0.6f; victim.kickCooldown = 0.8f; }
                Sfx.Kick(0.3f);
            }
            else if (!sliding)
            {
                stun = 0.45f;
            }
        }

        /// <summary>Подкат вперёд; отбор срабатывает, если по пути задел владельца мяча.</summary>
        public void Slide()
        {
            slideTimer = 0.7f;
            velocity = transform.forward * Mathf.Max(7.5f, U.Flat(velocity).magnitude + 2f);
            rig.Play(RigAction.Slide, 0.8f);
            slideHit = false;
        }

        bool slideHit;

        void SlideContact()
        {
            if (slideHit || slideTimer < 0.15f) return;
            var o = B.owner;
            if (o == null || o.team == team) return;
            if (U.FlatDist(o.Pos, Pos + transform.forward * 0.6f) < 1.3f)
            {
                slideHit = true;
                TryTackle(o, 0.12f + team.tackleBonus, true);
            }
        }

        public void OnKicked()
        {
            kickCooldown = 0.3f;
            setPiece = false;
            protectedTimer = 0f;
            rig.Play(throwIn ? RigAction.Throw : RigAction.Kick, throwIn ? 0.4f : 0.32f);
            throwIn = false;
        }

        // ---------------------------------------------------------------- движение и анимация

        public void Move(float dt)
        {
            kickCooldown -= dt; stun -= dt; tackleCd -= dt; protectedTimer -= dt;
            diveTimer -= dt; slideTimer -= dt;
            if (HasBall) holdTime += dt;

            float max = MaxSpeed * (sprint ? 1.3f : 1f) * (HasBall ? 0.92f : 1f);
            if (stun > 0f) max *= 0.35f;
            Vector3 d = Vector3.ClampMagnitude(desired, max);
            if (slideTimer > 0f)
            {
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, 9f * dt);
                SlideContact();
            }
            else if (diveTimer > 0f) velocity = Vector3.MoveTowards(velocity, Vector3.zero, 6f * dt);
            else velocity = Vector3.MoveTowards(velocity, d, 22f * dt);

            Vector3 p = Pos + velocity * dt;
            p.x = Mathf.Clamp(p.x, -MatchManager.HalfW - 1.5f, MatchManager.HalfW + 1.5f);
            p.z = Mathf.Clamp(p.z, -MatchManager.HalfL - 1.5f, MatchManager.HalfL + 1.5f);
            p.y = 0f;
            Pos = p;

            Vector3 face;
            if (isGK && !HasBall && diveTimer <= 0f) face = U.Flat(B.Pos - Pos);
            else if (HasBall && desired.sqrMagnitude < 0.1f) face = transform.forward;
            else face = velocity;
            bool locked = diveTimer > 0f || slideTimer > 0f || (rig.Action == RigAction.Fall);
            if (!locked && face.sqrMagnitude > 0.2f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(U.Flat(face)), 1f - Mathf.Exp(-14f * dt));

            Animate(dt);
        }

        public void Animate(float dt)
        {
            var c = new RigContext
            {
                speed = U.Flat(velocity).magnitude,
                sprint = sprint,
                gkReady = isGK && !HasBall && B.owner?.team != team && team.Progress(B.Pos) < 0.4f,
                holdBall = isGK && HasBall,
                throwIn = HasBall && throwIn,
                lookAt = B.Pos,
                celebrate = celebrate,
            };
            if (slideTimer > 0f || diveTimer > 0f) c.speed = 0f;
            rig.Tick(dt, c, transform);
        }
    }
}
