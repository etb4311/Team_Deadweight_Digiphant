using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeadweightTanks.Editor
{
    // Edit-mode check of phase 5: practice, countdown freeze, round wins, draw, match win, back to practice,
    // and the 2-player group size. Runs in the tank scene, then reloads it (nothing is saved).
    public static class TankMatchValidation
    {
        [MenuItem("Tank Battle/Validate Match (Phase 5)")]
        public static void Validate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(TankBattleBuilder.ScenePath)) { Debug.LogError("Build the arena first: Tank Battle > Build Arena Scene"); return; }
            EditorSceneManager.OpenScene(TankBattleBuilder.ScenePath);
            var tanks = UnityEngine.Object.FindObjectsByType<TankElephant>().OrderBy(t => t.player).ToArray();
            try { Run(tanks); }
            finally
            {
                foreach (var t in UnityEngine.Object.FindObjectsByType<TankElephant>(FindObjectsInactive.Include)) if (t) t.StopAnimation();
                EditorSceneManager.OpenScene(TankBattleBuilder.ScenePath);
            }
            Debug.Log("Tank Battle match validation (phase 5) passed.");
        }

        static void Run(TankElephant[] tanks)
        {
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
            var match = UnityEngine.Object.FindAnyObjectByType<TankMatch>();
            Check(match, "No TankMatch in the scene: rebuild the arena (Tank Battle > Build Arena Scene)");
            Check(tanks.Length == 3, "Expected 3 elephants");
            float now = 100;
            void Step(float seconds)
            {
                for (float s = 0; s < seconds; s += 1 / 60f)
                {
                    now += 1 / 60f;
                    Physics.SyncTransforms();
                    match.Tick(now);
                    foreach (var t in tanks) if (t.gameObject.activeSelf) t.Simulate(now, 1 / 60f);
                }
            }
            // A hit from nowhere, spaced past the grace period.
            void KnockOut(TankElephant t) { while (!t.KnockedOut) { t.TakeHit(null); Step(t.hitGraceSeconds + .05f); } }
            foreach (var t in tanks) { t.useTestInput = true; t.testForward = t.testTurn = 0; }
            Step(.1f);
            Check(match.State == TankMatch.Phase.Practice, "Should start in practice");

            // Countdown: everyone reset and frozen.
            var p1 = tanks[0];
            p1.TakeHit(null);
            match.StartMatch(now);
            Check(match.State == TankMatch.Phase.Countdown && match.Round == 1, "Start should begin round 1's countdown");
            Check(p1.Hearts == p1.maxHearts, "Round start should refill hearts");
            Vector3 before = p1.transform.position;
            p1.testForward = 1;
            Step(match.countdownSeconds * .5f);
            Check(Vector3.Distance(before, p1.transform.position) < .01f, "Elephants should be frozen during the countdown");
            Step(match.countdownSeconds * .5f + .1f);
            Check(match.State == TankMatch.Phase.Playing, "Countdown should end in play");
            Step(.3f);
            Check(Vector3.Distance(before, p1.transform.position) > .3f, "Elephants should move once the round starts");
            p1.testForward = 0;

            // Round 1: P2 and P3 knocked out -> P1 wins it. No revives during a round.
            KnockOut(tanks[1]);
            Step(1);
            Check(match.State == TankMatch.Phase.Playing && tanks[1].KnockedOut, "One knockout shouldn't end a 3-player round");
            KnockOut(tanks[2]);
            Step(match.settleSeconds + .15f);
            Check(match.State == TankMatch.Phase.RoundOver && match.Wins(1) == 1, $"P1 should win round 1 (state {match.State}, wins {match.Wins(1)})");
            Step(match.roundOverSeconds + .1f);
            Check(match.State == TankMatch.Phase.Countdown && match.Round == 2, "Round 2 should start after the round-over pause");
            Check(tanks.All(t => !t.KnockedOut && t.Hearts == t.maxHearts), "Everyone should be back for round 2");

            // Round 2: everyone except P2 goes down together -> P2 wins it.
            Step(match.countdownSeconds + .1f);
            tanks[0].TakeHit(null); tanks[2].TakeHit(null);
            Step(tanks[0].hitGraceSeconds + .05f);
            tanks[0].TakeHit(null); tanks[2].TakeHit(null);
            Step(tanks[0].hitGraceSeconds + .05f);
            tanks[0].TakeHit(null); tanks[2].TakeHit(null);
            Step(match.settleSeconds + .15f);
            Check(match.Wins(2) == 1 && match.State == TankMatch.Phase.RoundOver, "P2 should win round 2");

            // Round 3: P1 wins it -> P1 wins the match (2 wins).
            Step(match.roundOverSeconds + match.countdownSeconds + .2f);
            KnockOut(tanks[1]);
            KnockOut(tanks[2]);
            Step(match.settleSeconds + .15f);
            Check(match.State == TankMatch.Phase.MatchOver && match.Wins(1) == 2 && match.Banner.Contains("P1 WINS THE MATCH"),
                $"P1 should win the match (state {match.State}, P1 wins {match.Wins(1)})");
            Step(match.roundOverSeconds + 1);
            Check(match.State == TankMatch.Phase.MatchOver, "Match over should wait for Space / New match");

            // Shots can't change a finished round: none left in the air, frozen elephants can't be hit.
            Check(!UnityEngine.Object.FindObjectsByType<TankWaterShot>().Any(), "Shots should be cleared when a round ends");
            int hearts = tanks[0].Hearts;
            tanks[0].TakeHit(null);
            Check(tanks[0].Hearts == hearts, "Frozen elephants shouldn't take hits after the round");

            // Back to practice: wins cleared, practice revives on.
            match.ToPractice();
            Step(.1f);
            Check(match.State == TankMatch.Phase.Practice && match.Wins(1) == 0, "Backspace should return to practice");
            KnockOut(tanks[1]);
            Step(match.practiceReviveSeconds + .2f);
            Check(!tanks[1].KnockedOut, "Practice should revive knocked-out elephants");

            // Draw: the last two go down within the settle window (here the same frame).
            match.StartMatch(now);
            Step(match.countdownSeconds + .1f);
            KnockOut(tanks[2]);
            for (int i = 0; i < 3; i++) { tanks[0].TakeHit(null); tanks[1].TakeHit(null); Step(tanks[0].hitGraceSeconds + .05f); }
            Check(match.Banner == "DRAW!" && match.Wins(1) == 0 && match.Wins(2) == 0, $"Simultaneous knockouts should be a draw (banner '{match.Banner}')");

            // Group size 2: P3's elephant leaves (applied in practice).
            match.ToPractice();
            var controller = match.controller;
            Check(controller, "Match has no controller reference");
            controller.performerCount = 2;
            Step(.1f);
            Check(!tanks[2].gameObject.activeSelf && match.Playing.Count() == 2, "Group size 2 should remove P3's elephant");
            controller.performerCount = 3;
            Step(.1f);
            Check(tanks[2].gameObject.activeSelf, "Group size 3 should bring P3 back");
        }
    }
}
