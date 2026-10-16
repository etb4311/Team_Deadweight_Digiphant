using System.Collections.Generic;
using UnityEngine;
using DigiPhant;

namespace TeamDeadweight
{
    // A row in the top-right status card. Student scripts implement this instead of drawing their own label.
    public interface IStatusLine
    {
        string StatusTitle { get; }
        string StatusValue { get; }
        bool StatusActive { get; }   // highlighted while something is happening
    }

    // Shibuya-punk look for every DigiPhant panel: ink-black panels, hot red slanted banners,
    // big white type. Original palette and shapes only. Disable to get Unity's default look back.
    // Header font: Anton (SIL Open Font License), Resources/Fonts/Anton-Regular.ttf.
    // Unity resets GUI.skin for every OnGUI, so each panel applies the skin itself via DigiPhantGUI.Apply().
    [DisallowMultipleComponent]
    public class DeadweightTheme : MonoBehaviour
    {
        public static bool Active { get; private set; }
        // True when the status card replaces the scripts' own top-right labels.
        public static bool ShowsStatus => Active && current && current.showStatusCard && current.skin && DigiPhantGUI.Skin == current.skin;

        public Color ink = new Color(.043f, .043f, .051f, .94f);
        public Color red = new Color(.91f, .075f, .17f);
        public Color paper = new Color(.96f, .96f, .94f);
        public Color muted = new Color(.72f, .72f, .75f);
        [Range(1, 1.8f), Tooltip("Scales every themed UI element: text, buttons, banners, panel and status card.")]
        public float uiScale = 1.3f;
        [Range(11, 22), Tooltip("Body text size before UI Scale is applied.")] public int bodySize = 15;
        [Range(0, 4), Tooltip("Black border around light text, in pixels. 0 = off.")]
        public int outlineWidth = 2;
        public Color outline = Color.black;
        public bool showStatusCard = true;
        [TextArea] public string keyHints = "E eat · F drink · D drift · arrows drive test · C view · M map";

        static DeadweightTheme current;
        GUISkin skin;
        Font anton;
        GUIStyle title, subtitle, small, good, bad, banner, rowTitle, rowValue, hint;
        Texture2D inkTex, redTex, paperTex, zigzag;
        readonly Dictionary<string, GUIStyle> named = new Dictionary<string, GUIStyle>();
        readonly List<IStatusLine> lines = new List<IStatusLine>();
        readonly List<Texture2D> made = new List<Texture2D>(); // destroyed in OnDisable
        float nextRefresh;
        bool cardTopLeft;
        float builtScale;
        int builtBody;

        int S(float px) => Mathf.RoundToInt(px * builtScale); // scaled pixel size

        // Domain reload is off in this project, so statics survive Play sessions: reset them at start.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { current = null; Active = false; ClearHooks(); }

        static void ClearHooks()
        {
            DigiPhantGUI.Skin = null;
            DigiPhantGUI.SectionHeader = null;
            DigiPhantGUI.PanelBackground = null;
            DigiPhantGUI.StyleFor = null;
            DigiPhantGUI.TextDrawer = null;
            DigiPhantGUI.Scale = 1;
        }

        void OnEnable()
        {
            current = this;
            Active = true;
            anton = Resources.Load<Font>("Fonts/Anton-Regular");
        }

        void OnDisable()
        {
            if (current == this)
            {
                current = null;
                Active = false;
                ClearHooks();
            }
            DestroySkin();
        }

        void DestroySkin()
        {
            if (skin) Destroy(skin);
            foreach (var t in made) if (t) Destroy(t);
            made.Clear();
            skin = null;
        }

        void OnGUI()
        {
            if (!skin) { Build(); return; } // GUI.skin can only be read inside OnGUI; goes live next frame
            if (DigiPhantGUI.Skin != skin) return;
            GUI.skin = skin;
            if (showStatusCard) DrawStatusCard();
        }

        // ---------- skin ----------

