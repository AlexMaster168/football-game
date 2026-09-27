using System.Collections.Generic;
using UnityEngine;

namespace FD
{
    public enum Pos { GK, DEF, MID, FWD }

    [System.Serializable]
    public class PlayerData
    {
        public int id;
        public string name;
        public int nation;          // индекс сборной, -1 = академия
        public Pos pos;
        public int number;
        public int pace, shooting, passing, dribbling, defending, keeping;
        public int overall;
        public string club = "";
        public bool real;

        public int Price => Database.PriceFor(overall);

        public PlayerData Clone() => (PlayerData)MemberwiseClone();
        public string NationName => nation >= 0 ? Database.Nations[nation].name : "Академия";
        public string NationCode => nation >= 0 ? Database.Nations[nation].code : "ACD";
        public string PosName => Database.PosName(pos);
    }

    public class Nation
    {
        public int index;
        public string name, code, confed, pool;
        public int rating;
        public Color kit, kit2;
        public readonly List<PlayerData> squad = new List<PlayerData>();
    }

    /// <summary>
    /// 48 сборных ЧМ-2026 и сгенерированные (вымышленные) игроки.
    /// Имена вымышленные, чтобы не тащить лицензии и устаревшие составы.
    /// Хочешь реальных — просто замени генерацию на свои списки.
    /// </summary>
    public static class Database
    {
        public static readonly List<Nation> Nations = new List<Nation>();
        public static readonly Dictionary<int, PlayerData> Players = new Dictionary<int, PlayerData>();
        public static readonly List<PlayerData> Academy = new List<PlayerData>();
        public static readonly List<PlayerData> Market = new List<PlayerData>();
        static bool built;

