using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using DigiPhant;
using TeamDeadweight;

namespace DeadweightTanks
{
    // Rounds and the match HUD.
    //   Practice (lobby): free play, knocked-out elephants come back after practiceReviveSeconds.
    //   Space / "Start match": everyone resets to their spawn, 3-2-1 countdown (frozen), then a round with no revives.
    //   Last elephant standing wins the round (all down together = draw). First to winsNeeded wins the match.
    //   Space after a match starts a new one; Backspace returns to practice at any time.
    // Players taking part follow the panel's group size (2 or 3).
    [DefaultExecutionOrder(40)] // before the elephants (50) each frame
    public class TankMatch : MonoBehaviour, IStatusLine
    {
        public enum Phase { Practice, Countdown, Playing, RoundOver, MatchOver }

        public DigiPhantController controller;
        public TankElephant[] elephants;
        [Min(1)] public int winsNeeded = 2;
        public float countdownSeconds = 3;
        public float roundOverSeconds = 3;
        public float practiceReviveSeconds = 5;
        [Tooltip("Seconds to wait once one (or no) elephant is left, so near-simultaneous knockouts count as a draw.")]
        public float settleSeconds = .25f;
        [Tooltip("Optional: the arena camera, so it leaves room for the player cards.")]
        public TankArenaCamera arenaCamera;
        [Tooltip("Optional: the camera preview, to tell where the theme's status card sits.")]
        public DigiPhantCameraPreview preview;

        public Phase State { get; private set; } = Phase.Practice;
        public int Round { get; private set; }
        public string Banner { get; private set; } = "";
        public int Wins(int player) => player >= 1 && player <= 3 ? wins[player - 1] : 0;
        public IEnumerable<TankElephant> Playing => elephants.Where(e => e && e.gameObject.activeSelf);

        readonly int[] wins = new int[3];
        float phaseEnd, settleSince = -1;
        int lastCount = -1;

        public string StatusTitle => "Match";
        public string StatusValue => State switch
        {
            Phase.Practice => "Practice · Space to start",
            Phase.Countdown => $"Round {Round} starting",
            Phase.Playing => $"Round {Round} · first to {winsNeeded}",
            Phase.RoundOver => "Round over",
            _ => "Match over · Space: again",
        };
        public bool StatusActive => State == Phase.Playing;

        void Update() => Tick(Time.realtimeSinceStartup);

        // One step of the match. Public so the editor validation can drive it.
        public void Tick(float now)
        {
            if (elephants == null || elephants.Length == 0) elephants = FindObjectsByType<TankElephant>().OrderBy(e => e.player).ToArray();
            var k = Keyboard.current;
            bool start = k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame);
            if (k != null && k.backspaceKey.wasPressedThisFrame && State != Phase.Practice) { ToPractice(); return; }

            switch (State)
            {
                case Phase.Practice:
                    UpdateParticipants();
                    foreach (var e in Playing) { e.Frozen = false; e.reviveSeconds = practiceReviveSeconds; }
                    Banner = "PRACTICE";
                    if (start) StartMatch(now);
                    break;
                case Phase.Countdown:
                    int left = Mathf.CeilToInt(phaseEnd - now);
                    Banner = left > 0 ? left.ToString() : "GO!";
                    if (now >= phaseEnd)
                    {
                        State = Phase.Playing;
                        foreach (var e in Playing) e.Frozen = false;
                        Banner = "GO!";
                        phaseEnd = now + .8f; // show "GO!" briefly
                    }
                    break;
                case Phase.Playing:
                    if (now >= phaseEnd) Banner = "";
                    var standing = Playing.Where(e => !e.KnockedOut).ToList();
                    if (standing.Count > 1) settleSince = -1;
                    else if (settleSince < 0) settleSince = now;
                    else if (now - settleSince >= settleSeconds) EndRound(now, standing.FirstOrDefault());
                    break;
                case Phase.RoundOver:
                    if (now >= phaseEnd) StartRound(now);
                    break;
                case Phase.MatchOver:
                    if (start) StartMatch(now);
                    break;
            }
        }