        void Build()
        {
            builtScale = uiScale;
            builtBody = bodySize;
            inkTex = Solid(ink);
            redTex = Solid(red);
            paperTex = Solid(paper);
            zigzag = Zigzag();
            skin = Instantiate(GUI.skin);
            Font header = anton ? anton : skin.font;

            var label = skin.label;
            label.fontSize = S(bodySize);
            label.wordWrap = true;
            label.richText = true;
            label.normal.textColor = paper;
            label.padding = new RectOffset(S(2), S(2), S(3), S(3));

            var box = skin.box;
            Bg(box.normal, inkTex);
            box.padding = new RectOffset(S(12), S(12), S(10), S(10));
            box.border = new RectOffset(0, 0, 0, 0);

            var button = skin.button;
            button.font = header;
            button.fontSize = S(bodySize + 2);
            button.fixedHeight = S(34);
            button.margin = new RectOffset(S(3), S(3), S(4), S(4));
            button.padding = new RectOffset(S(10), S(10), S(4), S(4));
            button.border = new RectOffset(9, 9, 9, 9);
            button.alignment = TextAnchor.MiddleCenter;
            // Dark = available, dark red = hover, white = pressed. Solid red is kept for "selected" only.
            var dark = new Color(.08f, .08f, .09f);
            var plain = Slanted(dark, red);
            var warm = Slanted(new Color(.36f, .03f, .07f), red);
            var white = Slanted(paper, paper);
            var picked = Selected();
            SetState(button.normal, plain, paper);
            SetState(button.hover, warm, paper);
            SetState(button.focused, plain, paper);
            SetState(button.active, white, Color.black);
            SetState(button.onNormal, picked, paper);
            SetState(button.onHover, picked, paper);
            SetState(button.onFocused, picked, paper);
            SetState(button.onActive, white, Color.black);

            // Toolbar segments: unselected options are quieter (grey text), the chosen one is solid red
            // with a paper drop shadow and underline bar.
            var toolbar = new GUIStyle(button) { name = "deadweight toolbar" };
            var dimText = new Color(.78f, .78f, .8f);
            SetState(toolbar.normal, plain, dimText);
            SetState(toolbar.focused, plain, dimText);
            SetState(toolbar.hover, warm, paper);
            SetState(toolbar.active, white, Color.black);
            SetState(toolbar.onNormal, picked, paper);
            SetState(toolbar.onHover, picked, paper);
            SetState(toolbar.onFocused, picked, paper);
            SetState(toolbar.onActive, white, Color.black);

            var toggle = skin.toggle;
            toggle.fontSize = S(bodySize);
            toggle.normal.textColor = toggle.hover.textColor = paper;
            toggle.onNormal.textColor = toggle.onHover.textColor = paper;
            // Box on the left, transparent strip on the right that stretches under the label.
            Texture2D off = Checkbox(false), on = Checkbox(true);
            foreach (var st in new[] { toggle.normal, toggle.hover, toggle.focused }) Bg(st, off);
            foreach (var st in new[] { toggle.active, toggle.onNormal, toggle.onHover, toggle.onFocused, toggle.onActive }) Bg(st, on);
            int box16 = S(16);
            toggle.border = new RectOffset(box16, 1, box16, 0);
            toggle.overflow = new RectOffset(0, 0, 0, 0);
            toggle.padding = new RectOffset(box16 + S(8), 0, 0, S(2));
            toggle.margin = new RectOffset(S(4), S(4), S(6), S(6));

            var slider = skin.horizontalSlider;
            // Tall control rect (easy to grab) with a thin drawn track: the thumb takes the slider's height.
            Bg(slider.normal, Solid(new Color(.3f, .3f, .32f)));
            slider.fixedHeight = S(20);
            slider.overflow = new RectOffset(0, 0, -(S(20) - S(4)) / 2, -(S(20) - S(4)) / 2);
            slider.border = new RectOffset(0, 0, 0, 0);
            slider.padding = new RectOffset(0, 0, 0, 0);
            slider.margin = new RectOffset(S(4), S(4), S(4), S(4));
            var thumb = skin.horizontalSliderThumb;
            Bg(thumb.normal, Slanted(paper, paper, 14, 20, 4));
            Texture2D hotThumb = Slanted(red, red, 14, 20, 4);
            Bg(thumb.hover, hotThumb);
            Bg(thumb.active, hotThumb);
            Bg(thumb.focused, hotThumb);
            thumb.fixedWidth = S(14);
            thumb.fixedHeight = S(20);
            thumb.border = new RectOffset(0, 0, 0, 0);

            Bg(skin.verticalScrollbar.normal, Solid(new Color(.15f, .15f, .16f)));
            skin.verticalScrollbar.fixedWidth = S(6);
            Bg(skin.verticalScrollbarThumb.normal, redTex);
            skin.verticalScrollbarThumb.fixedWidth = S(6);
            skin.scrollView.normal.background = null;

            title = new GUIStyle(label) { font = header, fontSize = S(40), wordWrap = false, padding = new RectOffset(S(4), S(4), 0, 0) };
            subtitle = new GUIStyle(label) { font = header, fontSize = S(15) };
            subtitle.normal.textColor = red;
            small = new GUIStyle(label) { fontSize = S(Mathf.Max(11, bodySize - 2)) };
            small.normal.textColor = muted;
            good = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            bad = new GUIStyle(good);
            bad.normal.textColor = new Color(1, .33f, .38f);
            banner = new GUIStyle(label) { font = header, fontSize = S(19), wordWrap = false, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0) };
            rowTitle = new GUIStyle(label) { font = header, fontSize = S(15), wordWrap = false, alignment = TextAnchor.MiddleLeft };
            rowTitle.normal.textColor = muted;
            rowValue = new GUIStyle(label) { fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            hint = new GUIStyle(small) { fontSize = S(12) };
            var area = new GUIStyle(box);  // the panel background is painted separately, so the area stays clear
            Bg(area.normal, null);

            named["title"] = title;
            named["subtitle"] = subtitle;
            named["small"] = small;
            named["good"] = good;
            named["bad"] = bad;
            named["area"] = area;
            named["toolbar"] = toolbar;

        }

