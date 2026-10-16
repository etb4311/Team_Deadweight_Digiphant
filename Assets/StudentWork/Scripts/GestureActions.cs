using System;
using UnityEngine;
using UnityEngine.InputSystem;
using DigiPhant;

namespace TeamDeadweight
{
    [Serializable] class HandGestures { public int slot; public string left, right; public float leftScore, rightScore; }
    [Serializable] class GesturePacket { public int version; public HandGestures[] people; }

    // One performer's hand gesture plays Eat or Drink on the neck, head and trunk; the legs keep walking.
    // Test keys: E = eat, F = drink.
    [DefaultExecutionOrder(100)] // after DigiPhantController.LateUpdate has posed the elephant
    [RequireComponent(typeof(DigiPhantController))]
    public class GestureActions : MonoBehaviour, IStatusLine
    {
        [Range(1, 4)] public int performer = 2;
        public AnimationClip eat, drink;
        public string eatGesture = "Closed_Fist", drinkGesture = "Victory";
        [Range(0, 1)] public float minimumScore = .6f;
        [Tooltip("Seconds the gesture must be held before the clip starts.")]
        public float holdSeconds = .3f;
        public float blendSeconds = .4f;
        [Tooltip("Bones at or below this follow the clip. Empty = elephant_Neck_bone.")]
        public Transform takeoverRoot;

        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        LatestUdp udp;
        GesturePacket packet;
        float packetTime = -1000, heldSince, heldLastSeen = -1000;
        string held;
        bool armed;
        AnimationClip playing;
        float t;
        Transform[] bones;
        bool[] takeover;
        Vector3[] savedPosition;
        Quaternion[] savedRotation;

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            locomotion = GetComponent<DigiPhantLocomotion>();
            if (Application.isPlaying) udp = new LatestUdp(controller.port + 3);
        }

        void OnDisable() { udp?.Dispose(); udp = null; playing = null; }

        void LateUpdate()
        {
            float now = Time.realtimeSinceStartup;
            string json = udp?.Poll();
            if (json != null)
                try { packet = JsonUtility.FromJson<GesturePacket>(json); packetTime = now; }
                catch (ArgumentException) { }

            // Fire once per held gesture. Re-arm only after it has been gone or changed for holdSeconds,
            // so single-frame dropouts don't replay it; stay armed if a clip was already playing.
            string gesture = CurrentGesture(now);
            if (gesture != null && gesture == held) heldLastSeen = now;
            else if (held == null || now - heldLastSeen >= holdSeconds)
            {
                held = gesture;
                heldSince = heldLastSeen = now;
                armed = true;
            }
            if (held != null && armed && now - heldSince >= holdSeconds && Play(held == eatGesture ? eat : drink))
                armed = false;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame) Play(eat);
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame) Play(drink);
            if (playing) Advance();
        }

        string CurrentGesture(float now)
        {
            if (controller.inputMode != InputMode.Camera || packet?.people == null || now - packetTime > controller.trackingTimeout) return null;
            foreach (var p in packet.people)
            {
                if (p == null || p.slot != performer) continue;
                if (p.leftScore >= minimumScore && (p.left == eatGesture || p.left == drinkGesture)) return p.left;
                if (p.rightScore >= minimumScore && (p.right == eatGesture || p.right == drinkGesture)) return p.right;
            }
            return null;
        }

        bool Play(AnimationClip clip)
        {
            // Never overlap: a new clip waits until the current one finishes.
            if (playing || clip == null || locomotion == null || !locomotion.AnimationActive) return false;
            if (bones == null)
            {
                bones = locomotion.elephantAnimator.GetComponentsInChildren<Transform>(true);
                if (!takeoverRoot) takeoverRoot = Array.Find(bones, b => b.name == "elephant_Neck_bone");
                takeover = Array.ConvertAll(bones, b => takeoverRoot && b.IsChildOf(takeoverRoot));
                savedPosition = new Vector3[bones.Length];
                savedRotation = new Quaternion[bones.Length];
            }
            playing = clip;
            t = 0;
            return true;
        }

        void Advance()
        {
            if (!locomotion.AnimationActive) { playing = null; return; }
            t += Time.deltaTime;
            float weight = blendSeconds <= 0 ? 1 : Mathf.Clamp01(Mathf.Min(t, playing.length - t) / blendSeconds);
            for (int i = 0; i < bones.Length; i++)
            {
                savedPosition[i] = bones[i].localPosition;
                savedRotation[i] = bones[i].localRotation;
            }
            // SampleAnimation poses the whole rig; keep only the takeover bones, blended in and out.
            playing.SampleAnimation(locomotion.elephantAnimator.gameObject, Mathf.Min(t, playing.length));
            for (int i = 0; i < bones.Length; i++)
            {
                float w = takeover[i] ? weight : 0;
                bones[i].localPosition = Vector3.Lerp(savedPosition[i], bones[i].localPosition, w);
                bones[i].localRotation = Quaternion.Slerp(savedRotation[i], bones[i].localRotation, w);
            }
            if (t >= playing.length) playing = null;
        }

        public string StatusTitle => "Gesture P" + performer;
        public string StatusValue => (held ?? "none") + (playing ? " · " + playing.name : "");
        public bool StatusActive => playing;

        void OnGUI()
        {
            if (DeadweightTheme.ShowsStatus) return; // shown on the theme's status card
            GUI.Label(new Rect(Screen.width - 270, 10, 260, 22), StatusTitle + ": " + StatusValue + "  (E/F test)");
        }
    }
}
