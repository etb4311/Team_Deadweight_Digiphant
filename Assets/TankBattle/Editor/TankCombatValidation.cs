using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DeadweightTanks.Editor
{
    // Edit-mode check of phases 3-4: drinking, ammo, shooting, hits, bounces, knockout and practice revive.
    // Moves elephants and fires real water shots in the tank scene, then reloads it (nothing is saved).
    public static class TankCombatValidation
    {
        [MenuItem("Tank Battle/Validate Combat (Phases 3-4)")]
        public static void Validate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(TankBattleBuilder.ScenePath)) { Debug.LogError("Build the arena first: Tank Battle > Build Arena Scene"); return; }
            EditorSceneManager.OpenScene(TankBattleBuilder.ScenePath);
            var tanks = UnityEngine.Object.FindObjectsByType<TankElephant>().OrderBy(t => t.player).ToArray();
            try { Run(tanks); }
            finally
            {
                foreach (var t in tanks) if (t) t.StopAnimation();
                EditorSceneManager.OpenScene(TankBattleBuilder.ScenePath); // discard test moves and shots
            }
            Debug.Log("Tank Battle combat validation (phases 3-4) passed.");
        }

        static void Run(TankElephant[] tanks)
        {
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
            Check(tanks.Length == 3, "Expected 3 elephants");
            Check(tanks.All(t => t.attack && t.drink && t.die && t.shotMaterial), "Combat clips/materials missing: rebuild the arena (Tank Battle > Build Arena Scene)");
            Check(UnityEngine.Object.FindAnyObjectByType<TankPond>(), "Pond has no TankPond: rebuild the arena");
            float now = 100;
            void Step(float seconds)
            {
                for (float s = 0; s < seconds; s += 1 / 60f)
                {
                    now += 1 / 60f;
                    Physics.SyncTransforms();
                    foreach (var t in tanks) t.Simulate(now, 1 / 60f);
                    foreach (var shot in UnityEngine.Object.FindObjectsByType<TankWaterShot>()) shot.Step(1 / 60f);
                }
            }
            void Place(TankElephant t, float x, float z, float yaw)
            {
                t.transform.SetPositionAndRotation(new Vector3(x, t.transform.position.y, z), Quaternion.Euler(0, yaw, 0));
                Physics.SyncTransforms();
            }
            foreach (var t in tanks) { t.useTestInput = true; t.testForward = t.testTurn = 0; t.reviveSeconds = 5; }
            Step(1 / 60f);
            var (p1, p2, p3) = (tanks[0], tanks[1], tanks[2]);
            Check(p1.Water == p1.maxWater && p1.Hearts == p1.maxHearts, "Elephants should start with full water and hearts");

            // Shooting across the open pond: P1 at x=-4 facing +x, P2 at x=+4.
            Place(p1, -4, 0, 90);
            Place(p2, 4, 0, -90);
            Place(p3, 0, 11.5f, 180);
            Check(!p1.RequestDrink(), "Drinking with a full tank should be refused");
            Check(p1.RequestShoot(), "P1 could not shoot");
            Check(p1.Water == p1.maxWater - 1, "Shooting should use one water");
            Check(!p1.RequestShoot(), "A second shot inside the cooldown should be refused");
            Step(1);
            Check(p2.Hearts == p2.maxHearts - 1, $"P2 should have lost a heart (hearts {p2.Hearts})");
            Check(!UnityEngine.Object.FindObjectsByType<TankWaterShot>().Any(), "The shot should be gone after hitting P2");

            // Two more hits knock P2 out; then P1 is empty.
            Step(.5f); Check(p1.RequestShoot(), "P1 second shot"); Step(1.2f);
            Step(.5f); Check(p1.RequestShoot(), "P1 third shot"); Step(1.2f);
            Check(p2.Hearts == 0 && p2.KnockedOut, $"P2 should be knocked out after 3 hits (hearts {p2.Hearts})");
            Check(p1.Water == 0 && !p1.RequestShoot(), "P1 should be out of water");

            // Drinking: refused outside the pond, works inside, locks movement, refills.
            Place(p1, -12, -6, 0);
            Check(!p1.InPond && !p1.RequestDrink(), "Drinking outside the pond should be refused");
            Place(p1, -3, 0, 90);
            Check(p1.InPond && p1.RequestDrink(), "Drinking in the pond should start");
            p1.testForward = 1;
            Vector3 before = p1.transform.position;
            Step(p1.drinkSeconds * .5f);
            Check(p1.Drinking && Vector3.Distance(before, p1.transform.position) < .01f, "Elephant should stand still while drinking");
            Step(p1.drinkSeconds * .5f + .2f);
            p1.testForward = 0;
            Check(!p1.Drinking && p1.Water == p1.maxWater, $"Drink should refill the tank (water {p1.Water})");

            // Practice revive: P2 returns to its spawn with full hearts.
            Step(5);
            Check(!p2.KnockedOut && p2.Hearts == p2.maxHearts, "P2 should be back after the practice revive");

            // Bounce: P1 fires at the west wall from close range; the ball comes back and can hit its own shooter.
            Place(p1, -14, 0, -90);
            int heartsBefore = p1.Hearts;
            Step(1.1f); // past the hit grace and cooldown
            Check(p1.RequestShoot(), "P1 could not fire at the wall");
            bool sawBounce = false;
            for (int i = 0; i < 120 && p1.Hearts == heartsBefore; i++)
            {
                Step(1 / 60f);
                sawBounce |= UnityEngine.Object.FindObjectsByType<TankWaterShot>().Any(s => s.Bounced);
            }
            Check(sawBounce, "The shot should bounce off the wall");
            Check(p1.Hearts == heartsBefore - 1, "A bounced shot should be able to hit its own shooter");

            // Once its bounces are used up, the next wall stops it (probe with 1 bounce: south wall, then west wall).
            var probe = new GameObject("Probe shot").AddComponent<TankWaterShot>();
            probe.bounces = 1;
            probe.transform.position = new Vector3(-18, 1.5f, -12);
            probe.Launch(p3, new Vector3(-1, 0, -1));
            for (int i = 0; i < 240 && probe; i++) probe.Step(1 / 60f);
            Check(!probe, "A shot should stop at the wall after its last bounce");
            Check(p1.shotBounces == 2, "Shots should bounce twice (rebuild the arena if this fails)");
        }
    }
}