        // название, код, конфедерация, рейтинг, пул имён, форма, запасная форма
        // Последние 6 (Италия, Турция, Украина, Дания, ДР Конго, Ирак) — участники из стыков, правь под себя.
        static readonly string[,] Table =
        {
            { "Мексика", "MEX", "КОНКАКАФ", "78", "latin", "#006847", "#FFFFFF" },
            { "США", "USA", "КОНКАКАФ", "78", "english", "#F2F2F2", "#0A3161" },
            { "Канада", "CAN", "КОНКАКАФ", "75", "english", "#D80621", "#FFFFFF" },
            { "Панама", "PAN", "КОНКАКАФ", "71", "latin", "#DA121A", "#FFFFFF" },
            { "Гаити", "HAI", "КОНКАКАФ", "66", "french", "#00209F", "#D21034" },
            { "Кюрасао", "CUW", "КОНКАКАФ", "66", "latin", "#002B7F", "#F9E814" },
            { "Япония", "JPN", "АФК", "79", "japanese", "#1B2F8F", "#FFFFFF" },
            { "Иран", "IRN", "АФК", "75", "persian", "#F2F2F2", "#DA0000" },
            { "Узбекистан", "UZB", "АФК", "71", "persian", "#1EB5E0", "#FFFFFF" },
            { "Южная Корея", "KOR", "АФК", "76", "korean", "#E4002B", "#FFFFFF" },
            { "Иордания", "JOR", "АФК", "69", "arabic", "#F2F2F2", "#CE1126" },
            { "Австралия", "AUS", "АФК", "74", "english", "#FFCD00", "#00843D" },
            { "Катар", "QAT", "АФК", "70", "arabic", "#8A1538", "#FFFFFF" },
            { "Саудовская Аравия", "KSA", "АФК", "71", "arabic", "#1E7F3C", "#FFFFFF" },
            { "Ирак", "IRQ", "АФК", "69", "arabic", "#007A3D", "#FFFFFF" },
            { "Марокко", "MAR", "КАФ", "80", "arabic", "#C1272D", "#006233" },
            { "Тунис", "TUN", "КАФ", "72", "arabic", "#E70013", "#FFFFFF" },
            { "Египет", "EGY", "КАФ", "75", "arabic", "#CE1126", "#FFFFFF" },
            { "Алжир", "ALG", "КАФ", "75", "arabic", "#F2F2F2", "#006633" },
            { "Гана", "GHA", "КАФ", "73", "african", "#F2F2F2", "#FCD116" },
            { "Кабо-Верде", "CPV", "КАФ", "70", "african", "#003893", "#FFFFFF" },
            { "ЮАР", "RSA", "КАФ", "71", "african", "#FFB612", "#007749" },
            { "Сенегал", "SEN", "КАФ", "78", "african", "#F2F2F2", "#00853F" },
            { "Кот-д'Ивуар", "CIV", "КАФ", "76", "african", "#F77F00", "#FFFFFF" },
            { "ДР Конго", "COD", "КАФ", "73", "african", "#007FFF", "#CE1021" },
            { "Аргентина", "ARG", "КОНМЕБОЛ", "88", "latin", "#75AADB", "#1C2C5B" },
            { "Бразилия", "BRA", "КОНМЕБОЛ", "86", "latin", "#FEDF00", "#002776" },
            { "Эквадор", "ECU", "КОНМЕБОЛ", "77", "latin", "#FFD100", "#034EA2" },
            { "Уругвай", "URU", "КОНМЕБОЛ", "81", "latin", "#5CBFEB", "#FFFFFF" },
            { "Колумбия", "COL", "КОНМЕБОЛ", "80", "latin", "#FCD116", "#003893" },
            { "Парагвай", "PAR", "КОНМЕБОЛ", "75", "latin", "#D52B1E", "#FFFFFF" },
            { "Новая Зеландия", "NZL", "ОФК", "68", "english", "#F2F2F2", "#111111" },
            { "Англия", "ENG", "УЕФА", "87", "english", "#F2F2F2", "#CF081F" },
            { "Франция", "FRA", "УЕФА", "88", "french", "#002654", "#FFFFFF" },
            { "Хорватия", "CRO", "УЕФА", "81", "slavic", "#FF1A1A", "#1B3F8B" },
            { "Португалия", "POR", "УЕФА", "86", "latin", "#C8102E", "#006600" },
            { "Норвегия", "NOR", "УЕФА", "79", "nordic", "#BA0C2F", "#FFFFFF" },
            { "Германия", "GER", "УЕФА", "85", "nordic", "#F2F2F2", "#111111" },
            { "Нидерланды", "NED", "УЕФА", "84", "nordic", "#FF6200", "#FFFFFF" },
            { "Испания", "ESP", "УЕФА", "88", "latin", "#C60B1E", "#FFC400" },
            { "Бельгия", "BEL", "УЕФА", "82", "french", "#E30613", "#1B1B1B" },
            { "Австрия", "AUT", "УЕФА", "78", "nordic", "#ED2939", "#FFFFFF" },
            { "Швейцария", "SUI", "УЕФА", "78", "nordic", "#D52B1E", "#FFFFFF" },
            { "Шотландия", "SCO", "УЕФА", "75", "english", "#0B2A5B", "#FFFFFF" },
            { "Италия", "ITA", "УЕФА", "82", "italian", "#0066CC", "#FFFFFF" },
            { "Турция", "TUR", "УЕФА", "78", "turkish", "#E30A17", "#FFFFFF" },
            { "Украина", "UKR", "УЕФА", "76", "slavic", "#FFD500", "#005BBB" },
            { "Дания", "DEN", "УЕФА", "79", "nordic", "#C8102E", "#FFFFFF" },
        };