        // Publish outside OnGUI: switching hooks between a frame's Layout and Repaint events
        // would change the panels' layout mid-frame ("Mismatched LayoutGroup.repaint").
        void LateUpdate()
        {
            // Inspector size changes: drop the skin here (outside OnGUI); OnGUI rebuilds it, and the next frame publishes it.
            if (skin && (builtScale != uiScale || builtBody != bodySize))
            {
                if (current == this) ClearHooks();
                DestroySkin();
                return;
            }
            if (!skin || DigiPhantGUI.Skin == skin) return;
            DigiPhantGUI.Scale = builtScale;
            DigiPhantGUI.Skin = skin;
            DigiPhantGUI.SectionHeader = Section;
            DigiPhantGUI.PanelBackground = Panel;
            DigiPhantGUI.StyleFor = n => n != null && named.TryGetValue(n, out var s) ? s : null;
            DigiPhantGUI.TextDrawer = Text;
        }

        // ---------- outlined text ----------

        static readonly Vector2[] Directions =
            { new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1), new Vector2(-1, 0),
              new Vector2(1, 0), new Vector2(-1, 1), new Vector2(0, 1), new Vector2(1, 1) };
        static readonly Texture2D[] NoScaled = new Texture2D[0];

        // Light text gets a black border: the text is drawn in black at offsets all round, then on top.
        // Dark text (e.g. on the white pressed button) is drawn plain.
        void Text(Rect r, GUIContent content, GUIStyle style, Color color)
        {
            var state = style.normal;
            Color savedColor = state.textColor;
            Texture2D savedBackground = state.background;
            Texture2D[] savedScaled = state.scaledBackgrounds;
            state.background = null; // text only; the control has already drawn its background
            state.scaledBackgrounds = NoScaled;
            if (outlineWidth > 0 && color.r * .2126f + color.g * .7152f + color.b * .0722f > .35f)
            {
                state.textColor = new Color(outline.r, outline.g, outline.b, outline.a * color.a);
                for (int k = 1; k <= outlineWidth; k++)
                    foreach (var d in Directions)
                        style.Draw(new Rect(r.x + d.x * k, r.y + d.y * k, r.width, r.height), content, false, false, false, false);
            }
            state.textColor = color;
            style.Draw(r, content, false, false, false, false);
            state.textColor = savedColor;
            state.background = savedBackground;
            state.scaledBackgrounds = savedScaled;
        }

        void Text(Rect r, string text, GUIStyle style)
        {
            if (Event.current.type == EventType.Repaint) Text(r, new GUIContent(text), style, style.normal.textColor);
        }

