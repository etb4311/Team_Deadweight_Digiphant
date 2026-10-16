using UnityEngine;
using UnityEngine.InputSystem;
using DigiPhant;

namespace TeamDeadweight
{
    // Two "motors" driven by one performer's hands, measured from the neutral pose
    // (elbows out, hands level with the shoulders):
    //   both hands up = forward (higher = faster), both hands down = backward,
    //   only the left hand up = turn left on the spot, only the right hand up = turn right.
    // Test keys (held): Up = both up, Down = both down, Left / Right = one hand up.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DigiPhantController), typeof(DigiPhantLocomotion))]
    public class HandMotors : MonoBehaviour, IStatusLine
    {
        [Range(1, 4)] public int performer = 1;
        [Tooltip("How far above neutral a hand must rise to count as up. Unit: torso length (Full body) or shoulder width (Seated).")]
        [Range(.05f, 1)] public float upThreshold = .35f;
        [Tooltip("How far below neutral a hand must drop to count as down.")]
        [Range(.05f, 1)] public float downThreshold = .35f;
        [Tooltip("Hand height that gives full speed; speed ramps up from the threshold. Past the locomotion run threshold the elephant runs.")]
        [Range(.2f, 2)] public float fullSpeedHeight = 1;
        [Tooltip("Seconds one hand must stay up alone before the elephant turns, so raising or lowering both hands doesn't swerve.")]
        [Range(0, .5f)] public float turnDelay = .15f;

        public string State { get; private set; } = "Idle";

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        float turnSince, turnDirection; // when the current single-hand turn began, and its sign

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            locomotion.driveOverride = Drive;
        }

        void OnDisable()
        {
            if (locomotion.driveOverride == Drive) locomotion.driveOverride = null;
            State = "Idle";
        }

        bool Drive(float now, out float forward, out float turn)
        {
            float left, right;
            if (performer > controller.performerCount)
            {
                forward = turn = 0;
                turnDirection = 0;
                State = "P" + performer + " is not in the group";
                return false;
            }
            if (!TestKeys(out left, out right) &&
                (!controller.TryReadMovement(performer, Movement.LeftHandHeight, now, out left) ||
                 !controller.TryReadMovement(performer, Movement.RightHandHeight, now, out right)))
            {
                forward = turn = 0;
                turnDirection = 0;
                State = "Hands not tracked";
                return false;
            }
            Mix(left, right, upThreshold, downThreshold, fullSpeedHeight, out forward, out turn);
            if (turn != turnDirection) { turnDirection = turn; turnSince = now; }
            if (turn != 0 && now - turnSince < turnDelay) turn = 0;
            State = forward > 0 ? "Forward" : forward < 0 ? "Backward" : turn < 0 ? "Turn left" : turn > 0 ? "Turn right" : "Idle";
            return true;
        }

        // Hand heights are relative to neutral: positive = above, negative = below.
        public static void Mix(float left, float right, float up, float down, float fullSpeedHeight, out float forward, out float turn)
        {
            forward = turn = 0;
            bool leftUp = left > up, rightUp = right > up;
            if (leftUp && rightUp) forward = Mathf.InverseLerp(up, Mathf.Max(up + .01f, fullSpeedHeight), Mathf.Min(left, right));
            else if (left < -down && right < -down) forward = -Mathf.InverseLerp(down, Mathf.Max(down + .01f, fullSpeedHeight), -Mathf.Max(left, right));
            else if (leftUp) turn = -1;
            else if (rightUp) turn = 1;
        }

        bool TestKeys(out float left, out float right)
        {
            left = right = 0;
            var k = Keyboard.current;
            if (k == null) return false;
            // Halfway between threshold and full speed walks.
            float high = (upThreshold + Mathf.Max(upThreshold, fullSpeedHeight)) / 2, low = -(downThreshold + Mathf.Max(downThreshold, fullSpeedHeight)) / 2;
            if (k.upArrowKey.isPressed) left = right = high;
            else if (k.downArrowKey.isPressed) left = right = low;
            else if (k.leftArrowKey.isPressed) left = high;
            else if (k.rightArrowKey.isPressed) right = high;
            else return false;
            return true;
        }

        public string StatusTitle => "Motors P" + performer;
        public string StatusValue => controller && controller.inputMode == InputMode.Camera ? State : "Camera mode only";
        public bool StatusActive => StatusValue != "Idle" && StatusValue != "Camera mode only" && StatusValue != "Hands not tracked";

        void OnGUI()
        {
            if (DeadweightTheme.ShowsStatus) return; // shown on the theme's status card
            GUI.Label(new Rect(Screen.width - 270, 50, 260, 22), "Hand motors P" + performer + ": " + StatusValue + "  (arrows test)");
        }
    }
}