        static readonly Dictionary<string, string[][]> Names = new Dictionary<string, string[][]>
        {
            { "latin", new[] {
                new[] { "Carlos", "Diego", "Luis", "Mateo", "Javier", "Andrés", "Pablo", "Rafael", "Thiago", "Bruno", "Gabriel", "Lucas", "Rodrigo", "Santiago", "Emiliano", "Joaquín", "Nicolás", "Felipe", "Martín", "Hugo" },
                new[] { "García", "Fernández", "López", "Martínez", "Silva", "Santos", "Pereira", "Rodríguez", "Gómez", "Díaz", "Torres", "Ramírez", "Castro", "Morales", "Herrera", "Rojas", "Costa", "Almeida", "Vargas", "Medina" } } },
            { "english", new[] {
                new[] { "James", "Harry", "Jack", "Oliver", "Tyler", "Mason", "Ryan", "Liam", "Connor", "Ethan", "Kyle", "Owen", "Callum", "Jordan", "Luke", "Aaron", "Jamie", "Ben", "Noah", "Charlie" },
                new[] { "Smith", "Johnson", "Walker", "Brown", "Taylor", "Wilson", "Davies", "Evans", "Thomas", "Clarke", "Wright", "Hughes", "Turner", "Hall", "Green", "Baker", "Wood", "Miller", "Adams", "Reed" } } },
            { "french", new[] {
                new[] { "Antoine", "Hugo", "Lucas", "Théo", "Maxime", "Julien", "Adrien", "Raphaël", "Jules", "Mathis", "Enzo", "Nathan", "Louis", "Clément", "Yann", "Rayan", "Florian", "Benoît", "Jean", "Pierre" },
                new[] { "Martin", "Bernard", "Dubois", "Moreau", "Laurent", "Simon", "Michel", "Lefebvre", "Leroy", "Roux", "David", "Bertrand", "Morel", "Fournier", "Girard", "Bonnet", "Dupont", "Lambert", "Fontaine", "Rousseau" } } },
            { "nordic", new[] {
                new[] { "Lukas", "Jonas", "Leon", "Felix", "Niklas", "Timo", "Kai", "Florian", "Erik", "Lars", "Magnus", "Sander", "Joris", "Daan", "Stefan", "Marco", "Tobias", "Jannik", "Mats", "Oskar" },
                new[] { "Müller", "Schmidt", "Schneider", "Fischer", "Weber", "Becker", "Wagner", "Hoffmann", "de Jong", "van Dijk", "Bakker", "Visser", "Hansen", "Larsen", "Johansen", "Berg", "Nilsen", "Huber", "Keller", "Frei" } } },
            { "slavic", new[] {
                new[] { "Luka", "Ivan", "Marko", "Josip", "Ante", "Nikola", "Dario", "Andrej", "Petar", "Dominik", "Filip", "Mario", "Denis", "Oleksandr", "Artem", "Viktor", "Roman", "Taras", "Bohdan", "Mykola" },
                new[] { "Horvat", "Babić", "Jurić", "Novak", "Kovač", "Marić", "Petrović", "Knežević", "Vuković", "Tomić", "Pavlović", "Kovalenko", "Bondarenko", "Tkachenko", "Kravets", "Melnyk", "Lysenko", "Moroz", "Savchenko", "Boyko" } } },
            { "arabic", new[] {
                new[] { "Mohamed", "Ahmed", "Youssef", "Omar", "Ali", "Hamza", "Karim", "Amine", "Sofiane", "Khalid", "Salem", "Fahad", "Nasser", "Hassan", "Ismail", "Bilal", "Ayoub", "Tarek", "Walid", "Ziad" },
                new[] { "Haddad", "El Amrani", "Mansour", "Saleh", "Belkacem", "Rahmani", "Farouk", "Al-Harbi", "Al-Qahtani", "Al-Shehri", "Nasri", "Tahiri", "Benali", "Chahid", "Idrissi", "Khalil", "Hamdi", "Sabri", "Aziz", "Jaber" } } },
            { "african", new[] {
                new[] { "Kwame", "Kofi", "Ibrahima", "Moussa", "Cheikh", "Thabo", "Sipho", "Yaw", "Emmanuel", "Samuel", "Bongani", "Mamadou", "Ismaila", "Franck", "Serge", "Wilfried", "Joel", "Isaac", "Patrick", "Chancel" },
                new[] { "Mensah", "Boateng", "Diallo", "Ndiaye", "Sarr", "Traoré", "Koné", "Touré", "Mokoena", "Dlamini", "Asante", "Owusu", "Cissé", "Diop", "Gueye", "Bamba", "Kouassi", "Nkosi", "Tavares", "Mbemba" } } },
            { "japanese", new[] {
                new[] { "Takumi", "Daichi", "Ritsu", "Wataru", "Hiroki", "Kenta", "Yuto", "Shota", "Kaito", "Ren", "Sota", "Haruto", "Kazuki", "Yuki", "Riku", "Koji", "Daiki", "Keita", "Shun", "Ayumu" },
                new[] { "Tanaka", "Suzuki", "Sato", "Takahashi", "Watanabe", "Ito", "Yamamoto", "Nakamura", "Kobayashi", "Kato", "Yoshida", "Yamada", "Matsumoto", "Inoue", "Kimura", "Hayashi", "Shimizu", "Mori", "Ikeda", "Hashimoto" } } },
            { "korean", new[] {
                new[] { "Jae-won", "Seung-ho", "Dong-hyun", "Hyun-woo", "Ji-hoon", "Min-ho", "Sung-min", "Tae-yang", "Woo-jin", "Jun-young", "Do-yun", "Si-woo", "Ha-jun", "Joon-ho", "Young-min", "Chan-woo", "Sang-hoon", "Kyung-soo", "Jin-su", "Hyeon-jun" },
                new[] { "Kim", "Lee", "Park", "Choi", "Jung", "Kang", "Cho", "Yoon", "Jang", "Lim", "Han", "Oh", "Seo", "Shin", "Kwon", "Hwang", "Ahn", "Song", "Hong", "Yoo" } } },
            { "persian", new[] {
                new[] { "Ali", "Mehdi", "Reza", "Sardor", "Eldor", "Otabek", "Saman", "Alireza", "Mohammad", "Rustam", "Abbos", "Timur", "Farid", "Dilshod", "Sherzod", "Arash", "Behnam", "Omid", "Jasur", "Babak" },
                new[] { "Karimov", "Rahimov", "Yusupov", "Nazarov", "Rezaei", "Ahmadi", "Hosseini", "Mohammadi", "Jafari", "Ergashev", "Tursunov", "Azimov", "Nematov", "Sadeghi", "Moradi", "Kazemi", "Alimov", "Shukurov", "Ghorbani", "Hamidov" } } },
            { "italian", new[] {
                new[] { "Marco", "Federico", "Lorenzo", "Alessandro", "Giovanni", "Andrea", "Davide", "Matteo", "Nicolò", "Riccardo", "Simone", "Gianluca", "Stefano", "Filippo", "Tommaso", "Pietro", "Luca", "Giacomo", "Samuele", "Manuel" },
                new[] { "Rossi", "Bianchi", "Romano", "Colombo", "Ricci", "Marino", "Greco", "Bruno", "Gallo", "Conti", "De Luca", "Mancini", "Costa", "Giordano", "Rizzo", "Lombardi", "Moretti", "Barbieri", "Fontana", "Santoro" } } },
            { "turkish", new[] {
                new[] { "Emre", "Burak", "Kerem", "Arda", "Mert", "Cengiz", "Hakan", "Ozan", "Yusuf", "Kaan", "Barış", "Cem", "Oğuz", "Serkan", "Umut", "Volkan", "Efe", "Tolga", "Deniz", "Selim" },
                new[] { "Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Aydın", "Öztürk", "Arslan", "Doğan", "Koç", "Kurt", "Özdemir", "Aslan", "Polat", "Erdoğan", "Güneş", "Yıldız", "Aksoy", "Kılıç", "Tekin" } } },
        };

