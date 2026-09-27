using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FD
{
    /// <summary>
    /// Точка входа. Создаётся сама при старте любой сцены — ничего настраивать не надо.
    /// Меню, «Моя команда», трансферный рынок, ЧМ-2026, товарищеские матчи.
    /// </summary>
    public class Game : MonoBehaviour
    {
        public static Game I;

        enum Scr { Main, Dream, Transfers, Training, Staff, Opponent, WCSelect, WCHub, Friendly, Settings, Match, Post }

        Scr scr = Scr.Main;
        Career career;
        WorldCupState wc;
        Camera cam;
        float orbit;

        // экран «Моя команда»
        int selectedSlot = -1;
        Vector2 benchScroll;

        // трансферы
        int posFilter;        // 0 все, 1..4 = GK..FWD
        int sortMode;         // 0 рейтинг, 1 цена
        bool sellTab;
        string search = "";
        Vector2 marketScroll;
        List<PlayerData> marketCache;
        string marketKey;
        string toast;
        float toastTimer;

        // выбор соперника / нации
        Vector2 gridScroll, friendlyScrollA, friendlyScrollB;
        int friendlyA = -1, friendlyB = -1;
        bool friendlyWatch;

        // ЧМ
        bool wcShowKnockout;
        Vector2 wcScroll;

        // пост-матч
        MatchResult postResult;
        readonly List<string> postLines = new List<string>();
        string postTitle;
        Action postContinue;

        // автотест (-autotest в командной строке)
        bool autotest;
        public static readonly List<string> TestLog = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I == null) new GameObject("FootballDream").AddComponent<Game>();
        }

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            autotest = Environment.GetCommandLineArgs().Contains("-autotest");
            SaveSystem.Disabled = autotest;

            Database.Build();
            career = SaveSystem.LoadCareer();
            wc = SaveSystem.LoadWC();

            SetupCameraAndLight();
            Stadium.Build();
            Sfx.Init(gameObject);

            if (autotest) StartCoroutine(AutoTest());
        }

        void SetupCameraAndLight()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.farClipPlane = 600f;
            cam.fieldOfView = 40f;
            cam.clearFlags = CameraClearFlags.Skybox;

            var light = FindAnyObjectByType<Light>();
            if (light == null) light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(48f, -38f, 0f);
            light.color = new Color(1f, 0.95f, 0.86f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.72f;
            light.shadowBias = 0.03f;
            light.shadowNormalBias = 0.25f;

            // качество картинки
            QualitySettings.antiAliasing = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowDistance = 140f;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.pixelLightCount = 4;
            cam.allowMSAA = true;
            cam.allowHDR = false;
            cam.renderingPath = RenderingPath.Forward;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.7f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.53f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.32f, 0.22f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.86f);
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 520f;
        }

        void Update()
        {
            if (toastTimer > 0f) toastTimer -= Time.deltaTime;
            Stadium.Tick(Time.deltaTime);
            if (scr != Scr.Match && cam != null)
            {
                orbit += Time.deltaTime * 4f;
                float a = orbit * Mathf.Deg2Rad;
                cam.transform.position = new Vector3(Mathf.Sin(a) * 75f, 32f, Mathf.Cos(a) * 75f);
                cam.transform.LookAt(Vector3.zero);
                cam.fieldOfView = 45f;
            }
        }

        // ================================================================== матчи

        void StartMatch(MatchConfig cfg, Action<MatchResult> done)
        {
            scr = Scr.Match;
            MatchManager.Create(cfg, r =>
            {
                done(r);
            });
        }

        void ShowPost(string title, MatchResult r, Action cont)
        {
            postTitle = title;
            postResult = r;
            postContinue = cont;
            scr = Scr.Post;
        }

        int Reward(int rating) => 120 + Mathf.Max(0, rating - 60) * 12;

        void PlayDreamMatch(int nation)
        {
            var away = TeamSheet.FromNation(nation);
            var cfg = new MatchConfig
            {
                home = TeamSheet.FromCareer(career),
                away = away,
                durationSeconds = Settings.MatchMinutes * 60f,
                title = "Моя команда — товарищеский матч"
            };
            int reward = Reward(away.Rating);
            StartMatch(cfg, r =>
            {
                int o = r.HomeOutcome;
                int coins = Mathf.RoundToInt(((o > 0 ? reward : o == 0 ? reward * 2 / 5 : reward * 3 / 20) + r.homeGoals * 30) * career.CoinMultiplier);
                if (r.forfeit) coins = 0;
                career.coins += coins;
                career.played++;
                if (o > 0) career.wins++; else if (o == 0) career.draws++; else career.losses++;
                career.goalsFor += r.homeGoals;
                career.goalsAgainst += r.awayGoals;
                SaveSystem.SaveCareer(career);
                postLines.Clear();
                postLines.Add(o > 0 ? "Победа!" : o == 0 ? "Ничья" : "Поражение");
                postLines.Add("Заработано монет: +" + coins);
                ShowPost("Моя команда", r, () => scr = Scr.Dream);
            });
        }

        void PlayWorldCupMatch(Fixture f)
        {
            bool userIsA = f.a == wc.user;
            int opp = userIsA ? f.b : f.a;
            bool ko = wc.stage >= 3;
            var cfg = new MatchConfig
            {
                home = TeamSheet.FromNation(wc.user),
                away = TeamSheet.FromNation(opp),
                knockout = ko,
                durationSeconds = Settings.MatchMinutes * 60f,
                title = "ЧМ-2026 • " + WorldCup.StageName(wc)
            };
            StartMatch(cfg, r =>
            {
                FinishWorldCupFixture(f, r);
                ShowPost("Чемпионат мира 2026", r, () => scr = Scr.WCHub);
            });
        }

        void FinishWorldCupFixture(Fixture f, MatchResult r)
        {
            bool userIsA = f.a == wc.user;
            if (userIsA) WorldCup.RecordResult(wc, f, r.homeGoals, r.awayGoals, r.homePens, r.awayPens);
            else WorldCup.RecordResult(wc, f, r.awayGoals, r.homeGoals, r.awayPens, r.homePens);
            WorldCup.AdvanceStage(wc);
            postLines.Clear();
            postLines.Add(WorldCup.StageName(wc));
            CheckWorldCupPrize();
            SaveSystem.SaveWC(wc);
        }

        void CheckWorldCupPrize()
        {
            if (wc == null || wc.prizeGiven || !(wc.userOut || wc.finished)) return;
            wc.prizeGiven = true;
            int prize = WorldCup.Prize(wc);
            career.coins += prize;
            SaveSystem.SaveCareer(career);
            string msg = wc.champion == wc.user ? "ЧЕМПИОНЫ МИРА! " : "Ваш ЧМ закончен. ";
            postLines.Add(msg + "Приз для «Моей команды»: +" + prize + " монет");
            Toast(msg + "+" + prize + " монет");
        }

        void Toast(string s)
        {
            toast = s;
            toastTimer = 3f;
        }

        // ================================================================== GUI

        void OnGUI()
        {
            if (scr == Scr.Match) return;
            UI.Begin();
            float vw = UI.VW;
            UI.Box(new Rect(0, 0, vw, UI.VH), new Color(0.01f, 0.03f, 0.08f, 0.55f));

            switch (scr)
            {
                case Scr.Main: DrawMain(vw); break;
                case Scr.Dream: DrawDream(vw); break;
                case Scr.Transfers: DrawTransfers(vw); break;
                case Scr.Training: DrawTraining(vw); break;
                case Scr.Staff: DrawStaff(vw); break;
                case Scr.Opponent: DrawOpponent(vw); break;
                case Scr.WCSelect: DrawWCSelect(vw); break;
                case Scr.WCHub: DrawWCHub(vw); break;
                case Scr.Friendly: DrawFriendly(vw); break;
                case Scr.Settings: DrawSettings(vw); break;
                case Scr.Post: DrawPost(vw); break;
            }

            if (toastTimer > 0f && !string.IsNullOrEmpty(toast))
            {
                var r = new Rect(vw / 2 - 300, UI.VH - 80, 600, 46);
                UI.Box(r, new Color(0.1f, 0.4f, 0.2f, 0.95f));
                GUI.Label(r, toast, UI.labelC);
            }
        }

        bool Back(float vw)
        {
            return GUI.Button(new Rect(20, UI.VH - 70, 180, 50), "← Назад", UI.button);
        }

        void Header(float vw, string text)
        {
            GUI.Label(new Rect(30, 14, vw - 60, 50), text, UI.h1);
            GUI.Label(new Rect(vw - 330, 14, 300, 50), "Монеты: " + career.coins, UI.h2);
        }

        // ---------------------------------------------------------------- главное меню

        void DrawMain(float vw)
        {
            GUI.Label(new Rect(0, 60, vw, 90), "FOOTBALL DREAM", UI.title);
            GUI.Label(new Rect(0, 140, vw, 34), "Собери команду мечты  •  Возьми Кубок мира 2026", UI.subtitle);

            float bw = 440, bh = 62, x = vw / 2 - bw / 2, y = 215;
            if (GUI.Button(new Rect(x, y, bw, bh), "Моя команда", UI.bigButton)) scr = Scr.Dream;
            y += 76;
            if (wc != null && !wc.finished)
            {
                if (GUI.Button(new Rect(x, y, bw, bh), "Продолжить ЧМ-2026 (" + Database.Nations[wc.user].name + ")", UI.bigButton)) scr = Scr.WCHub;
                y += 76;
                if (GUI.Button(new Rect(x, y, bw, 44), "Новый ЧМ-2026", UI.button)) scr = Scr.WCSelect;
                y += 58;
            }
            else
            {
                if (GUI.Button(new Rect(x, y, bw, bh), "Чемпионат мира 2026", UI.bigButton)) scr = Scr.WCSelect;
                y += 76;
            }
            if (GUI.Button(new Rect(x, y, bw, bh), "Товарищеский матч", UI.bigButton)) scr = Scr.Friendly;
            y += 76;
            if (GUI.Button(new Rect(x, y, bw / 2 - 6, 50), "Настройки", UI.button)) scr = Scr.Settings;
            if (GUI.Button(new Rect(x + bw / 2 + 6, y, bw / 2 - 6, 50), "Выход", UI.button)) Application.Quit();

            GUI.Label(new Rect(20, UI.VH - 40, 600, 30), "«" + career.teamName + "»  •  монеты: " + career.coins + "  •  рейтинг: " + Database.TeamRating(career.LineupPlayers()), UI.small);
        }

        // ---------------------------------------------------------------- моя команда

        void DrawDream(float vw)
        {
            Header(vw, "Моя команда");
            var lineup = career.LineupPlayers();

            GUI.Label(new Rect(30, 70, 120, 36), "Название:", UI.label);
            string newName = GUI.TextField(new Rect(130, 70, 260, 36), career.teamName, 20, UI.field);
            if (newName != career.teamName) { career.teamName = newName; SaveSystem.SaveCareer(career); }
            GUI.Label(new Rect(410, 70, 700, 36),
                "Рейтинг: " + Database.TeamRating(lineup) + "   Матчи: " + career.played + "  (В " + career.wins + " / Н " + career.draws + " / П " + career.losses + ")   Голы: " + career.goalsFor + "-" + career.goalsAgainst,
                UI.label);

            // поле со схемой
            var pitch = new Rect(30, 118, 600, 500);
            UI.Box(pitch, new Color(0.13f, 0.42f, 0.17f, 0.95f));
            UI.Box(new Rect(pitch.x, pitch.y + pitch.height / 2 - 1, pitch.width, 2), new Color(1, 1, 1, 0.4f));
            UI.Box(new Rect(pitch.x + pitch.width / 2 - 90, pitch.y, 180, 2), new Color(1, 1, 1, 0.4f));
            UI.Box(new Rect(pitch.x + pitch.width / 2 - 140, pitch.yMax - 80, 280, 2), new Color(1, 1, 1, 0.4f));
            // ряды схемы 4-3-3: {первый слот, кол-во, высота ряда 0..1 снизу}
            int[,] rowsDef = { { 0, 1 }, { 1, 4 }, { 5, 3 }, { 8, 3 } };
            float[] rowY = { 0.06f, 0.33f, 0.6f, 0.87f };
            for (int row = 0; row < 4; row++)
            {
                int start = rowsDef[row, 0], count = rowsDef[row, 1];
                for (int k = 0; k < count; k++)
                {
                    int s = start + k;
                    if (s >= lineup.Count) continue;
                    float cx = pitch.x + pitch.width * (k + 1f) / (count + 1f);
                    float cy = pitch.yMax - rowY[row] * pitch.height - 20;
                    var p = lineup[s];
                    bool wrongPos = p.pos != Formation.SlotPos[s];
                    string txt = Formation.SlotNames[s] + "  " + p.overall + (wrongPos ? " (!)" : "") + "\n" + Short(p.name);
                    if (GUI.Button(new Rect(cx - 66, cy - 28, 132, 56), txt, s == selectedSlot ? UI.selButton : UI.rowButton))
                        selectedSlot = selectedSlot == s ? -1 : s;
                }
            }

            // скамейка
            var bench = career.OwnedPlayers().Where(p => !career.lineup.Contains(p.id)).OrderByDescending(p => p.overall).ToList();
            var br = new Rect(650, 118, vw - 680, 500);
            UI.Panel(br);
            GUI.Label(new Rect(br.x + 12, br.y + 6, br.width - 24, 30),
                selectedSlot >= 0 ? "Замена для слота " + Formation.SlotNames[selectedSlot] + " — выбери игрока:" : "Запасные (" + career.owned.Count + "/" + Career.MaxSquad + ")  •  клик по игроку на поле = замена",
                UI.small);
            float rowH = 40;
            var view = new Rect(br.x + 6, br.y + 40, br.width - 12, br.height - 46);
            benchScroll = GUI.BeginScrollView(view, benchScroll, new Rect(0, 0, view.width - 20, bench.Count * rowH));
            for (int i = 0; i < bench.Count; i++)
            {
                var p = bench[i];
                var r = new Rect(0, i * rowH, view.width - 20, rowH - 4);
                UI.Box(r, i % 2 == 0 ? new Color(1, 1, 1, 0.05f) : new Color(1, 1, 1, 0.1f));
                GUI.Label(new Rect(r.x + 8, r.y, 50, r.height), p.PosName, UI.small);
                GUI.Label(new Rect(r.x + 52, r.y, 220, r.height), p.name, UI.label);
                GUI.Label(new Rect(r.x + 270, r.y, 60, r.height), p.overall.ToString(), UI.h2);
                if (selectedSlot >= 0)
                {
                    if (GUI.Button(new Rect(r.xMax - 150, r.y + 3, 140, r.height - 6), "В состав", UI.selButton))
                    {
                        career.lineup[selectedSlot] = p.id;
                        selectedSlot = -1;
                        SaveSystem.SaveCareer(career);
                    }
                }
                else if (career.owned.Count > 11)
                {
                    int sell = career.SellPrice(p.id);
                    if (GUI.Button(new Rect(r.xMax - 170, r.y + 3, 160, r.height - 6), "Продать +" + sell, UI.rowButton))
                    {
                        career.owned.Remove(p.id);
                        career.coins += sell;
                        SaveSystem.SaveCareer(career);
                        marketCache = null;
                        Toast("Продан " + p.name + " за " + sell);
                    }
                }
            }
            GUI.EndScrollView();

            float by = UI.VH - 80;
            if (Back(vw)) { selectedSlot = -1; scr = Scr.Main; }
            if (GUI.Button(new Rect(215, by + 10, 170, 50), "Авто-состав", UI.button)) { career.AutoLineup(); SaveSystem.SaveCareer(career); }
            if (GUI.Button(new Rect(395, by + 10, 170, 50), "Трансферы", UI.button)) { marketCache = null; scr = Scr.Transfers; }
            if (GUI.Button(new Rect(575, by + 10, 170, 50), "Тренировки", UI.button)) scr = Scr.Training;
            if (GUI.Button(new Rect(755, by + 10, 170, 50), "Персонал", UI.button)) scr = Scr.Staff;
            if (GUI.Button(new Rect(vw - 280, by + 4, 260, 62), "Играть матч ▶", UI.bigButton)) scr = Scr.Opponent;
        }

        static string Short(string name)
        {
            int i = name.IndexOf(' ');
            return i > 0 && name.Length > 11 ? name[0] + ". " + name.Substring(i + 1) : name;
        }

        // ---------------------------------------------------------------- трансферы

        void DrawTransfers(float vw)
        {
            Header(vw, "Трансферный рынок");

            if (GUI.Button(new Rect(30, 70, 150, 38), "Купить", sellTab ? UI.tab : UI.tabOn)) { sellTab = false; marketCache = null; }
            if (GUI.Button(new Rect(185, 70, 150, 38), "Мои игроки", sellTab ? UI.tabOn : UI.tab)) { sellTab = true; marketCache = null; }

            string[] posNames = { "Все", "ВРТ", "ЗАЩ", "ПЗ", "НАП" };
            for (int i = 0; i < posNames.Length; i++)
                if (GUI.Button(new Rect(370 + i * 84, 70, 80, 38), posNames[i], posFilter == i ? UI.tabOn : UI.tab)) posFilter = i;
            if (GUI.Button(new Rect(800, 70, 170, 38), sortMode == 0 ? "По рейтингу" : "По цене", UI.tab)) sortMode = 1 - sortMode;
            GUI.Label(new Rect(990, 70, 70, 38), "Поиск:", UI.small);
            search = GUI.TextField(new Rect(1050, 70, Mathf.Max(150, vw - 1080), 38), search, 30, UI.field);

            string key = sellTab + "|" + posFilter + "|" + sortMode + "|" + search + "|" + career.owned.Count;
            if (marketCache == null || key != marketKey)
            {
                marketKey = key;
                IEnumerable<PlayerData> q = sellTab ? career.OwnedPlayers() : Database.Market.Where(p => !career.owned.Contains(p.id));
                if (posFilter > 0) q = q.Where(p => (int)p.pos == posFilter - 1);
                if (!string.IsNullOrEmpty(search))
                {
                    string s = search.ToLowerInvariant();
                    q = q.Where(p => p.name.ToLowerInvariant().Contains(s) || p.NationName.ToLowerInvariant().Contains(s) || p.club.ToLowerInvariant().Contains(s) || p.NationCode.ToLowerInvariant().Contains(s));
                }
                q = sortMode == 0 ? q.OrderByDescending(p => p.overall) : q.OrderBy(p => p.Price);
                marketCache = q.ToList();
            }

            var area = new Rect(30, 118, vw - 60, UI.VH - 208);
            UI.Panel(area);
            float y0 = area.y + 6;
            GUI.Label(new Rect(area.x + 12, y0, 60, 26), "Поз", UI.small);
            GUI.Label(new Rect(area.x + 70, y0, 260, 26), "Игрок", UI.small);
            GUI.Label(new Rect(area.x + 330, y0, 200, 26), "Клуб • сборная", UI.small);
            GUI.Label(new Rect(area.x + 530, y0, 60, 26), "ОВР", UI.small);
            GUI.Label(new Rect(area.x + 590, y0, 330, 26), "СКО   УДР   ПАС   ДРБ   ЗАЩ   ВРТ", UI.small);
            GUI.Label(new Rect(area.x + 930, y0, 120, 26), sellTab ? "Продажа" : "Цена", UI.small);

            float rowH = 36;
            var view = new Rect(area.x + 4, area.y + 34, area.width - 8, area.height - 40);
            var content = new Rect(0, 0, view.width - 20, marketCache.Count * rowH);
            marketScroll = GUI.BeginScrollView(view, marketScroll, content);
            int first = Mathf.Max(0, (int)(marketScroll.y / rowH));
            int last = Mathf.Min(marketCache.Count, first + (int)(view.height / rowH) + 2);
            for (int i = first; i < last; i++)
            {
                var p = marketCache[i];
                var r = new Rect(0, i * rowH, content.width, rowH - 3);
                UI.Box(r, i % 2 == 0 ? new Color(1, 1, 1, 0.04f) : new Color(1, 1, 1, 0.09f));
                GUI.Label(new Rect(r.x + 8, r.y, 60, r.height), p.PosName, UI.small);
                GUI.Label(new Rect(r.x + 66, r.y, 260, r.height), p.name, UI.label);
                GUI.Label(new Rect(r.x + 326, r.y, 200, r.height), p.club + " • " + p.NationCode, UI.small);
                GUI.Label(new Rect(r.x + 526, r.y, 60, r.height), p.overall.ToString(), UI.h2);
                GUI.Label(new Rect(r.x + 586, r.y, 340, r.height),
                    $"{p.pace,3}    {p.shooting,3}    {p.passing,3}    {p.dribbling,3}    {p.defending,3}    {p.keeping,3}", UI.small);
                if (sellTab)
                {
                    int sell = career.SellPrice(p.id);
                    bool inLineup = career.lineup.Contains(p.id);
                    GUI.enabled = !inLineup && career.owned.Count > 11;
                    if (GUI.Button(new Rect(r.xMax - 200, r.y + 3, 190, r.height - 6), inLineup ? "в основе" : "Продать +" + sell, UI.rowButton))
                    {
                        career.owned.Remove(p.id);
                        career.coins += sell;
                        SaveSystem.SaveCareer(career);
                        marketCache = null;
                        Toast("Продан " + p.name + " за " + sell);
                    }
                    GUI.enabled = true;
                }
                else
                {
                    int price = career.BuyPrice(p);
                    GUI.Label(new Rect(r.x + 926, r.y, 120, r.height), price.ToString(), UI.label);
                    bool can = career.coins >= price && career.owned.Count < Career.MaxSquad;
                    GUI.enabled = can;
                    if (GUI.Button(new Rect(r.xMax - 160, r.y + 3, 150, r.height - 6), "Купить", can ? UI.selButton : UI.rowButton))
                    {
                        career.coins -= price;
                        career.owned.Add(p.id);
                        SaveSystem.SaveCareer(career);
                        marketCache = null;
                        Toast("Куплен " + p.name + " (" + p.overall + ")! Поставь его в состав.");
                    }
                    GUI.enabled = true;
                }
            }
            GUI.EndScrollView();

            GUI.Label(new Rect(220, UI.VH - 70, 700, 50),
                sellTab ? "Продажа = половина цены. Игроков из основы продать нельзя." : "Состав: " + career.owned.Count + "/" + Career.MaxSquad + "   •   Монеты зарабатываются в матчах и на ЧМ", UI.small);
            if (Back(vw)) scr = Scr.Dream;
        }

        // ---------------------------------------------------------------- тренировки

        int trainSelected = -1;
        Vector2 trainScroll;

        void DrawTraining(float vw)
        {
            Header(vw, "Тренировочный центр");
            var squad = career.OwnedPlayers().OrderByDescending(p => p.overall).ToList();
            if (trainSelected < 0 && squad.Count > 0) trainSelected = squad[0].id;

            var left = new Rect(30, 76, 470, UI.VH - 166);
            UI.Panel(left);
            float rowH = 40;
            var view = new Rect(left.x + 4, left.y + 4, left.width - 8, left.height - 8);
            trainScroll = GUI.BeginScrollView(view, trainScroll, new Rect(0, 0, view.width - 20, squad.Count * rowH));
            for (int i = 0; i < squad.Count; i++)
            {
                var p = squad[i];
                var t = career.GetTraining(p.id, false);
                string lvl = t != null && t.Total > 0 ? "  (+" + t.Total + ")" : "";
                var r = new Rect(0, i * rowH, view.width - 20, rowH - 4);
                if (GUI.Button(r, p.PosName + "   " + p.name + "   " + p.overall + lvl, p.id == trainSelected ? UI.selButton : UI.rowButton))
                    trainSelected = p.id;
            }
            GUI.EndScrollView();

            var right = new Rect(520, 76, vw - 550, UI.VH - 166);
            UI.Panel(right);
            var sel = career.Effective(trainSelected);
            if (sel != null)
            {
                var baseP = Database.Get(sel.id);
                var tr = career.GetTraining(sel.id, false);
                GUI.Label(new Rect(right.x + 20, right.y + 12, right.width - 40, 44), sel.name, UI.h1);
                GUI.Label(new Rect(right.x + 20, right.y + 56, right.width - 40, 28),
                    sel.PosName + "  •  " + sel.NationName + (string.IsNullOrEmpty(sel.club) ? "" : "  •  " + sel.club), UI.label);
                GUI.Label(new Rect(right.x + 20, right.y + 88, 400, 40), "Рейтинг: " + sel.overall + (sel.overall > baseP.overall ? "  (база " + baseP.overall + ")" : ""), UI.h2);

                int cost = career.TrainCost(sel.id);
                float y = right.y + 140;
                for (int s = 0; s < 6; s++)
                {
                    int val = Career.GetStat(sel, s);
                    int add = tr != null ? tr.add[s] : 0;
                    GUI.Label(new Rect(right.x + 20, y, 160, 40), Career.StatNames[s], UI.label);
                    UI.Box(new Rect(right.x + 180, y + 12, 250, 16), new Color(1, 1, 1, 0.1f));
                    UI.Box(new Rect(right.x + 180, y + 12, 250 * (val - add) / 99f, 16), UI.Accent);
                    if (add > 0) UI.Box(new Rect(right.x + 180 + 250 * (val - add) / 99f, y + 12, 250 * add / 99f, 16), new Color(0.3f, 0.9f, 0.4f));
                    GUI.Label(new Rect(right.x + 440, y, 110, 40), val + (add > 0 ? " (+" + add + ")" : ""), UI.label);
                    bool can = career.coins >= cost && val < 99;
                    GUI.enabled = can;
                    if (GUI.Button(new Rect(right.xMax - 190, y + 3, 170, 34), "+1 за " + cost, can ? UI.selButton : UI.rowButton))
                    {
                        if (career.Train(sel.id, s))
                        {
                            SaveSystem.SaveCareer(career);
                            Toast(sel.name + ": " + Career.StatNames[s] + " +1");
                        }
                    }
                    GUI.enabled = true;
                    y += 48;
                }
                GUI.Label(new Rect(right.x + 20, y + 10, right.width - 40, 60),
                    "Каждые 3 очка тренировок = +1 к рейтингу. Цена растёт с каждой тренировкой.\nТренер-методист в «Персонале» делает тренировки дешевле.", UI.small);
            }
            if (Back(vw)) scr = Scr.Dream;
        }

        // ---------------------------------------------------------------- персонал

        void DrawStaff(float vw)
        {
            Header(vw, "Персонал клуба");
            float cw = (vw - 80) / 3f, ch = 250;
            for (int i = 0; i < Career.Staff.Length; i++)
            {
                var role = Career.Staff[i];
                int lvl = career.StaffLevel(i);
                var r = new Rect(30 + (i % 3) * (cw + 10), 80 + (i / 3) * (ch + 14), cw, ch);
                UI.Panel(r);
                GUI.Label(new Rect(r.x + 16, r.y + 10, r.width - 32, 34), role.name, UI.h2);
                GUI.Label(new Rect(r.x + 16, r.y + 46, r.width - 32, 26), role.desc, UI.small);
                for (int l = 0; l < Career.MaxStaffLevel; l++)
                {
                    UI.Box(new Rect(r.x + 16 + l * 42, r.y + 86, 36, 12), l < lvl ? UI.Gold : new Color(1, 1, 1, 0.15f));
                }
                GUI.Label(new Rect(r.x + 150, r.y + 76, r.width - 166, 30),
                    lvl > 0 ? "Сейчас: " + role.perLevel[lvl - 1] : "Не нанят", UI.label);
                if (lvl < Career.MaxStaffLevel)
                {
                    GUI.Label(new Rect(r.x + 16, r.y + 116, r.width - 32, 28), "Уровень " + (lvl + 1) + ": " + role.perLevel[lvl], UI.small);
                    int cost = career.StaffUpgradeCost(i);
                    bool can = career.coins >= cost;
                    GUI.enabled = can;
                    if (GUI.Button(new Rect(r.x + 16, r.yMax - 70, r.width - 32, 54), (lvl == 0 ? "Нанять за " : "Повысить за ") + cost, can ? UI.selButton : UI.rowButton))
                    {
                        if (career.HireStaff(i))
                        {
                            SaveSystem.SaveCareer(career);
                            Toast(role.name + " — уровень " + career.StaffLevel(i));
                        }
                    }
                    GUI.enabled = true;
                }
                else GUI.Label(new Rect(r.x + 16, r.yMax - 64, r.width - 32, 40), "Максимальный уровень", UI.h2c);
            }
            GUI.Label(new Rect(220, UI.VH - 70, 800, 50), "Бонусы тренеров действуют в матчах «Моей команды».", UI.small);
            if (Back(vw)) scr = Scr.Dream;
        }

        // ---------------------------------------------------------------- выбор соперника

        void DrawOpponent(float vw)
        {
            Header(vw, "Выбери соперника");
            int myRating = Database.TeamRating(career.LineupPlayers());
            GUI.Label(new Rect(30, 64, vw - 60, 30), "Твой рейтинг: " + myRating + ". Чем сильнее соперник — тем больше монет за победу.", UI.small);
            var order = Enumerable.Range(0, Database.Nations.Count).OrderBy(i => Database.Nations[i].rating).ToList();
            int picked = NationGrid(vw, order, i => Database.Nations[i].name + "\n★ " + Database.Nations[i].rating + "   +" + Reward(Database.Nations[i].rating));
            if (picked >= 0) PlayDreamMatch(picked);
            if (Back(vw)) scr = Scr.Dream;
        }

        int NationGrid(float vw, List<int> order, Func<int, string> text)
        {
            int cols = 6;
            float gx = 30, gy = 100, gw = vw - 60, gh = UI.VH - 190;
            float bw = (gw - 20 - (cols - 1) * 8) / cols, bh = 60;
            int rows = Mathf.CeilToInt(order.Count / (float)cols);
            int picked = -1;
            gridScroll = GUI.BeginScrollView(new Rect(gx, gy, gw, gh), gridScroll, new Rect(0, 0, gw - 20, rows * (bh + 8)));
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                var r = new Rect((k % cols) * (bw + 8), (k / cols) * (bh + 8), bw, bh);
                UI.Box(new Rect(r.x, r.y, 8, r.height), Database.Nations[i].kit);
                if (GUI.Button(new Rect(r.x + 8, r.y, r.width - 8, r.height), text(i), UI.rowButton)) picked = i;
            }
            GUI.EndScrollView();
            return picked;
        }

        // ---------------------------------------------------------------- ЧМ

        void DrawWCSelect(float vw)
        {
            Header(vw, "ЧМ-2026: выбери сборную");
            GUI.Label(new Rect(30, 64, vw - 60, 30), "48 сборных • 12 групп • 1/16 финала • США, Мексика, Канада", UI.small);
            var order = Enumerable.Range(0, Database.Nations.Count).OrderByDescending(i => Database.Nations[i].rating).ToList();
            int picked = NationGrid(vw, order, i => Database.Nations[i].name + "\n★ " + Database.Nations[i].rating + "  •  " + Database.Nations[i].confed);
            if (picked >= 0)
            {
                wc = WorldCup.New(picked);
                SaveSystem.SaveWC(wc);
                wcShowKnockout = false;
                scr = Scr.WCHub;
            }
            if (Back(vw)) scr = Scr.Main;
        }

        void DrawWCHub(float vw)
        {
            var me = Database.Nations[wc.user];
            Header(vw, "ЧМ-2026 • " + me.name);

            // левая колонка
            var left = new Rect(30, 70, 400, UI.VH - 160);
            UI.Panel(left);
            GUI.Label(new Rect(left.x + 14, left.y + 8, left.width - 28, 32), WorldCup.StageName(wc), UI.h2);

            float y = left.y + 50;
            var f = WorldCup.UserFixture(wc);
            if (wc.finished)
            {
                GUI.Label(new Rect(left.x + 14, y, left.width - 28, 30), "Чемпион мира:", UI.label);
                GUI.Label(new Rect(left.x + 14, y + 30, left.width - 28, 44), Database.Nations[wc.champion].name, UI.h1);
                y += 90;
                if (GUI.Button(new Rect(left.x + 14, y, left.width - 28, 50), "Новый ЧМ", UI.button)) scr = Scr.WCSelect;
                y += 64;
            }
            else if (f != null)
            {
                int opp = f.a == wc.user ? f.b : f.a;
                GUI.Label(new Rect(left.x + 14, y, left.width - 28, 26), "Следующий матч:", UI.small);
                GUI.Label(new Rect(left.x + 14, y + 26, left.width - 28, 40), me.name + "  —  " + Database.Nations[opp].name, UI.h2c);
                GUI.Label(new Rect(left.x + 14, y + 62, left.width - 28, 24), "★ " + me.rating + "   vs   ★ " + Database.Nations[opp].rating, UI.smallC);
                y += 96;
                if (GUI.Button(new Rect(left.x + 14, y, left.width - 28, 58), "Играть ▶", UI.bigButton)) PlayWorldCupMatch(f);
                y += 66;
                if (GUI.Button(new Rect(left.x + 14, y, left.width - 28, 42), "Симулировать матч", UI.button))
                {
                    WorldCup.SimAndRecord(wc, f);
                    int ug = f.a == wc.user ? f.ga : f.gb, og = f.a == wc.user ? f.gb : f.ga;
                    WorldCup.AdvanceStage(wc);
                    postLines.Clear();
                    CheckWorldCupPrize();
                    SaveSystem.SaveWC(wc);
                    if (toastTimer <= 0f) Toast("Сыграли " + ug + ":" + og + " против " + Database.Nations[opp].name);
                }
                y += 56;
            }
            else
            {
                GUI.Label(new Rect(left.x + 14, y, left.width - 28, 30), "Твоя сборная выбыла.", UI.label);
                y += 40;
                if (GUI.Button(new Rect(left.x + 14, y, left.width - 28, 50), "Досимулировать турнир", UI.button))
                {
                    int guard = 0;
                    while (!wc.finished && guard++ < 20) WorldCup.AdvanceStage(wc);
                    SaveSystem.SaveWC(wc);
                }
                y += 64;
            }

            // своя группа
            int g = WorldCup.GroupOf(wc, wc.user);
            if (g >= 0) DrawGroupTable(new Rect(left.x + 10, y + 6, left.width - 20, 170), wc.groups[g], true);

            // правая часть
            var right = new Rect(450, 70, vw - 480, UI.VH - 160);
            if (GUI.Button(new Rect(right.x, right.y, 160, 36), "Группы", wcShowKnockout ? UI.tab : UI.tabOn)) wcShowKnockout = false;
            if (GUI.Button(new Rect(right.x + 166, right.y, 160, 36), "Плей-офф", wcShowKnockout ? UI.tabOn : UI.tab)) wcShowKnockout = true;
            var area = new Rect(right.x, right.y + 44, right.width, right.height - 44);
            UI.Panel(area);
            if (!wcShowKnockout)
            {
                int cols = area.width > 900 ? 4 : 3;
                float tw = (area.width - 30 - (cols - 1) * 10) / cols, th = 160;
                int rows = Mathf.CeilToInt(12f / cols);
                wcScroll = GUI.BeginScrollView(new Rect(area.x + 5, area.y + 5, area.width - 10, area.height - 10), wcScroll, new Rect(0, 0, area.width - 30, rows * (th + 10)));
                for (int i = 0; i < 12; i++)
                    DrawGroupTable(new Rect((i % cols) * (tw + 10), (i / cols) * (th + 10), tw, th), wc.groups[i], false);
                GUI.EndScrollView();
            }
            else
            {
                if (wc.rounds.Count == 0)
                {
                    GUI.Label(new Rect(area.x + 20, area.y + 20, area.width - 40, 30), "Плей-офф начнётся после группового этапа.", UI.label);
                }
                else
                {
                    int n = wc.rounds.Count;
                    float cw = (area.width - 20) / Mathf.Max(n, 3);
                    float maxH = 0;
                    foreach (var rd in wc.rounds) maxH = Mathf.Max(maxH, 40 + rd.fixtures.Count * 30);
                    wcScroll = GUI.BeginScrollView(new Rect(area.x + 5, area.y + 5, area.width - 10, area.height - 10), wcScroll, new Rect(0, 0, area.width - 30, maxH));
                    for (int ri = 0; ri < n; ri++)
                    {
                        var rd = wc.rounds[ri];
                        GUI.Label(new Rect(ri * cw + 6, 0, cw - 12, 32), rd.name, UI.h2);
                        for (int k = 0; k < rd.fixtures.Count; k++)
                        {
                            var fx = rd.fixtures[k];
                            var r = new Rect(ri * cw + 6, 36 + k * 30, cw - 12, 27);
                            bool mine = fx.Has(wc.user);
                            UI.Box(r, mine ? new Color(0.8f, 0.6f, 0.1f, 0.35f) : new Color(1, 1, 1, 0.06f));
                            string score = fx.played ? fx.ga + ":" + fx.gb + (fx.Pens ? " (" + fx.pa + ":" + fx.pb + ")" : "") : "—";
                            GUI.Label(r, Database.Nations[fx.a].code + "  " + score + "  " + Database.Nations[fx.b].code, UI.smallC);
                        }
                    }
                    GUI.EndScrollView();
                }
            }

            if (Back(vw)) scr = Scr.Main;
        }

        void DrawGroupTable(Rect r, Group g, bool big)
        {
            UI.Box(r, new Color(1, 1, 1, 0.05f));
            GUI.Label(new Rect(r.x + 8, r.y + 2, r.width - 16, 26), "Группа " + g.letter, UI.h2);
            float y = r.y + 30;
            float lh = (r.height - 34) / 5f;
            GUI.Label(new Rect(r.x + 8, y, 120, lh), "Команда", UI.small);
            GUI.Label(new Rect(r.xMax - 150, y, 150, lh), " И   В  Н  П   РМ  О", UI.small);
            y += lh;
            var rows = WorldCup.Sorted(g);
            for (int i = 0; i < rows.Count; i++)
            {
                var t = rows[i];
                var line = new Rect(r.x + 4, y, r.width - 8, lh);
                if (t.nation == wc.user) UI.Box(line, new Color(0.8f, 0.6f, 0.1f, 0.35f));
                else if (i < 2) UI.Box(line, new Color(0.2f, 0.7f, 0.3f, 0.15f));
                string nm = big ? Database.Nations[t.nation].name : Database.Nations[t.nation].code;
                GUI.Label(new Rect(r.x + 8, y, r.width - 160, lh), (i + 1) + ". " + nm, UI.small);
                GUI.Label(new Rect(r.xMax - 150, y, 150, lh), $"{t.p,2}  {t.w,2} {t.d,2} {t.l,2}  {(t.GD > 0 ? "+" : "") + t.GD,3}  {t.Pts,2}", UI.small);
                y += lh;
            }
        }

        // ---------------------------------------------------------------- товарищеский

        void DrawFriendly(float vw)
        {
            Header(vw, "Товарищеский матч");
            var order = Enumerable.Range(0, Database.Nations.Count).OrderByDescending(i => Database.Nations[i].rating).ToList();
            float colW = (vw - 90) / 2;
            GUI.Label(new Rect(30, 66, colW, 30), "Ты играешь за:", UI.h2);
            GUI.Label(new Rect(60 + colW, 66, colW, 30), "Соперник:", UI.h2);
            friendlyA = NationList(new Rect(30, 100, colW, UI.VH - 200), order, friendlyA, ref friendlyScrollA);
            friendlyB = NationList(new Rect(60 + colW, 100, colW, UI.VH - 200), order, friendlyB, ref friendlyScrollB);

            friendlyWatch = GUI.Toggle(new Rect(240, UI.VH - 62, 360, 36), friendlyWatch, "  Только смотреть (ИИ против ИИ)", UI.label);
            GUI.enabled = friendlyA >= 0 && friendlyB >= 0 && friendlyA != friendlyB;
            if (GUI.Button(new Rect(vw - 280, UI.VH - 76, 260, 62), "Начать ▶", UI.bigButton))
            {
                var cfg = new MatchConfig
                {
                    home = TeamSheet.FromNation(friendlyA),
                    away = TeamSheet.FromNation(friendlyB),
                    homeHuman = !friendlyWatch,
                    durationSeconds = Settings.MatchMinutes * 60f,
                };
                StartMatch(cfg, r =>
                {
                    postLines.Clear();
                    ShowPost("Товарищеский матч", r, () => scr = Scr.Friendly);
                });
            }
            GUI.enabled = true;
            if (Back(vw)) scr = Scr.Main;
        }

        int NationList(Rect area, List<int> order, int selected, ref Vector2 scroll)
        {
            UI.Panel(area);
            float rowH = 38;
            var view = new Rect(area.x + 4, area.y + 4, area.width - 8, area.height - 8);
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0, 0, view.width - 20, order.Count * rowH));
            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                var n = Database.Nations[i];
                var r = new Rect(0, k * rowH, view.width - 20, rowH - 4);
                UI.Box(new Rect(r.x, r.y, 8, r.height), n.kit);
                if (GUI.Button(new Rect(r.x + 10, r.y, r.width - 10, r.height), n.name + "   ★ " + n.rating, i == selected ? UI.selButton : UI.rowButton)) selected = i;
            }
            GUI.EndScrollView();
            return selected;
        }

        // ---------------------------------------------------------------- настройки

        void DrawSettings(float vw)
        {
            Header(vw, "Настройки");
            float x = vw / 2 - 300;
            GUI.Label(new Rect(x, 120, 600, 36), "Длительность матча (реальных минут):", UI.h2);
            int[] mins = { 2, 4, 6, 8 };
            for (int i = 0; i < mins.Length; i++)
                if (GUI.Button(new Rect(x + i * 150, 164, 140, 50), mins[i] + " мин", Settings.MatchMinutes == mins[i] ? UI.tabOn : UI.tab)) Settings.MatchMinutes = mins[i];

            GUI.Label(new Rect(x, 250, 600, 36), "Сложность:", UI.h2);
            string[] diffs = { "Легко", "Нормально", "Сложно" };
            for (int i = 0; i < diffs.Length; i++)
                if (GUI.Button(new Rect(x + i * 200, 294, 190, 50), diffs[i], Settings.Difficulty == i ? UI.tabOn : UI.tab)) Settings.Difficulty = i;

            GUI.Label(new Rect(x, 390, 600, 36), "Прогресс:", UI.h2);
            if (GUI.Button(new Rect(x, 434, 400, 50), "Сбросить «Мою команду» и ЧМ", UI.button))
            {
                SaveSystem.ResetAll();
                career = SaveSystem.LoadCareer();
                wc = null;
                marketCache = null;
                Toast("Прогресс сброшен");
            }
            if (Back(vw)) scr = Scr.Main;
        }

        // ---------------------------------------------------------------- итоги матча

        void DrawPost(float vw)
        {
            var r = postResult;
            var panel = new Rect(vw / 2 - 360, 70, 720, 560);
            UI.Panel(panel);
            GUI.Label(new Rect(panel.x, panel.y + 16, panel.width, 40), postTitle, UI.h2c);
            GUI.Label(new Rect(panel.x, panel.y + 70, panel.width / 2 - 70, 60), r.homeName, UI.h2c);
            GUI.Label(new Rect(panel.x + panel.width / 2 + 70, panel.y + 70, panel.width / 2 - 70, 60), r.awayName, UI.h2c);
            GUI.Label(new Rect(panel.x + panel.width / 2 - 80, panel.y + 60, 160, 80), r.homeGoals + " : " + r.awayGoals, UI.title);
            float y = panel.y + 140;
            if (r.pens)
            {
                GUI.Label(new Rect(panel.x, y, panel.width, 30), "Пенальти  " + r.homePens + " : " + r.awayPens, UI.h2c);
                y += 34;
            }
            if (r.forfeit)
            {
                GUI.Label(new Rect(panel.x, y, panel.width, 30), "Техническое поражение", UI.labelC);
                y += 30;
            }
            GUI.Label(new Rect(panel.x, y, panel.width, 26),
                "Удары: " + r.homeShots + " — " + r.awayShots + "     Владение: " + Mathf.RoundToInt(r.homePossession * 100) + "% — " + Mathf.RoundToInt((1 - r.homePossession) * 100) + "%",
                UI.smallC);
            y += 36;
            foreach (var s in r.scorers.Take(8))
            {
                GUI.Label(new Rect(panel.x, y, panel.width, 24), "⚽ " + s, UI.smallC);
                y += 24;
            }
            y += 10;
            foreach (var line in postLines)
            {
                GUI.Label(new Rect(panel.x, y, panel.width, 30), line, UI.h2c);
                y += 32;
            }
            if (GUI.Button(new Rect(panel.x + panel.width / 2 - 150, panel.yMax - 80, 300, 60), "Далее", UI.bigButton))
                postContinue?.Invoke();
        }

        // ================================================================== автотест

        /// <summary>
        /// Запуск билда с -autotest: скриншоты меню, симуляция ЧМ, ускоренный матч ИИ vs ИИ, лог в autotest.log рядом с exe.
        /// </summary>
        IEnumerator AutoTest()
        {
            string dir = System.IO.Path.GetDirectoryName(Application.dataPath);
            void Log(string s) { TestLog.Add(s); Debug.Log("[AUTOTEST] " + s); }
            void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name)); Log("screenshot " + name); }

            yield return new WaitForSeconds(1f);
            Shot("shot_menu.png");
            yield return new WaitForSeconds(0.5f);

            scr = Scr.Dream;
            yield return new WaitForSeconds(0.5f);
            Shot("shot_dream.png");
            yield return new WaitForSeconds(0.5f);

            scr = Scr.Transfers;
            yield return new WaitForSeconds(0.5f);
            Shot("shot_transfers.png");
            yield return new WaitForSeconds(0.5f);

            // покупка игрока
            career.coins = 5000;
            var buy = Database.Market.Where(p => p.pos == Pos.FWD && p.Price <= career.coins).OrderByDescending(p => p.overall).First();
            career.coins -= buy.Price;
            career.owned.Add(buy.id);
            career.AutoLineup();
            Log("bought " + buy.name + " (" + buy.club + ") ovr " + buy.overall + " price " + buy.Price + " -> in lineup: " + career.lineup.Contains(buy.id) + ", rating " + Database.TeamRating(career.LineupPlayers()));

            // реальные составы
            int realCount = Database.Market.Count(p => p.real);
            var top = Database.Nations.OrderByDescending(n => n.rating).Take(5).Select(n => n.code + " " + n.rating);
            Log("real players: " + realCount + " / " + Database.Market.Count + ", top nations: " + string.Join(", ", top) +
                ", ENG XI: " + string.Join(", ", Database.AutoLineup(Database.Nations[Database.FindNation("ENG")].squad).Select(p => p.name)));

            // тренировки и персонал
            career.coins = 20000;
            int before = career.Effective(buy.id).shooting, ovrBefore = career.Effective(buy.id).overall;
            int c1 = career.TrainCost(buy.id);
            for (int i = 0; i < 3; i++) career.Train(buy.id, 1);
            Log($"training: shooting {before}->{career.Effective(buy.id).shooting}, ovr {ovrBefore}->{career.Effective(buy.id).overall}, cost {c1}->{career.TrainCost(buy.id)}, coins {career.coins}");
            bool hired = career.HireStaff(Career.Coach) && career.HireStaff(Career.Scout);
            var ml = career.MatchLineup().First(p => p.id == buy.id);
            Log($"staff hired={hired} coach={career.StaffLevel(Career.Coach)} scout={career.StaffLevel(Career.Scout)} matchShooting={ml.shooting} buyPrice {buy.Price}->{career.BuyPrice(buy)} coins {career.coins}");
            trainSelected = buy.id;
            scr = Scr.Training;
            yield return new WaitForSeconds(0.5f);
            Shot("shot_training.png");
            yield return new WaitForSeconds(0.3f);
            scr = Scr.Staff;
            yield return new WaitForSeconds(0.5f);
            Shot("shot_staff.png");
            yield return new WaitForSeconds(0.3f);
            sellTab = false; marketCache = null; search = "";
            scr = Scr.Transfers;
            yield return new WaitForSeconds(0.5f);
            Shot("shot_transfers_real.png");
            yield return new WaitForSeconds(0.3f);

            // полный ЧМ в симуляции
            int arg = Database.FindNation("ARG");
            wc = WorldCup.New(arg);
            int guard = 0;
            while (!wc.finished && guard++ < 20)
            {
                if (wc.stage == 3)
                {
                    var teams = wc.rounds[0].fixtures.SelectMany(x => new[] { x.a, x.b }).ToList();
                    Log("R32 teams: " + teams.Count + " unique: " + teams.Distinct().Count());
                }
                WorldCup.AdvanceStage(wc);
            }
            Log("WC finished=" + wc.finished + " stages=" + guard + " rounds=" + wc.rounds.Count + " champion=" + Database.Nations[wc.champion].name + " userOut=" + wc.userOut + " prize=" + WorldCup.Prize(wc));
            wc = WorldCup.New(arg);
            WorldCup.AdvanceStage(wc);
            scr = Scr.WCHub;
            yield return new WaitForSeconds(0.5f);
            Shot("shot_wchub.png");
            yield return new WaitForSeconds(0.5f);

            // матч ИИ против ИИ, ускоренно
            bool done = false;
            MatchResult res = null;
            var cfg = new MatchConfig
            {
                home = TeamSheet.FromNation(Database.FindNation("BRA")),
                away = TeamSheet.FromNation(Database.FindNation("FRA")),
                homeHuman = false,
                durationSeconds = 150f,
                title = "Автотест"
            };
            scr = Scr.Match;
            var m = MatchManager.Create(cfg, r => { res = r; done = true; });
            m.autoplay = true;
            yield return new WaitForSeconds(3f);
            Shot("shot_match1.png");
            Time.timeScale = 3f;
            yield return new WaitForSecondsRealtime(8f);
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.2f);
            Shot("shot_match2.png");
            Time.timeScale = 4f;
            float t0 = Time.realtimeSinceStartup;
            while (!done && Time.realtimeSinceStartup - t0 < 120f) yield return null;
            Time.timeScale = 1f;
            if (res != null)
                Log($"match {res.homeName} {res.homeGoals}:{res.awayGoals} {res.awayName} shots {res.homeShots}-{res.awayShots} poss {res.homePossession:F2} scorers: {string.Join(" | ", res.scorers)}");
            else
                Log("match did not finish in time");

            // матч с «человеком» (ввод пустой) — проверяем, что режим управления не ломает игру
            done = false;
            cfg = new MatchConfig { home = TeamSheet.FromCareer(career), away = TeamSheet.FromNation(Database.FindNation("NZL")), homeHuman = true, durationSeconds = 30f, knockout = true };
            m = MatchManager.Create(cfg, r => { res = r; done = true; });
            yield return new WaitForSeconds(1.6f);
            Shot("shot_intro.png");
            yield return new WaitForSeconds(4f);
            Shot("shot_match_human.png");
            m.TestGoal();
            yield return new WaitForSeconds(1.8f);
            Shot("shot_goal.png");
            yield return new WaitForSeconds(3.5f);
            Time.timeScale = 4f;
            t0 = Time.realtimeSinceStartup;
            while (!done && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            Time.timeScale = 1f;
            Log(done ? $"human-mode match {res.homeGoals}:{res.awayGoals} pens={res.pens} {res.homePens}:{res.awayPens} outcome={res.HomeOutcome}" : "human-mode match did not finish");
            scr = Scr.Main;

            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "autotest.log"), TestLog);
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }
    }
}
