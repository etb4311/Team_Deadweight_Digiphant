using System;
using UnityEngine;

namespace DigiPhant
{
    // Optional look for every DigiPhant panel. A theme script fills these in; when nothing does,
    // the panels draw with Unity's default skin exactly as before.
    public static class DigiPhantGUI
    {
        public static GUISkin Skin;
        public static Action<string> SectionHeader;      // draws a section title inside a GUILayout area
        public static Action<Rect> PanelBackground;      // draws behind the left control panel
        public static Func<string, GUIStyle> StyleFor;   // named extra styles: "title", "subtitle", "small", "good", "bad"

        public static float Scale = 1;                   // UI size multiplier set by the theme

        public static void Apply() { if (Skin) GUI.skin = Skin; }

        // Width of the left control panel's content area.
        public static float PanelWidth() => Mathf.Min(380 * Scale, Screen.width * Mathf.Min(.45f, .34f * Scale));

        public static void Section(string title)
        {
            if (SectionHeader != null) SectionHeader(title);
            else Label(title.ToUpperInvariant());
        }

        public static GUIStyle Style(string name, GUIStyle fallback) => StyleFor?.Invoke(name) ?? fallback;

        // Optional text renderer (e.g. outlined text). Called on Repaint with the rect, content, style and
        // the text colour for the control's current state. Null = the controls below draw normally.
        public static Action<Rect, GUIContent, GUIStyle, Color> TextDrawer;

        // Drop-in replacements for GUILayout / GUI text controls. Each reserves exactly one layout
        // entry, like the original, then draws its text through TextDrawer.
        public static void Label(string text, params GUILayoutOption[] options) => Label(text, GUI.skin.label, options);
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            if (TextDrawer == null) { GUILayout.Label(text, style, options); return; }
            var content = new GUIContent(text);
            Rect r = GUILayoutUtility.GetRect(content, style, options);
            DrawText(r, content, style, style.normal.textColor);
        }
        public static void Label(Rect r, string text, GUIStyle style = null)
        {
            style ??= GUI.skin.label;
            if (TextDrawer == null) { GUI.Label(r, text, style); return; }
            DrawText(r, new GUIContent(text), style, style.normal.textColor);
        }

        public static bool Button(string text, params GUILayoutOption[] options)
        {
            if (TextDrawer == null) return GUILayout.Button(text, options);
            var content = new GUIContent(text);
            return Button(GUILayoutUtility.GetRect(content, GUI.skin.button, options), content);
        }
        public static bool Button(Rect r, string text) =>
            TextDrawer == null ? GUI.Button(r, text) : Button(r, new GUIContent(text));
        static bool Button(Rect r, GUIContent content)
        {
            var style = GUI.skin.button;
            bool clicked = GUI.Button(r, GUIContent.none, style);
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            bool pressed = hover && GUIUtility.hotControl != 0; // a control is held down under the mouse
            DrawText(r, content, style, pressed ? style.active.textColor : hover ? style.hover.textColor : style.normal.textColor);
            return clicked;
        }

        public static int Toolbar(int selected, string[] texts)
        {
            if (TextDrawer == null) return GUILayout.Toolbar(selected, texts);
            // Drawn as separate segments with the theme's "toolbar" style: GUI.Toolbar would switch to the
            // skin's built-in left/mid/right button styles and ignore the theme.
            var style = Style("toolbar", GUI.skin.button);
            string longest = "";
            foreach (var t in texts) if (t.Length > longest.Length) longest = t;
            Rect r = GUILayoutUtility.GetRect(new GUIContent(longest), style, GUILayout.ExpandWidth(true));
            const float spacing = 6;
            float w = (r.width - spacing * (texts.Length - 1)) / texts.Length;
            int result = selected;
            for (int i = 0; i < texts.Length; i++)
            {
                var segment = new Rect(r.x + i * (w + spacing), r.y, w, r.height);
                bool on = i == selected;
                if (GUI.Toggle(segment, on, GUIContent.none, style) && !on) result = i;
                bool hover = GUI.enabled && segment.Contains(Event.current.mousePosition);
                bool pressed = hover && GUIUtility.hotControl != 0;
                DrawText(segment, new GUIContent(texts[i]), style, pressed ? style.active.textColor
                    : on ? style.onNormal.textColor : hover ? style.hover.textColor : style.normal.textColor);
            }
            return result;
        }

        public static bool Toggle(bool value, string text, params GUILayoutOption[] options)
        {
            if (TextDrawer == null) return GUILayout.Toggle(value, text, options);
            var style = GUI.skin.toggle;
            var content = new GUIContent(text);
            Rect r = GUILayoutUtility.GetRect(content, style, options);
            bool result = GUI.Toggle(r, value, GUIContent.none, style);
            DrawText(r, content, style, result ? style.onNormal.textColor : style.normal.textColor);
            return result;
        }

        static void DrawText(Rect r, GUIContent content, GUIStyle style, Color color)
        {
            if (Event.current.type == EventType.Repaint) TextDrawer(r, content, style, color);
        }
    }
}