        // ---------- drawing ----------

        void Section(string text)
        {
            GUILayout.Space(S(10));
            Rect r = GUILayoutUtility.GetRect(10, S(34), GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            text = text.ToUpperInvariant();
            float w = banner.CalcSize(new GUIContent(text)).x + S(30), h = S(24);
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(-2.5f, new Vector2(r.x + w / 2, r.y + S(14))); // pivot on the banner itself
            GUI.DrawTexture(new Rect(r.x + S(3), r.y + S(5), w, h), paperTex); // offset paper shadow
            GUI.DrawTexture(new Rect(r.x, r.y + S(2), w, h), redTex);
            Text(new Rect(r.x + S(12), r.y + S(2), w, h), text, banner);
            GUI.matrix = saved;
            GUI.DrawTexture(new Rect(r.x + w + S(6), r.yMax - S(6), Mathf.Max(0, r.width - w - S(6)), S(2)), redTex);
        }

        void Panel(Rect r)
        {
            GUI.DrawTexture(r, inkTex, ScaleMode.StretchToFill, false); // opaque: nothing else clears this strip
            // Slashed red shard behind the title, and a jagged stripe down the panel edge.
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(-7, new Vector2(r.x + S(80), r.y + S(40)));
            GUI.DrawTexture(new Rect(r.x - S(40), r.y + S(14), r.width * .78f, S(46)), redTex);
            GUI.matrix = saved;
            float stripe = S(12);
            GUI.DrawTextureWithTexCoords(new Rect(r.xMax - stripe, r.y, stripe, r.height), zigzag, new Rect(0, 0, 1, r.height / (2 * stripe)));
        }

        void DrawStatusCard()
        {
            // Refresh on a timer, and only on Layout so Layout and Repaint see the same rows.
            if (Event.current.type == EventType.Layout && (Time.unscaledTime >= nextRefresh || nextRefresh - Time.unscaledTime > 1)) // clock restarts each Play
            {
                nextRefresh = Time.unscaledTime + .5f;
                lines.Clear();
                cardTopLeft = false;
                foreach (var mb in FindObjectsByType<MonoBehaviour>())
                {
                    if (mb is IStatusLine line && mb.isActiveAndEnabled) lines.Add(line);
                    // With the control panel hidden, the camera preview box takes the top-right corner.
                    if (mb is DigiPhantController c && c.isActiveAndEnabled && !c.showControls &&
                        c.TryGetComponent(out DigiPhantCameraPreview p) && p.isActiveAndEnabled && p.showPreview) cardTopLeft = true;
                }
                lines.Sort((a, b) => string.CompareOrdinal(a.StatusTitle, b.StatusTitle));
            }
            if (lines.Count == 0) return;
            float width = S(320), row = S(30);
            var hintContent = new GUIContent(keyHints);
            float inner = width - S(24), top = S(50);
            float hintHeight = string.IsNullOrEmpty(keyHints) ? 0 : hint.CalcHeight(hintContent, inner);
            float height = top + lines.Count * row + hintHeight + S(8);
            var card = new Rect(cardTopLeft ? 14 : Screen.width - width - 14, 12, width, height);
            GUI.DrawTexture(card, inkTex);
            GUI.DrawTexture(new Rect(card.x, card.y, S(6), card.height), redTex);
            GUILayout.BeginArea(new Rect(card.x + S(14), card.y + S(2), inner, top - S(2)));
            Section("Status");
            GUILayout.EndArea();
            float y = card.y + top;
            float titleWidth = S(110);
            foreach (var line in lines)
            {
                var r = new Rect(card.x + S(14), y, inner, row - S(4));
                if (line.StatusActive) GUI.DrawTexture(r, redTex);
                rowTitle.normal.textColor = line.StatusActive ? paper : muted;
                Text(new Rect(r.x + S(8), r.y, titleWidth, r.height), line.StatusTitle.ToUpperInvariant(), rowTitle);
                Text(new Rect(r.x + S(8) + titleWidth, r.y, r.width - titleWidth - S(12), r.height), line.StatusValue, rowValue);
                y += row;
            }
            if (hintHeight > 0) Text(new Rect(card.x + S(14), y, inner, hintHeight), keyHints, hint);
        }

        // Red frame with a tag, used around the minimap.
        public static void Frame(Rect r, string tag)
        {
            var t = current;
            if (!t || !t.skin || Event.current.type != EventType.Repaint) return;
            float b = t.S(3);
            GUI.DrawTexture(new Rect(r.x - b, r.y - b, r.width + 2 * b, b), t.redTex);
            GUI.DrawTexture(new Rect(r.x - b, r.yMax, r.width + 2 * b, b), t.redTex);
            GUI.DrawTexture(new Rect(r.x - b, r.y, b, r.height), t.redTex);
            GUI.DrawTexture(new Rect(r.xMax, r.y, b, r.height), t.redTex);
            float w = t.banner.CalcSize(new GUIContent(tag)).x + t.S(20);
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(-4, new Vector2(r.x, r.y));
            GUI.DrawTexture(new Rect(r.x - t.S(8), r.y - t.S(14), w, t.S(24)), t.redTex);
            t.Text(new Rect(r.x + t.S(2), r.y - t.S(14), w, t.S(24)), tag, t.banner);
            GUI.matrix = saved;
        }

        // ---------- textures ----------

        static void SetState(GUIStyleState state, Texture2D background, Color text)
        {
            Bg(state, background);
            state.textColor = text;
        }

        // Retina screens prefer scaledBackgrounds over background, so clear them whenever the background is set.
        static void Bg(GUIStyleState state, Texture2D background)
        {
            state.background = background;
            state.scaledBackgrounds = new Texture2D[0];
        }

        Texture2D Track(Texture2D t) { made.Add(t); return t; }

        Texture2D Solid(Color c)
        {
            var t = Track(new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave });
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        // Rectangle with the top-left and bottom-right corners cut on a diagonal, plus an edge colour.
        Texture2D Slanted(Color fill, Color edge, int w = 32, int h = 32, int cut = 7)
        {
            var t = Track(new Texture2D(w, h) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int topLeft = x + (h - 1 - y), bottomRight = (w - 1 - x) + y;
                    bool inside = topLeft >= cut && bottomRight >= cut;
                    bool border = x < 2 || x > w - 3 || y < 2 || y > h - 3 || topLeft < cut + 2 || bottomRight < cut + 2;
                    t.SetPixel(x, y, !inside ? clear : border ? edge : fill);
                }
            t.Apply();
            return t;
        }

        // Selected: red slanted plate with a paper shadow offset down-right and a paper underline bar.
        Texture2D Selected()
        {
            const int w = 36, h = 36, cut = 7, shift = 3, size = 32;
            var t = Track(new Texture2D(w, h) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
            bool Shape(int x, int y, int x0, int y0)
            {
                int lx = x - x0, ly = y - y0;
                if (lx < 0 || ly < 0 || lx >= size || ly >= size) return false;
                return lx + (size - 1 - ly) >= cut && (size - 1 - lx) + ly >= cut;
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = new Color(0, 0, 0, 0);
                    if (Shape(x, y, shift, 0)) c = paper;                              // shadow, down-right
                    if (Shape(x, y, 0, shift)) c = y >= shift + 2 && y <= shift + 4 && x > 6 && x < size - 4 ? paper : red; // plate + underline
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        Texture2D Checkbox(bool on)
        {
            int s = S(16);
            var t = Track(new Texture2D(s + 2, s) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point });
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s + 2; x++)
                {
                    bool edge = x < 2 || y < 2 || x > s - 3 || y > s - 3;
                    bool core = on && x > 3 && y > 3 && x < s - 4 && y < s - 4;
                    t.SetPixel(x, y, x >= s ? new Color(0, 0, 0, 0) : edge ? red : core ? paper : new Color(.08f, .08f, .09f));
                }
            t.Apply();
            return t;
        }

        Texture2D Zigzag()
        {
            const int w = 12, h = 24;
            var t = Track(new Texture2D(w, h) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear });
            for (int y = 0; y < h; y++)
            {
                float reach = 3 + 9 * Mathf.Abs(y / (h / 2f) - 1); // triangle wave
                for (int x = 0; x < w; x++) t.SetPixel(x, y, x >= w - reach ? red : new Color(0, 0, 0, 0));
            }
            t.Apply();
            return t;
        }
    }
}
