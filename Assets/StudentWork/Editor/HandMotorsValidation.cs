using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using DigiPhant;

namespace TeamDeadweight.Editor
{
    // Run from DigiPhant > Validate Hand Motors (outside Play mode), or in batch mode with
    // -executeMethod TeamDeadweight.Editor.HandMotorsValidation.Run. Offers to save open scenes,
    // then tests on the team scene and reloads it from disk, discarding the test changes.
    public static class HandMotorsValidation
    {
        public const string ScenePath = "Assets/StudentWork/Scenes/TeamDeadweight.unity";

        [MenuItem("DigiPhant/Validate Hand Motors")]
        public static void Run()
        {
            void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

            // Pure mixing rules.
            HandMotors.Mix(.36f, .9f, .35f, .35f, 1, out float f, out float t);
            Check(f > 0 && f < .05f && t == 0, "Speed should ramp up from zero just above the threshold");
            HandMotors.Mix(.675f, .9f, .35f, .35f, 1, out f, out t);
            Check(Mathf.Approximately(f, .5f) && t == 0, "Both hands up should drive forward, scaled by the lower hand");
            HandMotors.Mix(-.675f, -.9f, .35f, .35f, 1, out f, out t);
            Check(Mathf.Approximately(f, -.5f) && t == 0, "Both hands down should drive backward");
            HandMotors.Mix(.5f, 0, .35f, .35f, 1, out f, out t);
            Check(f == 0 && t == -1, "Left hand up should turn left on the spot");
            HandMotors.Mix(-.5f, .5f, .35f, .35f, 1, out f, out t);
            Check(f == 0 && t == 1, "Right hand up should turn right on the spot");
            HandMotors.Mix(.2f, -.2f, .35f, .35f, 1, out f, out t);
            Check(f == 0 && t == 0, "Hands near neutral should idle");

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try { RunInScene(Check); }
            finally { EditorSceneManager.OpenScene(ScenePath); }
            Debug.Log("HandMotors validation passed.");
        }

        // Through the real controller and locomotion with fake camera packets.
        static void RunInScene(Action<bool, string> Check)
        {
            EditorSceneManager.OpenScene(ScenePath);
            var c = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            var loco = c.GetComponent<DigiPhantLocomotion>();
            Check(loco && loco.Initialise(), "Missing locomotion setup");
            var motors = c.GetComponent<HandMotors>();
            if (!motors) motors = c.gameObject.AddComponent<HandMotors>();
            typeof(HandMotors).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(motors, null);
            Check(loco.driveOverride != null, "HandMotors did not hook locomotion");
            c.inputMode = InputMode.Camera;
            c.CaptureRestPose();
            loco.inputSmoothing = 0;

            float now = 10;
            string Packet(float left, float right) => JsonUtility.ToJson(new PoseFrame
            {
                version = 1, performerCount = c.performerCount, upperBodyOnly = c.upperBodyOnly,
                people = Enumerable.Range(1, c.performerCount).Select(i => new PerformerFrame
                {
                    slot = i, confidence = Enumerable.Repeat(1f, 6).ToArray(),
                    values = i == motors.performer ? new[] { left, right, 0, 0, 0, 0 } : new float[6]
                }).ToArray()
            });
            Check(c.AcceptPacket(Packet(0, 0), now), "Baseline rejected");
            Check(c.CalibrateAt(now), "Calibration failed");
            void Expect(float left, float right, string action, float later = 0)
            {
                now += later;
                Check(c.AcceptPacket(Packet(left, right), now), "Packet rejected");
                c.ApplyControls(now, 1 / 60f);
                Check(loco.CurrentAction.StartsWith(action), $"Hands ({left}, {right}) gave '{loco.CurrentAction}', expected '{action}'");
            }
            Expect(.5f, .5f, "Walk");
            Expect(1.2f, 1.2f, "Run");
            Expect(-.5f, -.5f, "Backward");
            Expect(.5f, 0, "Idle"); // one hand up briefly: no swerve yet
            Expect(.5f, 0, "Turn left", motors.turnDelay + .01f);
            Expect(0, .5f, "Idle");
            Expect(0, .5f, "Turn right", motors.turnDelay + .01f);
            Expect(0, 0, "Idle");

            c.ApplyControls(now + 5, 1 / 60f);
            Check(loco.CurrentAction == "Idle" && motors.State == "Hands not tracked", "Lost tracking should stop the elephant");

            typeof(HandMotors).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(motors, null);
            Check(loco.driveOverride == null, "Disabling HandMotors did not restore the mappings");
            loco.StopAnimation();
        }
    }
}
