using System.Collections.Generic;
using UnityEngine;

namespace FD
{
    /// <summary>Схема 4-3-3. x: -1..1 поперёк поля (лево/право), y: 0..1 от своих ворот к чужим.</summary>
    public static class Formation
    {
        public static readonly Vector2[] Slots =
        {
            new Vector2(0f, 0.04f),
            new Vector2(-0.72f, 0.24f), new Vector2(-0.26f, 0.2f), new Vector2(0.26f, 0.2f), new Vector2(0.72f, 0.24f),
            new Vector2(-0.42f, 0.42f), new Vector2(0f, 0.37f), new Vector2(0.42f, 0.42f),
            new Vector2(-0.7f, 0.64f), new Vector2(0f, 0.7f), new Vector2(0.7f, 0.64f),
        };

        public static readonly Pos[] SlotPos =
        {
            Pos.GK, Pos.DEF, Pos.DEF, Pos.DEF, Pos.DEF, Pos.MID, Pos.MID, Pos.MID, Pos.FWD, Pos.FWD, Pos.FWD
        };

        public static readonly string[] SlotNames = { "ВРТ", "ЛЗ", "ЦЗ", "ЦЗ", "ПЗ", "ЛПЗ", "ЦПЗ", "ППЗ", "ЛН", "ФРВ", "ПН" };
    }

    public class TeamSheet
    {
        public string name, code;
        public Color kit, kit2;
        public List<PlayerData> players;   // 11 в порядке слотов
        public int Rating => Database.TeamRating(players);

        public static TeamSheet FromNation(int index)
        {
            var n = Database.Nations[index];
            return new TeamSheet { name = n.name, code = n.code, kit = n.kit, kit2 = n.kit2, players = Database.AutoLineup(n.squad) };
        }

        public static TeamSheet FromCareer(Career c)
        {
            return new TeamSheet
            {
                name = c.teamName,
                code = MakeCode(c.teamName),
                kit = new Color(0.48f, 0.17f, 0.75f),
                kit2 = Color.white,
                players = c.MatchLineup()
            };
        }

        static string MakeCode(string name)
        {
            string s = "";
            foreach (char ch in name) if (char.IsLetter(ch)) s += char.ToUpperInvariant(ch);
            if (s.Length < 3) s = (s + "DRM").Substring(0, 3);
            return s.Substring(0, 3);
        }
    }

    public class MatchConfig
    {
        public TeamSheet home, away;
        public bool homeHuman = true;
        public bool knockout;
        public float durationSeconds = 240f;
        public string title = "Товарищеский матч";
    }

    public class MatchResult
    {
        public string homeName, awayName;
        public int homeGoals, awayGoals, homePens, awayPens;
        public bool pens, forfeit;
        public int homeShots, awayShots;
        public float homePossession = 0.5f;
        public readonly List<string> scorers = new List<string>();

        /// <summary>1 — победа хозяев, 0 — ничья, -1 — поражение (с учётом пенальти).</summary>
        public int HomeOutcome
        {
            get
            {
                if (homeGoals != awayGoals) return homeGoals > awayGoals ? 1 : -1;
                if (pens) return homePens > awayPens ? 1 : -1;
                return 0;
            }
        }
    }

    public struct PadInput
    {
        public Vector3 move;
        public bool sprint, passDown, lobDown, shootHeld, shootDown, switchDown;
    }

    public class TeamController
    {
        public string name, code;
        public int sign;                 // +1 атакует в сторону +z
        public bool human;
        public Color kit, shorts, gkKit;
        public int rating;
        public readonly List<FootballPlayer> players = new List<FootballPlayer>();
        public FootballPlayer controlled, chaser, chaser2;
        public TeamController opp;

        // модификаторы сложности (для ИИ-соперника)
        public float speedMul = 1f, reaction = 1f, tackleBonus, gkBonus;
        public bool doublePress;

        public Vector3 AttackDir => new Vector3(0f, 0f, sign);
        public Vector3 OppGoal => new Vector3(0f, 0f, sign * MatchManager.HalfL);
        public Vector3 OwnGoal => new Vector3(0f, 0f, -sign * MatchManager.HalfL);
        public FootballPlayer GK => players[0];

        /// <summary>0 — у своих ворот, 1 — у чужих.</summary>
        public float Progress(Vector3 p) => (p.z * sign + MatchManager.HalfL) / (2f * MatchManager.HalfL);

        public Vector3 SlotWorld(int slot, float zNorm, float xOffset)
        {
            float x = Formation.Slots[slot].x * MatchManager.HalfW * 0.85f * sign + xOffset;
            x = Mathf.Clamp(x, -MatchManager.HalfW + 1.5f, MatchManager.HalfW - 1.5f);
            return new Vector3(x, 0f, sign * (zNorm * 2f * MatchManager.HalfL - MatchManager.HalfL));
        }

        public bool InOwnBox(Vector3 p)
        {
            return Mathf.Abs(p.z - OwnGoal.z) < 16.5f && Mathf.Abs(p.x) < 20.16f;
        }
    }
}