        public static void Build()
        {
            if (built) return;
            built = true;
            for (int i = 0; i < Table.GetLength(0); i++)
            {
                var n = new Nation
                {
                    index = i,
                    name = Table[i, 0],
                    code = Table[i, 1],
                    confed = Table[i, 2],
                    rating = int.Parse(Table[i, 3]),
                    pool = Table[i, 4],
                    kit = U.Hex(Table[i, 5]),
                    kit2 = U.Hex(Table[i, 6]),
                };
                Nations.Add(n);
                GenSquad(n);
            }

            var rng = new System.Random(2026);
            var used = new HashSet<string>();
            string[] pools = { "latin", "english", "french", "nordic", "slavic", "african", "arabic", "italian" };
            Pos[] layout = { Pos.GK, Pos.GK, Pos.DEF, Pos.DEF, Pos.DEF, Pos.DEF, Pos.DEF, Pos.MID, Pos.MID, Pos.MID, Pos.MID, Pos.FWD, Pos.FWD, Pos.FWD, Pos.FWD, Pos.MID };
            for (int i = 0; i < layout.Length; i++)
            {
                int ovr = 50 + rng.Next(0, 10);
                var p = MakePlayer(rng, 10000 + i, UniqueName(rng, pools[i % pools.Length], used), -1, layout[i], ovr, i + 1);
                Academy.Add(p);
                Players[p.id] = p;
            }
        }