        public void StartMatch(float now)
        {
            UpdateParticipants();
            System.Array.Clear(wins, 0, wins.Length);
            Round = 0;
            StartRound(now);
        }

        void StartRound(float now)
        {
            Round++;
            ClearShots();
            foreach (var e in Playing)
            {
                e.ResetForRound();
                e.reviveSeconds = 0; // no comebacks during a round
                e.Frozen = true;
            }
            State = Phase.Countdown;
            phaseEnd = now + countdownSeconds;
            settleSince = -1;
            Banner = Mathf.CeilToInt(countdownSeconds).ToString();
        }

        void EndRound(float now, TankElephant winner)
        {
            // Whistle: nothing in the air or about to leave a trunk can change the result.
            ClearShots();
            foreach (var e in Playing) { e.Frozen = true; e.CancelActions(); }
            settleSince = -1;
            if (winner) wins[winner.player - 1]++;
            bool matchWon = winner && wins[winner.player - 1] >= winsNeeded;
            Banner = !winner ? "DRAW!" : matchWon ? $"P{winner.player} WINS THE MATCH!" : $"P{winner.player} WINS ROUND {Round}";
            State = matchWon ? Phase.MatchOver : Phase.RoundOver;
            phaseEnd = now + roundOverSeconds;
        }

        public void ToPractice()
        {
            ClearShots();
            System.Array.Clear(wins, 0, wins.Length);
            Round = 0;
            foreach (var e in Playing) { e.ResetForRound(); e.Frozen = false; e.reviveSeconds = practiceReviveSeconds; }
            State = Phase.Practice;
        }

        // Group size 2 removes P3's elephant; only changed between matches.
        void UpdateParticipants()
        {
            int count = controller ? Mathf.Clamp(controller.performerCount, 2, 3) : 3;
            if (count == lastCount) return;
            lastCount = count;
            foreach (var e in elephants) if (e) e.gameObject.SetActive(e.player <= count);
        }

        static void ClearShots()
        {
            foreach (var shot in FindObjectsByType<TankWaterShot>())
                if (Application.isPlaying) Destroy(shot.gameObject); else DestroyImmediate(shot.gameObject);
        }

        // ---------- HUD ----------

        GUIStyle bannerStyle, cardTitle, cardText, hintStyle;
        GUISkin styledSkin;
        float styledScale;

        void OnGUI()
        {
            DigiPhantGUI.Apply();
            var cam = Camera.main;
            float x0 = cam ? cam.rect.x * Screen.width : 0, width = Screen.width - x0;
            float scale = Mathf.Max(1, DigiPhantGUI.Scale);
            if (bannerStyle == null || styledSkin != DigiPhantGUI.Skin || styledScale != scale) // theme or size changed
            {
                styledSkin = DigiPhantGUI.Skin;
                styledScale = scale;
                var title = DigiPhantGUI.Style("title", null);
                bannerStyle = new GUIStyle(title ?? GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                bannerStyle.fontSize = Mathf.RoundToInt(52 * scale);
                if (title == null) bannerStyle.fontStyle = FontStyle.Bold;
                var sub = DigiPhantGUI.Style("subtitle", null);
                cardTitle = new GUIStyle(sub ?? GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * scale), alignment = TextAnchor.MiddleLeft, wordWrap = false };
                cardText = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(14 * scale), wordWrap = false, clipping = TextClipping.Clip };
                hintStyle = new GUIStyle(cardText) { alignment = TextAnchor.MiddleCenter };
            }

