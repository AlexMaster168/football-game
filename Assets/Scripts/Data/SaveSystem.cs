using System.Collections.Generic;
using UnityEngine;

namespace FD
{
    /// <summary>Прокачка игрока на тренировках (прибавки к базовым характеристикам).</summary>
    [System.Serializable]
    public class Training
    {
        public int id;
        public int[] add = new int[6];   // pace, shooting, passing, dribbling, defending, keeping

        public int Total { get { int s = 0; foreach (var v in add) s += v; return s; } }
    }

    public class StaffRole
    {
        public string name, desc;
        public string[] perLevel;
    }

    [System.Serializable]
    public class Career
    {
        public string teamName = "Dream FC";
        public int coins = 1000;
        public List<int> owned = new List<int>();
        public List<int> lineup = new List<int>();   // 11 id в порядке слотов Formation
        public int played, wins, draws, losses, goalsFor, goalsAgainst;
        public List<Training> training = new List<Training>();
        public int[] staff = new int[6];            // уровни персонала 0..3 (см. Staff)

        public const int MaxSquad = 25;
        public const int MaxStaffLevel = 3;
        public static readonly string[] StatNames = { "Скорость", "Удар", "Пас", "Дриблинг", "Защита", "Вратарь" };
        public static readonly int[] StaffCost = { 1500, 4000, 9000 };

        public const int Coach = 0, Fitness = 1, GkCoach = 2, Trainer = 3, Scout = 4, Sponsor = 5;
        public static readonly StaffRole[] Staff =
        {
            new StaffRole { name = "Главный тренер", desc = "Прибавка ко всем характеристикам основы в матче", perLevel = new[] { "+1", "+2", "+3" } },
            new StaffRole { name = "Тренер по физподготовке", desc = "Прибавка к скорости всей команды в матче", perLevel = new[] { "+2", "+4", "+6" } },
            new StaffRole { name = "Тренер вратарей", desc = "Прибавка к игре вратаря в матче", perLevel = new[] { "+3", "+6", "+9" } },
            new StaffRole { name = "Тренер-методист", desc = "Тренировки дешевле", perLevel = new[] { "-15%", "-30%", "-45%" } },
            new StaffRole { name = "Скаут", desc = "Скидка на покупку игроков", perLevel = new[] { "-10%", "-20%", "-30%" } },
            new StaffRole { name = "Спонсор-менеджер", desc = "Больше монет за матчи", perLevel = new[] { "+20%", "+40%", "+60%" } },
        };

        public int StaffLevel(int role) => staff != null && role < staff.Length ? staff[role] : 0;

        public Training GetTraining(int id, bool create)
        {
            foreach (var t in training) if (t.id == id) return t;
            if (!create) return null;
            var nt = new Training { id = id };
            training.Add(nt);
            return nt;
        }

        /// <summary>Игрок с учётом тренировок (копия, база не меняется).</summary>
        public PlayerData Effective(int id)
        {
            var b = Database.Get(id);
            if (b == null) return null;
            var t = GetTraining(id, false);
            if (t == null || t.Total == 0) return b;
            var p = b.Clone();
            p.pace = Mathf.Min(99, p.pace + t.add[0]);
            p.shooting = Mathf.Min(99, p.shooting + t.add[1]);
            p.passing = Mathf.Min(99, p.passing + t.add[2]);
            p.dribbling = Mathf.Min(99, p.dribbling + t.add[3]);
            p.defending = Mathf.Min(99, p.defending + t.add[4]);
            p.keeping = Mathf.Min(99, p.keeping + t.add[5]);
            p.overall = Mathf.Min(99, p.overall + t.Total / 3);
            return p;
        }

        public static int GetStat(PlayerData p, int s)
        {
            switch (s)
            {
                case 0: return p.pace;
                case 1: return p.shooting;
                case 2: return p.passing;
                case 3: return p.dribbling;
                case 4: return p.defending;
                default: return p.keeping;
            }
        }

        public int TrainCost(int id)
        {
            var p = Effective(id);
            var t = GetTraining(id, false);
            int done = t != null ? t.Total : 0;
            float cost = (30f + p.overall * 3f) * (1f + done * 0.12f) * (1f - 0.15f * StaffLevel(Trainer));
            return Mathf.Max(10, Mathf.RoundToInt(cost / 10f) * 10);
        }

        /// <summary>Тренировка: +1 к характеристике за монеты.</summary>
        public bool Train(int id, int stat)
        {
            var p = Effective(id);
            if (p == null || GetStat(p, stat) >= 99) return false;
            int cost = TrainCost(id);
            if (coins < cost) return false;
            coins -= cost;
            GetTraining(id, true).add[stat]++;
            return true;
        }

        public int BuyPrice(PlayerData p) => Mathf.RoundToInt(p.Price * (1f - 0.1f * StaffLevel(Scout)) / 10f) * 10;
        public int SellPrice(int id) => Database.PriceFor(Effective(id).overall) / 2;
        public float CoinMultiplier => 1f + 0.2f * StaffLevel(Sponsor);