        static void GenSquad(Nation n)
        {
            var rng = new System.Random(n.index * 7919 + 17);
            var used = new HashSet<string>();
            if (RealPlayers.Squads.TryGetValue(n.code, out var lines))
            {
                LoadReal(n, lines, rng, used);
                return;
            }
            Pos[] layout = { Pos.GK, Pos.GK, Pos.DEF, Pos.DEF, Pos.DEF, Pos.DEF, Pos.DEF, Pos.DEF, Pos.MID, Pos.MID, Pos.MID, Pos.MID, Pos.MID, Pos.MID, Pos.FWD, Pos.FWD, Pos.FWD, Pos.FWD };
            int[] numbers = { 1, 12, 2, 3, 4, 5, 15, 22, 6, 8, 10, 14, 16, 18, 7, 9, 11, 19 };
            for (int i = 0; i < layout.Length; i++)
            {
                int ovr = n.rating - 5 + rng.Next(-6, 5);
                if (i == 0 || i == 2 || i == 3 || i == 8 || i == 10 || i == 14 || i == 15) ovr += 4; // ядро команды
                ovr = Mathf.Clamp(ovr, 45, 93);
                var p = MakePlayer(rng, n.index * 100 + i, UniqueName(rng, n.pool, used), n.index, layout[i], ovr, numbers[i]);
                n.squad.Add(p);
                Players[p.id] = p;
                Market.Add(p);
            }
        }

        /// <summary>Реальные игроки + сгенерированные запасные до 18 человек (минимум 2 вратаря).</summary>
        static void LoadReal(Nation n, string[] lines, System.Random rng, HashSet<string> used)
        {
            int idx = 0;
            var numbersUsed = new HashSet<int>();
            foreach (var line in lines)
            {
                var f = line.Split('|');
                if (f.Length < 5) continue;
                var pos = ParsePos(f[1]);
                int ovr = int.Parse(f[2]);
                var p = new PlayerData
                {
                    id = n.index * 100 + idx, name = f[0], nation = n.index, pos = pos, overall = ovr,
                    club = f[4], real = true
                };
                if (pos == Pos.GK)
                {
                    p.keeping = Mathf.Min(99, ovr + 1);
                    p.pace = ovr - 30; p.shooting = ovr - 50; p.passing = ovr - 20; p.dribbling = ovr - 40; p.defending = ovr - 45;
                }
                else
                {
                    var s = f[3].Split(' ');
                    p.pace = int.Parse(s[0]); p.shooting = int.Parse(s[1]); p.passing = int.Parse(s[2]);
                    p.dribbling = int.Parse(s[3]); p.defending = int.Parse(s[4]); p.keeping = 10;
                }
                p.pace = Mathf.Clamp(p.pace, 20, 99); p.shooting = Mathf.Clamp(p.shooting, 15, 99);
                p.passing = Mathf.Clamp(p.passing, 20, 99); p.dribbling = Mathf.Clamp(p.dribbling, 15, 99);
                p.defending = Mathf.Clamp(p.defending, 15, 99);
                p.number = NextNumber(pos, numbersUsed);
                used.Add(p.name);
                n.squad.Add(p);
                Players[p.id] = p;
                Market.Add(p);
                idx++;
            }

            // добить заявку до 18 сгенерированными резервистами
            int gks = n.squad.FindAll(x => x.pos == Pos.GK).Count;
            int worst = 99;
            foreach (var p in n.squad) worst = Mathf.Min(worst, p.overall);
            Pos[] fill = { Pos.DEF, Pos.MID, Pos.FWD, Pos.DEF, Pos.MID, Pos.FWD };
            int k = 0;
            while (n.squad.Count < 18 || gks < 2)
            {
                Pos pos = gks < 2 ? Pos.GK : fill[k++ % fill.Length];
                if (pos == Pos.GK) gks++;
                int ovr = Mathf.Clamp(worst - 2 + rng.Next(-3, 2), 45, 90);
                var p = MakePlayer(rng, n.index * 100 + idx, UniqueName(rng, n.pool, used), n.index, pos, ovr, NextNumber(pos, numbersUsed));
                p.club = "Резерв";
                n.squad.Add(p);
                Players[p.id] = p;
                Market.Add(p);
                idx++;
            }

            // рейтинг сборной — по её лучшим 11
            n.rating = TeamRating(AutoLineup(n.squad));
        }

        static Pos ParsePos(string s)
        {
            switch (s)
            {
                case "GK": return Pos.GK;
                case "DEF": return Pos.DEF;
                case "MID": return Pos.MID;
                default: return Pos.FWD;
            }
        }

