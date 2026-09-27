using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FD
{
    [System.Serializable]
    public class Fixture
    {
        public int a, b;
        public int ga, gb, pa, pb;
        public bool played;
        public int group = -1;

        public bool Pens => played && ga == gb && (pa > 0 || pb > 0);
        public int Winner => ga > gb ? a : gb > ga ? b : (pa > pb ? a : b);
        public bool Has(int n) => a == n || b == n;
    }

    [System.Serializable]
    public class TeamRow
    {
        public int nation, p, w, d, l, gf, ga;
        public int Pts => w * 3 + d;
        public int GD => gf - ga;
    }

    [System.Serializable]
    public class Group
    {
        public string letter;
        public List<TeamRow> rows = new List<TeamRow>();
        public List<Fixture> fixtures = new List<Fixture>();   // тур k -> fixtures[2k], fixtures[2k+1]
    }

    [System.Serializable]
    public class KoRound
    {
        public string name;
        public List<Fixture> fixtures = new List<Fixture>();
    }

    [System.Serializable]
    public class WorldCupState
    {
        public int user;
        public List<Group> groups = new List<Group>();
        public List<KoRound> rounds = new List<KoRound>();
        public int stage;             // 0..2 — туры группы, 3+ — плей-офф (rounds[stage-3])
        public bool finished, userOut, prizeGiven;
        public int champion = -1;
    }

    /// <summary>
    /// ЧМ-2026: 48 команд, 12 групп по 4, в плей-офф выходят 2 лучших + 8 лучших третьих мест (1/16 финала).
    /// Жеребьёвка упрощённая: 4 корзины по рейтингу, хозяева (MEX, CAN, USA) — в A, B, D.
    /// </summary>
    public static class WorldCup
    {
        public static readonly string[] RoundNames = { "1/16 финала", "1/8 финала", "1/4 финала", "Полуфинал", "Финал" };
        static readonly int[,] MatchdayPairs = { { 0, 1 }, { 2, 3 }, { 0, 2 }, { 3, 1 }, { 3, 0 }, { 1, 2 } };

        public static WorldCupState New(int user)
        {
            var s = new WorldCupState { user = user };
            var all = Enumerable.Range(0, Database.Nations.Count).ToList();
            int mex = Database.FindNation("MEX"), can = Database.FindNation("CAN"), usa = Database.FindNation("USA");
            var hosts = new List<int> { mex, can, usa };
            var rest = all.Where(i => !hosts.Contains(i)).OrderByDescending(i => Database.Nations[i].rating).ToList();

            for (int g = 0; g < 12; g++) s.groups.Add(new Group { letter = ((char)('A' + g)).ToString() });

            // Корзина 1: хозяева в A, B, D + 9 сильнейших
            var pot1 = rest.Take(9).ToList();
            Shuffle(pot1);
            int k = 0;
            for (int g = 0; g < 12; g++)
            {
                int team = g == 0 ? mex : g == 1 ? can : g == 3 ? usa : pot1[k++];
                s.groups[g].rows.Add(new TeamRow { nation = team });
            }
            for (int pot = 0; pot < 3; pot++)
            {
                var teams = rest.Skip(9 + pot * 12).Take(12).ToList();
                Shuffle(teams);
                for (int g = 0; g < 12; g++) s.groups[g].rows.Add(new TeamRow { nation = teams[g] });
            }

            for (int g = 0; g < 12; g++)
            {
                var grp = s.groups[g];
                for (int m = 0; m < 6; m++)
                {
                    grp.fixtures.Add(new Fixture
                    {
                        a = grp.rows[MatchdayPairs[m, 0]].nation,
                        b = grp.rows[MatchdayPairs[m, 1]].nation,
                        group = g
                    });
                }
            }
            return s;
        }

        static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public static string StageName(WorldCupState s)
        {
            if (s.finished) return "Турнир завершён";
            if (s.stage < 3) return "Групповой этап, тур " + (s.stage + 1);
            return s.rounds[s.stage - 3].name;
        }

        public static List<Fixture> StageFixtures(WorldCupState s)
        {
            var list = new List<Fixture>();
            if (s.finished) return list;
            if (s.stage < 3)
            {
                foreach (var g in s.groups)
                {
                    list.Add(g.fixtures[s.stage * 2]);
                    list.Add(g.fixtures[s.stage * 2 + 1]);
                }
            }
            else if (s.stage - 3 < s.rounds.Count)
            {
                list.AddRange(s.rounds[s.stage - 3].fixtures);
            }
            return list;
        }

        public static Fixture UserFixture(WorldCupState s)
        {
            if (s.userOut || s.finished) return null;
            return StageFixtures(s).FirstOrDefault(f => !f.played && f.Has(s.user));
        }

        public static int GroupOf(WorldCupState s, int nation)
        {
            for (int g = 0; g < s.groups.Count; g++)
                if (s.groups[g].rows.Any(r => r.nation == nation)) return g;
            return -1;
        }

        /// <summary>Записать результат матча (голы в ориентации fixture.a / fixture.b).</summary>
        public static void RecordResult(WorldCupState s, Fixture f, int ga, int gb, int pa, int pb)
        {
            f.ga = ga; f.gb = gb; f.pa = pa; f.pb = pb; f.played = true;
            if (f.group >= 0) ApplyGroup(s.groups[f.group], f);
        }

        static void ApplyGroup(Group g, Fixture f)
        {
            var ra = g.rows.First(r => r.nation == f.a);
            var rb = g.rows.First(r => r.nation == f.b);
            ra.p++; rb.p++;
            ra.gf += f.ga; ra.ga += f.gb;
            rb.gf += f.gb; rb.ga += f.ga;
            if (f.ga > f.gb) { ra.w++; rb.l++; }
            else if (f.ga < f.gb) { rb.w++; ra.l++; }
            else { ra.d++; rb.d++; }
        }

        public static void Sim(Fixture f, bool knockout)
        {
            int ra = Database.Nations[f.a].rating, rb = Database.Nations[f.b].rating;
            float la = Mathf.Clamp(1.3f * Mathf.Pow(1.085f, ra - rb), 0.2f, 4f);
            float lb = Mathf.Clamp(1.3f * Mathf.Pow(1.085f, rb - ra), 0.2f, 4f);
            f.ga = U.Poisson(la);
            f.gb = U.Poisson(lb);
            f.pa = f.pb = 0;
            if (knockout && f.ga == f.gb) Penalties(ra, rb, out f.pa, out f.pb);
            f.played = true;
        }

        /// <summary>Симулировать матч и сразу учесть его в таблице группы.</summary>
        public static void SimAndRecord(WorldCupState s, Fixture f)
        {
            Sim(f, s.stage >= 3);
            if (f.group >= 0) ApplyGroup(s.groups[f.group], f);
        }

        /// <summary>Серия пенальти: 5 ударов + до первого промаха.</summary>
        public static void Penalties(float ra, float rb, out int pa, out int pb)
        {
            float probA = Mathf.Clamp(0.75f + (ra - rb) * 0.004f, 0.6f, 0.9f);
            float probB = Mathf.Clamp(0.75f + (rb - ra) * 0.004f, 0.6f, 0.9f);
            pa = pb = 0;
            for (int i = 0; i < 5; i++)
            {
                if (Random.value < probA) pa++;
                if (Random.value < probB) pb++;
            }
            int guard = 0;
            while (pa == pb && guard++ < 50)
            {
                if (Random.value < probA) pa++;
                if (Random.value < probB) pb++;
            }
            if (pa == pb) pa++;
        }

        public static List<TeamRow> Sorted(Group g)
        {
            return g.rows
                .OrderByDescending(r => r.Pts)
                .ThenByDescending(r => r.GD)
                .ThenByDescending(r => r.gf)
                .ThenByDescending(r => Database.Nations[r.nation].rating)
                .ToList();
        }

        static IEnumerable<TeamRow> Rank(IEnumerable<TeamRow> rows)
        {
            return rows.OrderByDescending(r => r.Pts).ThenByDescending(r => r.GD).ThenByDescending(r => r.gf)
                .ThenByDescending(r => Database.Nations[r.nation].rating);
        }

        /// <summary>Досимулировать текущий этап и перейти к следующему.</summary>
        public static void AdvanceStage(WorldCupState s)
        {
            if (s.finished) return;
            bool ko = s.stage >= 3;
            foreach (var f in StageFixtures(s))
            {
                if (!f.played) SimAndRecord(s, f);
            }

            if (!ko)
            {
                if (s.stage < 2) { s.stage++; return; }
                BuildRoundOf32(s);
                s.stage = 3;
                CheckUser(s);
                return;
            }

            var cur = s.rounds[s.stage - 3];
            if (cur.fixtures.Count == 1)
            {
                s.champion = cur.fixtures[0].Winner;
                s.finished = true;
                if (s.champion != s.user) s.userOut = true;
                return;
            }
            var winners = cur.fixtures.Select(f => f.Winner).ToList();
            var next = new KoRound { name = RoundNames[s.rounds.Count] };
            for (int i = 0; i < winners.Count; i += 2)
                next.fixtures.Add(new Fixture { a = winners[i], b = winners[i + 1] });
            s.rounds.Add(next);
            s.stage++;
            CheckUser(s);
        }

        static void CheckUser(WorldCupState s)
        {
            if (!StageFixtures(s).Any(f => f.Has(s.user))) s.userOut = true;
        }

        static void BuildRoundOf32(WorldCupState s)
        {
            var sorted = s.groups.Select(Sorted).ToList();
            var groupOf = new Dictionary<int, int>();
            for (int g = 0; g < 12; g++) foreach (var r in s.groups[g].rows) groupOf[r.nation] = g;

            var seeds = new List<int>();
            seeds.AddRange(Rank(sorted.Select(x => x[0])).Select(r => r.nation));
            seeds.AddRange(Rank(sorted.Select(x => x[1])).Select(r => r.nation));
            seeds.AddRange(Rank(sorted.Select(x => x[2])).Take(8).Select(r => r.nation));

            // классическая сетка: 1 и 2 сеянные встречаются только в финале
            var order = new List<int> { 1, 2 };
            while (order.Count < 32)
            {
                var n = new List<int>();
                int size = order.Count * 2;
                foreach (var x in order) { n.Add(x); n.Add(size + 1 - x); }
                order = n;
            }

            var round = new KoRound { name = RoundNames[0] };
            for (int i = 0; i < 32; i += 2)
                round.fixtures.Add(new Fixture { a = seeds[order[i] - 1], b = seeds[order[i + 1] - 1] });

            // разводим команды из одной группы
            var fx = round.fixtures;
            for (int i = 0; i < fx.Count; i++)
            {
                if (groupOf[fx[i].a] != groupOf[fx[i].b]) continue;
                for (int j = 0; j < fx.Count; j++)
                {
                    if (j == i) continue;
                    if (groupOf[fx[i].a] != groupOf[fx[j].b] && groupOf[fx[j].a] != groupOf[fx[i].b])
                    {
                        (fx[i].b, fx[j].b) = (fx[j].b, fx[i].b);
                        break;
                    }
                }
            }
            s.rounds.Add(round);
        }

        /// <summary>Призовые монеты для «Моей команды» за результат на ЧМ.</summary>
        public static int Prize(WorldCupState s)
        {
            if (s.champion == s.user) return 6000;
            int reached = s.rounds.Count(r => r.fixtures.Any(f => f.Has(s.user)));
            switch (reached)
            {
                case 0: return 300;
                case 1: return 600;
                case 2: return 1000;
                case 3: return 1600;
                case 4: return 2500;
                default: return 3500;
            }
        }
    }
}
