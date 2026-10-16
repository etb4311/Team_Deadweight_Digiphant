using System;
using UnityEngine;
using UnityEngine.InputSystem;
using DigiPhant;

namespace TeamDeadweight
{
    [Serializable] class PhoneSighting { public int slot; public float score; }
    [Serializable] class PhonePacket { public int version; public PhoneSighting[] phones; }

    // While a performer shows a phone, the elephant loses grip: its travel direction lags behind
    // where it faces, so steering makes it slide. Test key: D toggles drift.
    [DefaultExecutionOrder(100)] // after DigiPhantController.LateUpdate has moved the travel root
    [RequireComponent(typeof(DigiPhantLocomotion))]
    public class PhoneDrift : MonoBehaviour, IStatusLine
    {
        [Range(0, 4), Tooltip("Performer holding the phone. 0 = anyone in view.")]
        public int performer = 3;
        [Range(0, 1)] public float minimumScore = .5f;
        [Tooltip("Keep drifting through detection gaps shorter than this (seconds).")]
        public float holdSeconds = .7f;
        [Tooltip("How quickly travel turns toward the facing direction while drifting. Lower = slidier.")]
        public float driftGrip = 1.2f;
        [Tooltip("Same, with no phone in view.")]
        public float normalGrip = 15;

        public bool Drifting => testDrift || Time.realtimeSinceStartup - phoneSeen < holdSeconds;

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        LatestUdp udp;
        float phoneSeen = -1000;
        bool testDrift, tracking;
        Vector3 start, last, velocity;

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            if (Application.isPlaying) udp = new LatestUdp(controller.port + 4);
            tracking = false;
        }

        void OnDisable() { udp?.Dispose(); udp = null; }

        void Start() { if (locomotion.travelRoot) start = locomotion.travelRoot.position; }

        void LateUpdate()
        {
            ReadPhone();
            if (Keyboard.current != null && Keyboard.current.dKey.wasPressedThisFrame) testDrift = !testDrift;

            var root = locomotion.travelRoot;
            float dt = Mathf.Min(Time.deltaTime, .1f);
            if (!root || !locomotion.AnimationActive || dt <= 0) { tracking = false; return; }

            // Locomotion moved the root from `last` along its facing this frame. Replace that step
            // with a velocity that only gradually turns toward it.
            Vector3 moved = root.position;
            if (!tracking || (moved - last).sqrMagnitude > 1) // first frame or "Return to starting position"
            {
                tracking = true;
                last = moved;
                velocity = Vector3.zero;
                return;
            }
            float grip = Drifting ? driftGrip : normalGrip;
            velocity = Vector3.Lerp(velocity, (moved - last) / dt, 1 - Mathf.Exp(-grip * dt));
            Vector3 next = last + velocity * dt;
            if (locomotion.stageRadius > 0)
            {
                Vector3 offset = next - start;
                offset.y = 0;
                next = start + Vector3.ClampMagnitude(offset, locomotion.stageRadius);
            }
            next.y = moved.y;
            root.position = next;
            // A parented camera follows on its own; a followElephant camera gets the same correction.
            var cam = locomotion.followCamera;
            if (locomotion.followElephant && cam && !cam.transform.IsChildOf(root)) cam.transform.position += next - moved;
            last = next;
        }

        void ReadPhone()
        {
            string json = udp?.Poll();
            if (json == null || controller.inputMode != InputMode.Camera) return;
            PhonePacket packet;
            try { packet = JsonUtility.FromJson<PhonePacket>(json); }
            catch (ArgumentException) { return; }
            if (packet?.phones == null) return;
            foreach (var phone in packet.phones)
                if (phone != null && phone.score >= minimumScore && (performer == 0 || phone.slot == performer))
                    phoneSeen = Time.realtimeSinceStartup;
        }

        public string StatusTitle => "Drift";
        public string StatusValue => Drifting ? "ON · phone seen" : "off";
        public bool StatusActive => Drifting;

        void OnGUI()
        {
            if (DeadweightTheme.ShowsStatus) return; // shown on the theme's status card
            GUI.Label(new Rect(Screen.width - 270, 30, 260, 22), "Drift: " + (Drifting ? "ON" : "off") + "  (D test)");
        }
    }
}