            // Banner across the top of the arena view, left of the theme's status card (top right).
            string banner = Banner;
            // The status card is top right, or top left when the control panel is hidden and the camera preview shows.
            float cardSpace = DeadweightTheme.ShowsStatus ? 340 * scale : 0;
            bool cardLeft = controller && !controller.showControls && preview && preview.showPreview;
            float bannerWidth = Mathf.Max(200, width - cardSpace - 36), bannerX = x0 + 12 + (cardLeft ? cardSpace : 0);
            if (banner.Length > 0)
            {
                float h = 80 * scale;
                var r = new Rect(bannerX, 12, bannerWidth, h);
                Shade(r, .55f);
                bannerStyle.normal.textColor = State == Phase.Practice ? new Color(.85f, .85f, .88f) : Color.white;
                // Shrink long banners ("P1 WINS THE MATCH!") to fit narrow views.
                bannerStyle.fontSize = Mathf.RoundToInt(52 * scale);
                while (bannerStyle.fontSize > 18 && bannerStyle.CalcSize(new GUIContent(banner)).x > r.width - 20) bannerStyle.fontSize -= 2;
                DigiPhantGUI.Label(r, banner, bannerStyle);
                string hint = State == Phase.Practice ? "Space: start match" : State == Phase.MatchOver ? "Space: new match · Backspace: practice" : "";
                if (hint.Length > 0) DigiPhantGUI.Label(new Rect(r.x, r.yMax, r.width, 26 * scale), hint, hintStyle);
            }
            if (State == Phase.Practice || State == Phase.MatchOver)
            {
                var b = new Rect(bannerX + bannerWidth / 2 - 90 * scale, 12 + 80 * scale + 30 * scale, 180 * scale, 40 * scale);
                if (DigiPhantGUI.Button(b, State == Phase.Practice ? "Start match" : "New match")) StartMatch(Time.realtimeSinceStartup);
            }

            // One card per player along the bottom.
            var players = Playing.ToList();
            if (players.Count == 0) return;
            float gap = 12 * scale, cardW = Mathf.Min(300 * scale, (width - gap * (players.Count + 1)) / players.Count), cardH = 84 * scale;
            if (arenaCamera && cam) arenaCamera.reserveBottom = (cardH + 24) / Mathf.Max(1, cam.pixelHeight); // keep the arena above the cards
            float total = players.Count * cardW + (players.Count - 1) * gap, x = x0 + (width - total) / 2, y = Screen.height - cardH - 12;
            foreach (var e in players)
            {
                Card(new Rect(x, y, cardW, cardH), e, scale);
                x += cardW + gap;
            }
        }

        void Card(Rect r, TankElephant e, float scale)
        {
            Shade(r, .8f);
            var stripe = new Rect(r.x, r.y, 8 * scale, r.height);
            var old = GUI.color; GUI.color = e.color; GUI.DrawTexture(stripe, Texture2D.whiteTexture); GUI.color = old;
            float pad = 16 * scale, line = 22 * scale;
            float cx = r.x + pad, cy = r.y + 4 * scale;
            cardTitle.normal.textColor = e.color;
            DigiPhantGUI.Label(new Rect(cx, cy, r.width - pad * 2, line + 4 * scale), "P" + e.player + Stars(Wins(e.player)), cardTitle);
            cy += line + 4 * scale;
            float pip = 13 * scale, pg = 5 * scale;
            for (int i = 0; i < e.maxHearts; i++) Pip(new Rect(cx + i * (pip + pg), cy, pip, pip), new Color(.95f, .15f, .2f), i < e.Hearts);
            float wx = cx + e.maxHearts * (pip + pg) + 14 * scale;
            for (int i = 0; i < e.maxWater; i++) Pip(new Rect(wx + i * (pip + pg), cy, pip, pip), new Color(.3f, .7f, 1), i < e.Water);
            cy += pip + 4 * scale;
            string status = e.KnockedOut ? "OUT" : e.Drinking ? "Drinking..." : e.Water == 0 ? "Out of water: go drink!" : e.InputSource;
            cardText.normal.textColor = e.KnockedOut || e.Water == 0 ? new Color(1, .45f, .45f) : new Color(.85f, .85f, .88f);
            DigiPhantGUI.Label(new Rect(cx, cy, r.width - pad * 2, line), status, cardText);
        }

        string Stars(int n) => n <= 0 ? "" : "  " + new string('*', n) + (n == 1 ? " win" : " wins");

        static void Shade(Rect r, float alpha)
        {
            var old = GUI.color;
            GUI.color = new Color(.04f, .04f, .05f, alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Pip(Rect r, Color c, bool full)
        {
            var old = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Texture2D.whiteTexture);
            GUI.color = full ? c : new Color(c.r * .3f, c.g * .3f, c.b * .3f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
