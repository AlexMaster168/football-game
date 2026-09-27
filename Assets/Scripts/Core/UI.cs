using UnityEngine;

namespace FD
{
    /// <summary>Стили IMGUI. Вся разметка — в виртуальных координатах высотой 720.</summary>
    public static class UI
    {
        public const float VH = 720f;
        public static float Scale => Screen.height / VH;
        public static float VW => Screen.width / Scale;

        public static GUIStyle title, subtitle, h1, h1c, h2, h2c, label, labelC, labelR, small, smallC, hint;
        public static GUIStyle button, bigButton, selButton, rowButton, tab, tabOn, field;
        public static GUIStyle scoreBig, scoreTeam, scoreTime, banner, nameTag, arrow;
        public static Texture2D white;
        static bool inited;

        public static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        public static readonly Color Accent = new Color(0.35f, 0.75f, 1f);

        public static void Begin()
        {
            Init();
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
        }

        static void Init()
        {
            if (inited && white != null) return;
            inited = true;
            white = U.Tex(Color.white);

            title = Label(66, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            subtitle = Label(22, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
            h1 = Label(34, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            h1c = Label(34, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            h2 = Label(22, FontStyle.Bold, TextAnchor.MiddleLeft, Gold);
            h2c = Label(22, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            label = Label(18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
            labelC = Label(18, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white);
            labelR = Label(18, FontStyle.Normal, TextAnchor.MiddleRight, Color.white);
            small = Label(15, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.85f, 0.88f, 0.95f));
            smallC = Label(15, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.85f, 0.88f, 0.95f));
            hint = Label(15, FontStyle.Normal, TextAnchor.LowerLeft, new Color(1f, 1f, 1f, 0.85f));
            hint.wordWrap = true;

            scoreBig = Label(30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            scoreTeam = Label(24, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            scoreTime = Label(22, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            banner = Label(58, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
            nameTag = Label(15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            arrow = Label(18, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.1f));

            button = Button(20, "#1E3A5F", "#2F5C8F", "#14304F", Color.white);
            bigButton = Button(26, "#1E3A5F", "#2F5C8F", "#14304F", Color.white);
            selButton = Button(18, "#C9A227", "#E0B830", "#A88720", Color.black);
            rowButton = Button(16, "#1A2B45", "#27466E", "#14304F", Color.white);
            rowButton.alignment = TextAnchor.MiddleCenter;
            tab = Button(18, "#15243A", "#23406A", "#14304F", new Color(0.8f, 0.85f, 0.95f));
            tabOn = Button(18, "#2F6FB5", "#3A80CC", "#2F6FB5", Color.white);

            field = new GUIStyle(GUI.skin.textField) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            field.normal.background = U.Tex(new Color(0.1f, 0.14f, 0.22f));
            field.focused.background = U.Tex(new Color(0.14f, 0.2f, 0.32f));
            field.normal.textColor = field.focused.textColor = Color.white;
            field.padding = new RectOffset(10, 10, 4, 4);
        }

        static GUIStyle Label(int size, FontStyle fs, TextAnchor a, Color c)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a, wordWrap = false, clipping = TextClipping.Clip };
            s.normal.textColor = c;
            return s;
        }

        static GUIStyle Button(int size, string n, string h, string a, Color text)
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            s.normal.background = U.Tex(U.Hex(n));
            s.hover.background = U.Tex(U.Hex(h));
            s.active.background = U.Tex(U.Hex(a));
            s.focused.background = s.normal.background;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = text;
            s.border = new RectOffset(0, 0, 0, 0);
            return s;
        }

        public static void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        public static void Panel(Rect r) => Box(r, new Color(0.04f, 0.07f, 0.14f, 0.88f));
    }
}