        static int NextNumber(Pos pos, HashSet<int> used)
        {
            int[] pref;
            switch (pos)
            {
                case Pos.GK: pref = new[] { 1, 12, 23, 13 }; break;
                case Pos.DEF: pref = new[] { 2, 4, 5, 3, 15, 22, 6, 24, 25, 26 }; break;
                case Pos.MID: pref = new[] { 8, 6, 10, 14, 16, 18, 20, 17, 21, 24 }; break;
                default: pref = new[] { 9, 7, 11, 19, 10, 21, 17, 20, 26, 27 }; break;
            }
            foreach (int x in pref) if (used.Add(x)) return x;
            for (int x = 2; x < 99; x++) if (used.Add(x)) return x;
            return 99;
        }

        static string UniqueName(System.Random rng, string pool, HashSet<string> used)
        {
            var arr = Names[pool];
            for (int tries = 0; tries < 50; tries++)
            {
                string s = arr[0][rng.Next(arr[0].Length)] + " " + arr[1][rng.Next(arr[1].Length)];
                if (used.Add(s)) return s;
            }
            return arr[0][0] + " " + arr[1][0] + " " + used.Count;
        }

        static PlayerData MakePlayer(System.Random rng, int id, string name, int nation, Pos pos, int ovr, int number)
        {
            int R(int v) => Mathf.Clamp(v + rng.Next(-6, 7), 20, 99);
            var p = new PlayerData { id = id, name = name, nation = nation, pos = pos, overall = ovr, number = number };
            switch (pos)
            {
                case Pos.GK:
                    p.keeping = R(ovr + 3); p.pace = R(ovr - 25); p.shooting = R(ovr - 40);
                    p.passing = R(ovr - 18); p.dribbling = R(ovr - 35); p.defending = R(ovr - 30);
                    break;
                case Pos.DEF:
                    p.defending = R(ovr + 4); p.pace = R(ovr - 3); p.passing = R(ovr - 6);
                    p.shooting = R(ovr - 22); p.dribbling = R(ovr - 10); p.keeping = 10;
                    break;
                case Pos.MID:
                    p.passing = R(ovr + 4); p.dribbling = R(ovr + 1); p.shooting = R(ovr - 5);
                    p.defending = R(ovr - 10); p.pace = R(ovr - 3); p.keeping = 10;
                    break;
                default:
                    p.shooting = R(ovr + 4); p.pace = R(ovr + 2); p.dribbling = R(ovr + 2);
                    p.passing = R(ovr - 6); p.defending = R(ovr - 35); p.keeping = 10;
                    break;
            }
            return p;
        }

        public static int PriceFor(int ovr)
        {
            return Mathf.Max(20, Mathf.RoundToInt(40f * Mathf.Pow(1.13f, ovr - 50) / 10f) * 10);
        }

        public static string PosName(Pos p)
        {
            switch (p)
            {
                case Pos.GK: return "ВРТ";
                case Pos.DEF: return "ЗАЩ";
                case Pos.MID: return "ПЗ";
                default: return "НАП";
            }
        }

        public static PlayerData Get(int id) => Players.TryGetValue(id, out var p) ? p : null;

        public static int FindNation(string code)
        {
            for (int i = 0; i < Nations.Count; i++) if (Nations[i].code == code) return i;
            return -1;
        }

        /// <summary>Лучшие 11 под схему 4-3-3 (порядок слотов как в Formation).</summary>
        public static List<PlayerData> AutoLineup(IEnumerable<PlayerData> players)
        {
            var pool = new List<PlayerData>(players);
            pool.Sort((a, b) => b.overall.CompareTo(a.overall));
            var res = new PlayerData[11];
            var used = new HashSet<int>();
            for (int s = 0; s < 11; s++)
            {
                foreach (var p in pool)
                {
                    if (p.pos == Formation.SlotPos[s] && !used.Contains(p.id)) { res[s] = p; used.Add(p.id); break; }
                }
            }
            for (int s = 0; s < 11; s++)
            {
                if (res[s] != null) continue;
                foreach (var p in pool)
                {
                    if (!used.Contains(p.id)) { res[s] = p; used.Add(p.id); break; }
                }
            }
            var list = new List<PlayerData>();
            foreach (var p in res) if (p != null) list.Add(p);
            return list;
        }

        public static int TeamRating(List<PlayerData> eleven)
        {
            if (eleven == null || eleven.Count == 0) return 0;
            float s = 0;
            foreach (var p in eleven) s += p.overall;
            return Mathf.RoundToInt(s / eleven.Count);
        }
    }
}