        public int StaffUpgradeCost(int role)
        {
            int lvl = StaffLevel(role);
            return lvl >= MaxStaffLevel ? 0 : StaffCost[lvl];
        }

        public bool HireStaff(int role)
        {
            int lvl = StaffLevel(role);
            if (lvl >= MaxStaffLevel || coins < StaffCost[lvl]) return false;
            coins -= StaffCost[lvl];
            staff[role] = lvl + 1;
            return true;
        }

        /// <summary>Состав на матч: тренировки + бонусы персонала.</summary>
        public List<PlayerData> MatchLineup()
        {
            int coach = StaffLevel(Coach), fit = StaffLevel(Fitness) * 2, gk = StaffLevel(GkCoach) * 3;
            var list = new List<PlayerData>();
            foreach (var src in LineupPlayers())
            {
                var p = src.Clone();
                p.pace = Mathf.Min(99, p.pace + coach + fit);
                p.shooting = Mathf.Min(99, p.shooting + coach);
                p.passing = Mathf.Min(99, p.passing + coach);
                p.dribbling = Mathf.Min(99, p.dribbling + coach);
                p.defending = Mathf.Min(99, p.defending + coach);
                if (p.pos == Pos.GK) p.keeping = Mathf.Min(99, p.keeping + coach + gk);
                p.overall = Mathf.Min(99, p.overall + coach);
                list.Add(p);
            }
            return list;
        }

        public List<PlayerData> LineupPlayers()
        {
            var list = new List<PlayerData>();
            foreach (var id in lineup)
            {
                var p = Effective(id);
                if (p != null) list.Add(p);
            }
            return list;
        }

        public List<PlayerData> OwnedPlayers()
        {
            var list = new List<PlayerData>();
            foreach (var id in owned)
            {
                var p = Effective(id);
                if (p != null) list.Add(p);
            }
            return list;
        }

        public void AutoLineup()
        {
            lineup.Clear();
            foreach (var p in Database.AutoLineup(OwnedPlayers())) lineup.Add(p.id);
        }

        /// <summary>Чинит сейв, если что-то поехало (удалённые игроки, неполный состав).</summary>
        public void Validate()
        {
            if (staff == null || staff.Length < 6) staff = new int[6];
            if (training == null) training = new List<Training>();
            training.RemoveAll(t => t.add == null || t.add.Length < 6);
            owned.RemoveAll(id => Database.Get(id) == null);
            lineup.RemoveAll(id => !owned.Contains(id));
            if (lineup.Count != 11 || new HashSet<int>(lineup).Count != 11) AutoLineup();
        }
    }

    public static class Settings
    {
        public static int MatchMinutes
        {
            get => PlayerPrefs.GetInt("fd_minutes", 4);
            set { PlayerPrefs.SetInt("fd_minutes", value); PlayerPrefs.Save(); }
        }

        /// <summary>0 = легко, 1 = нормально, 2 = сложно</summary>
        public static int Difficulty
        {
            get => PlayerPrefs.GetInt("fd_difficulty", 1);
            set { PlayerPrefs.SetInt("fd_difficulty", value); PlayerPrefs.Save(); }
        }
    }

    public static class SaveSystem
    {
        const string CareerKey = "fd_career";
        const string WcKey = "fd_worldcup";

        /// <summary>В автотесте сейвы не трогаем.</summary>
        public static bool Disabled;

        public static Career LoadCareer()
        {
            Career c = null;
            if (!Disabled && PlayerPrefs.HasKey(CareerKey))
            {
                try { c = JsonUtility.FromJson<Career>(PlayerPrefs.GetString(CareerKey)); }
                catch { c = null; }
            }
            if (c == null || c.owned.Count < 11)
            {
                c = new Career();
                foreach (var p in Database.Academy) c.owned.Add(p.id);
                c.AutoLineup();
            }
            c.Validate();
            return c;
        }

        public static void SaveCareer(Career c)
        {
            if (Disabled) return;
            PlayerPrefs.SetString(CareerKey, JsonUtility.ToJson(c));
            PlayerPrefs.Save();
        }

        public static WorldCupState LoadWC()
        {
            if (Disabled || !PlayerPrefs.HasKey(WcKey)) return null;
            try
            {
                var s = JsonUtility.FromJson<WorldCupState>(PlayerPrefs.GetString(WcKey));
                return s != null && s.groups != null && s.groups.Count == 12 ? s : null;
            }
            catch { return null; }
        }

        public static void SaveWC(WorldCupState s)
        {
            if (Disabled) return;
            if (s == null) PlayerPrefs.DeleteKey(WcKey);
            else PlayerPrefs.SetString(WcKey, JsonUtility.ToJson(s));
            PlayerPrefs.Save();
        }

        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(CareerKey);
            PlayerPrefs.DeleteKey(WcKey);
            PlayerPrefs.Save();
        }
    }
}
