using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using DigiPhant;

namespace DeadweightTanks
{
    // One player's tank elephant. Hand-motor driving from that player's camera lane (or their test keys),
    // its own gait animation, and a CharacterController so it can't pass walls, cover or other elephants.
    // Combat: holds up to maxWater shots; ✌️ (or the drink key) in the pond drinks a full tank; ✊ (or the shoot
    // key) fires a water ball from the trunk; maxHearts hits knock it out.
    // Independent of DigiPhantLocomotion, which drives the single demo elephant.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)] // after DigiPhantController reads the camera, before TankArenaCamera (200)
    [RequireComponent(typeof(CharacterController), typeof(Animator))]
    public class TankElephant : MonoBehaviour
    {
        [Range(1, 3)] public int player = 1;
        public Color color = Color.red;
        [Tooltip("Camera input. Empty = keyboard only.")]
        public DigiPhantController controller;
        [Tooltip("Gesture input (drink / shoot). Empty = keyboard only.")]
        public TankGestures gestures;
        public TankKeys keys = TankKeys.ForPlayer(1);

        [Header("Hand motors")]
        [Range(.05f, 1)] public float upThreshold = .35f;
        [Range(.05f, 1)] public float downThreshold = .35f;
        [Range(.2f, 2)] public float fullSpeedHeight = 1;
        [Range(0, .5f)] public float turnDelay = .15f;

        [Header("Movement")]
        public float walkSpeed = 3;
        public float runSpeed = 6;
        public float backwardSpeed = 2;
        public float turnDegreesPerSecond = 100;
        [Range(.2f, .95f)] public float runThreshold = .65f;
        [Range(0, 20)] public float inputSmoothing = 8;
        [Tooltip("Ground speeds at which the walk / run / backward clips play at normal rate, and the turn rate of the turn clips.")]
        public float walkClipSpeed = 1.8f, runClipSpeed = 4.8f, backwardClipSpeed = 1.2f, turnClipDegreesPerSecond = 31;
        [Min(.01f)] public float animationBlendSeconds = .2f;

        [Header("Combat")]
        [Min(1)] public int maxHearts = 3;
        [Min(1)] public int maxWater = 3;
        [Tooltip("Seconds between shots.")] public float shotCooldown = .6f;
        [Tooltip("How long a drink takes (can't move meanwhile).")] public float drinkSeconds = 2.2f;
        [Tooltip("How long the trunk attack animation plays.")] public float attackSeconds = 1.1f;
        [Range(0, 1), Tooltip("Point in the attack animation where the water ball leaves.")] public float fireAt = .35f;
        public float shotSpeed = 14;
        public int shotBounces = 2;
        [Tooltip("Height of the water ball above the ground: hits elephants, walls and cover.")] public float shotHeight = 1.5f;
        [Tooltip("After a hit, seconds before the next one counts.")] public float hitGraceSeconds = 1;
        [Tooltip("Practice mode: seconds after a knockout before respawning. 0 = stay out (rounds arrive in phase 5).")]
        public float reviveSeconds = 5;
        [Tooltip("Stop turning while the shoot sign is showing and briefly after firing, so closing your hands doesn't spoil the aim.")]
        public bool steadyAim = true;
        [Tooltip("Seconds turning stays frozen after a shot (with steady aim).")]
        public float aimHoldSeconds = .4f;
        public Material shotMaterial, splashMaterial;

        [Header("Animation")]
        public AnimationClip idle, walk, run, backward, turnLeft, turnRight;
        public AnimationClip attack, drink, die;
        [Tooltip("Bone whose exported clip drift is removed each frame (elephant_Main_ctrl).")]
        public Transform motionRoot;

        [Tooltip("Set by the match during countdowns and between rounds: no driving, drinking or shooting.")]
        public bool Frozen;

        [Header("Testing")]
        [Tooltip("Drive from testForward / testTurn instead of keys or camera.")]
        public bool useTestInput;
        [Range(-1, 1)] public float testForward;
        [Range(-1, 1)] public float testTurn;

        public string Action { get; private set; } = "Idle";
        public string InputSource { get; private set; } = "none";
        public float Throttle => throttle;
        public float Turn => turn;
        public float Speed { get; private set; }
        public int Hearts { get; private set; }
        public int Water { get; private set; }
        public bool Drinking { get; private set; }
        public bool KnockedOut { get; private set; }
        public bool InPond => TankPond.AnyContains(transform.position);
        public bool CanBeHit => !KnockedOut && !Frozen && clock >= graceUntil;
        public string Message => clock < messageUntil ? message : "";

        CharacterController body;
        Animator animator;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        AnimationClipPlayable[] playables;
        AnimationClip[] clips;
        Vector3 motionPosition, spawnPosition;
        Quaternion motionRotation, spawnRotation;
        float throttle, turn, cycle, groundY, turnSince, turnDirection;
        float clock, nextShot, graceUntil, reviveAt, messageUntil, aimHoldUntil;
        Material shotTint;
        string message = "";
        bool combatReady;
        static readonly string[] Actions = { "Idle", "Walk", "Run", "Backward", "Turn left", "Turn right" };
        static readonly List<TankElephant> All = new List<TankElephant>(); // for elephant-elephant separation

        void Awake()
        {
            body = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();
            groundY = transform.position.y;
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            All.RemoveAll(t => !t); // statics survive Play sessions here (domain reload is off)
            if (!All.Contains(this)) All.Add(this);
        }

        void OnDestroy()
        {
            All.Remove(this);
            if (shotTint) { if (Application.isPlaying) Destroy(shotTint); else DestroyImmediate(shotTint); }
        }

        float Radius => body.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);

        void OnDisable() => StopAnimation();

        // LateUpdate: after the Animator's own pass, so the gait and action poses written here are what renders.
        void LateUpdate() => Simulate(Time.realtimeSinceStartup, Time.deltaTime);

        // One step of input, combat, movement and animation. Public so the editor validation can drive it.
        public void Simulate(float now, float dt)
        {
            if (!body) Awake();
            if (!StartAnimation()) return;
            clock = now;
            dt = Mathf.Clamp(dt, 0, .1f);
            if (!combatReady) { Hearts = maxHearts; Water = maxWater; combatReady = true; }
            if (KnockedOut && reviveSeconds > 0 && now >= reviveAt) Revive();

            bool locked = KnockedOut || Drinking || Frozen;
            if (!locked) HandleActionInput();
            else if (gestures)
            {
                // Signs made while busy don't queue up and fire the moment the drink / knockout ends.
                gestures.Consume(player, gestures.drinkGesture);
                gestures.Consume(player, gestures.shootGesture);
            }

            bool valid = ReadInput(now, out float desiredForward, out float desiredTurn);
            if (locked) { desiredForward = desiredTurn = 0; valid = true; }
            if (!valid) throttle = turn = 0;
            else
            {
                float blend = inputSmoothing <= 0 || locked ? 1 : 1 - Mathf.Exp(-inputSmoothing * dt);
                throttle = Mathf.Lerp(throttle, desiredForward, blend);
                turn = Mathf.Lerp(turn, desiredTurn, blend);
            }
            if (Mathf.Abs(throttle) < .015f) throttle = 0;
            if (Mathf.Abs(turn) < .015f) turn = 0;

            bool running = throttle > runThreshold;
            Speed = throttle < 0 ? throttle * backwardSpeed : throttle <= runThreshold
                ? throttle / runThreshold * walkSpeed : Mathf.Lerp(walkSpeed, runSpeed, Mathf.InverseLerp(runThreshold, 1, throttle));
            transform.Rotate(0, turn * turnDegreesPerSecond * dt, 0, Space.World);
            Vector3 before = transform.position;
            if (body.enabled)
            {
                if (Speed != 0) body.Move(transform.forward * Speed * dt);
                SeparateFromOthers();
            }
            var p = transform.position;
            if (Mathf.Abs(p.y - groundY) > .001f) { p.y = groundY; transform.position = p; } // flat arena
            // Real ground speed (less when blocked), so legs don't keep walking into a wall.
            Vector3 moved = transform.position - before;
            moved.y = 0;
            float groundSpeed = dt > 0 ? moved.magnitude / dt : 0;

            int action = throttle < 0 ? 3 : throttle > 0 ? (running ? 2 : 1) : turn < 0 ? 4 : turn > 0 ? 5 : 0;
            Action = KnockedOut ? "Knocked out" : Drinking ? "Drinking" : Actions[action];
            Animate(action, groundSpeed, dt);
            ApplyActionClip(dt);
            // The CharacterController alone moves the elephant; remove the clips' own drift.
            motionRoot.localPosition = motionPosition;
            motionRoot.localRotation = motionRotation;
        }

        // CharacterControllers don't reliably block each other (and never in edit mode), so step out of any
        // elephant this one overlaps. Moving through body.Move keeps walls and cover respected.
        void SeparateFromOthers()
        {
            foreach (var other in All)
            {
                if (!other || other == this || !other.isActiveAndEnabled || !other.body || !other.body.enabled) continue;
                Vector3 away = transform.position - other.transform.position;
                away.y = 0;
                float overlap = Radius + other.Radius - away.magnitude;
                if (overlap <= 0) continue;
                if (away.sqrMagnitude < 1e-6f) away = -transform.forward; // exactly on top: back off
                body.Move(away.normalized * overlap);
            }
        }

        bool ReadInput(float now, out float forward, out float turnInput)
        {
            forward = turnInput = 0;
            float left, right;
            if (useTestInput)
            {
                InputSource = "test";
                forward = Mathf.Clamp(testForward, -1, 1);
                turnInput = Mathf.Clamp(testTurn, -1, 1);
                return true;
            }
            if (keys != null && keys.TryHands(upThreshold, fullSpeedHeight, out left, out right)) InputSource = "keys";
            else if (controller && controller.inputMode == InputMode.Camera &&
                     controller.TryReadMovement(player, Movement.LeftHandHeight, now, out left) &&
                     controller.TryReadMovement(player, Movement.RightHandHeight, now, out right)) InputSource = "camera";
            else
            {
                InputSource = controller && controller.inputMode == InputMode.Camera && !controller.IsCalibrated ? "set neutral pose" : "no input";
                turnDirection = 0;
                return false;
            }
            TankControls.Mix(left, right, upThreshold, downThreshold, fullSpeedHeight, out forward, out turnInput);
            if (steadyAim && Aiming) { turnInput = 0; turnDirection = 0; } // hold the aim; walking still works
            // One hand must stay up alone briefly before turning, so raising/lowering both hands doesn't swerve.
            if (turnInput != turnDirection) { turnDirection = turnInput; turnSince = now; }
            if (turnInput != 0 && now - turnSince < turnDelay) turnInput = 0;
            return true;
        }

        // ---------- combat ----------

        // True while the shoot sign is showing (it takes a moment to be recognised and held) or just after a shot.
        public bool Aiming => clock < aimHoldUntil || (gestures && gestures.Showing(player) == gestures.shootGesture);

        void HandleActionInput()
        {
            bool drinkKey = keys != null && keys.Pressed(keys.drink);
            bool shootKey = keys != null && keys.Pressed(keys.shoot);
            bool drinkSign = gestures && gestures.Consume(player, gestures.drinkGesture);
            bool shootSign = gestures && gestures.Consume(player, gestures.shootGesture);
            if (drinkKey || drinkSign) RequestDrink();
            else if (shootKey || shootSign) RequestShoot();
        }

        // Start drinking if in the pond and not full. Returns true if the drink started.
        public bool RequestDrink()
        {
            if (KnockedOut || Drinking) return false;
            if (Water >= maxWater) { Say("Tank already full"); return false; }
            if (!InPond) { Say("Go to the pond to drink"); return false; }
            Drinking = true;
            StartActionClip(drink, drinkSeconds, fullBody: true, hold: false, onDone: () => { Water = maxWater; Drinking = false; Say("Tank full!"); });
            return true;
        }

        // Fire a water ball if there's water and the cooldown has passed. Returns true if the shot started.
        public bool RequestShoot()
        {
            if (KnockedOut || Drinking || clock < nextShot) return false;
            if (Water <= 0) { Say("Out of water — drink at the pond"); return false; }
            Water--;
            nextShot = clock + shotCooldown;
            aimHoldUntil = clock + aimHoldSeconds;
            bool fired = false;
            StartActionClip(attack, attackSeconds, fullBody: false, hold: false,
                onDone: () => { if (!fired) { fired = true; Fire(); } },
                at: fireAt, onAt: () => { if (!fired) { fired = true; Fire(); } });
            return true;
        }

        void Fire()
        {
            // Start inside the shooter's own body (which it ignores until a bounce) at a fixed height, so the first
            // sweep finds anything in front of the trunk: a wall or elephant at point-blank range can't be skipped.
            Vector3 start = transform.position;
            start.y = groundY + shotHeight;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Water shot P" + player;
            DestroyImmediate(ball.GetComponent<Collider>()); // now, so it can't block anyone's move this frame
            ball.transform.position = start;
            var shot = ball.AddComponent<TankWaterShot>();
            ball.transform.localScale = Vector3.one * shot.radius * 2;
            // Each player's water is their colour, so everyone can see whose shot is whose.
            if (!shotTint || shotTint.name != "Water shot P" + player + " " + ColorUtility.ToHtmlStringRGB(color))
                shotTint = MakeShotMaterial();
            ball.GetComponent<Renderer>().sharedMaterial = shotTint;
            if (Application.isPlaying)
            {
                var trail = ball.AddComponent<TrailRenderer>();
                trail.time = .3f;
                trail.startWidth = shot.radius * 1.6f;
                trail.endWidth = 0;
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0);
                if (shotTint) trail.sharedMaterial = shotTint;
            }
            shot.tint = color;
            shot.speed = shotSpeed;
            shot.bounces = shotBounces;
            shot.splashMaterial = splashMaterial;
            shot.Launch(this, transform.forward);
        }

        // This player's water: the shot material's shader with every colour property set explicitly to the player colour
        // (base, legacy _Color and emission), so the ball, trail and glow all read as this player's.
        Material MakeShotMaterial()
        {
            if (shotTint) { if (Application.isPlaying) Destroy(shotTint); else DestroyImmediate(shotTint); }
            var shader = shotMaterial ? shotMaterial.shader : Shader.Find("Universal Render Pipeline/Lit");
            var m = shotMaterial ? new Material(shotMaterial) : new Material(shader);
            m.name = "Water shot P" + player + " " + ColorUtility.ToHtmlStringRGB(color);
            foreach (var property in new[] { "_BaseColor", "_Color" })
                if (m.HasProperty(property)) m.SetColor(property, color);
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", color * 1.2f);
            }
            return m;
        }

        // Called by a water ball. Ignored while knocked out or in the short grace period after a hit.
        public void TakeHit(TankWaterShot shot)
        {
            if (!CanBeHit) return;
            Hearts = Mathf.Max(0, Hearts - 1);
            graceUntil = clock + hitGraceSeconds;
            string by = shot && shot.shooter ? (shot.shooter == this ? "your own bounced shot" : "P" + shot.shooter.player) : "water";
            if (Hearts > 0) { Say("Hit by " + by + "!"); return; }
            KnockedOut = true;
            Drinking = false;
            body.enabled = false; // shots and elephants pass through while out
            reviveAt = clock + reviveSeconds;
            Say("KNOCKED OUT by " + by);
            StopActionClip(); // an interrupted drink must not finish (and refill) now
            StartActionClip(die, 1.6f, fullBody: true, hold: true, onDone: null);
        }

        void Revive()
        {
            ResetForRound();
            graceUntil = clock + 2;
            Say("Back in!");
        }

        // Round over: drop any drink or attack in progress (so no shot leaves after the whistle). A knockout pose stays.
        public void CancelActions()
        {
            if (KnockedOut) return;
            StopActionClip();
            Drinking = false;
        }

        // Back to the spawn point with full hearts and water, ready for a new round.
        public void ResetForRound()
        {
            if (!body) Awake();
            StopActionClip();
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            Physics.SyncTransforms(); // teleport: let the controller see the new position before it moves
            body.enabled = true;
            KnockedOut = Drinking = false;
            Hearts = maxHearts;
            Water = maxWater;
            combatReady = true;
            throttle = turn = 0;
            turnDirection = 0;
            graceUntil = nextShot = aimHoldUntil = clock;
            messageUntil = 0;
        }

        void Say(string text) { message = text; messageUntil = clock + 2; }

        // ---------- animation ----------

        bool StartAnimation()
        {
            if (graph.IsValid()) return true;
            if (!idle || !walk || !run || !backward || !turnLeft || !turnRight || !motionRoot) { Action = "Animation setup incomplete"; return false; }
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            motionPosition = motionRoot.localPosition;
            motionRotation = motionRoot.localRotation;
            graph = PlayableGraph.Create("Tank elephant P" + player);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            clips = new[] { idle, walk, run, backward, turnLeft, turnRight };
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            playables = new AnimationClipPlayable[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetApplyFootIK(false);
                graph.Connect(playables[i], 0, mixer, i);
                mixer.SetInputWeight(i, i == 0 ? 1 : 0);
            }
            AnimationPlayableOutput.Create(graph, "Tank gait", animator).SetSourcePlayable(mixer);
            graph.Play();
            return true;
        }

        void Animate(int action, float groundSpeed, float dt)
        {
            // Play each clip at the speed its feet were authored for. Blocked = idle-slow legs, not a treadmill.
            float rate = action == 0 ? 1
                : action == 4 || action == 5 ? Mathf.Abs(turn) * turnDegreesPerSecond / turnClipDegreesPerSecond
                : groundSpeed / (action == 3 ? backwardClipSpeed : action == 2 ? runClipSpeed : walkClipSpeed);
            rate = Mathf.Max(.15f, rate);
            cycle = Mathf.Repeat(cycle + dt * rate / Mathf.Max(.01f, clips[action].length), 1);
            float weightBlend = 1 - Mathf.Exp(-dt / Mathf.Max(.01f, animationBlendSeconds));
            for (int i = 0; i < clips.Length; i++)
            {
                mixer.SetInputWeight(i, Mathf.Lerp(mixer.GetInputWeight(i), i == action ? 1 : 0, weightBlend));
                playables[i].SetTime(cycle * clips[i].length);
            }
            graph.Evaluate(0);
        }

        // Action clips (attack / drink / die) are sampled over the gait: attack on the neck, head and trunk only
        // (legs keep walking), drink and die on the whole body. Blended in and out, or held at the end (die).
        AnimationClip actionClip;
        float actionTime, actionDuration, actionAt;
        bool actionFull, actionHold, actionAtDone;
        System.Action actionDone, actionAtCallback;
        Transform[] bones;
        bool[] upperBody;
        Vector3[] savedPosition;
        Quaternion[] savedRotation;

        void StartActionClip(AnimationClip clip, float seconds, bool fullBody, bool hold, System.Action onDone, float at = -1, System.Action onAt = null)
        {
            actionDone?.Invoke(); // finish anything still playing (e.g. a refill) before replacing it
            // Re-triggering the same clip (rapid shots) starts already blended in, so the head doesn't snap back.
            bool continuing = clip && clip == actionClip && !hold;
            actionClip = clip;
            actionTime = continuing ? Mathf.Min(.2f, seconds * .5f) : 0;
            actionDuration = Mathf.Max(.05f, seconds);
            actionFull = fullBody;
            actionHold = hold;
            actionDone = onDone;
            actionAt = at;
            actionAtCallback = onAt;
            actionAtDone = false;
            if (!clip && !hold) { var done = actionDone; actionDone = null; done?.Invoke(); } // no animation: finish now
        }

        void StopActionClip() { actionClip = null; actionDone = null; actionAtCallback = null; }

        void ApplyActionClip(float dt)
        {
            if (actionDone == null && actionClip == null) return;
            actionTime += dt;
            float t01 = Mathf.Clamp01(actionTime / actionDuration);
            if (!actionAtDone && actionAt >= 0 && t01 >= actionAt) { actionAtDone = true; actionAtCallback?.Invoke(); }
            if (!actionHold && actionTime >= actionDuration)
            {
                var done = actionDone;
                actionClip = null; actionDone = null; actionAtCallback = null;
                done?.Invoke();
                return;
            }
            if (!actionClip) return;
            if (bones == null)
            {
                bones = animator.GetComponentsInChildren<Transform>(true);
                var neck = bones.FirstOrDefault(b => b.name == "elephant_Neck_bone");
                upperBody = bones.Select(b => neck && b.IsChildOf(neck)).ToArray();
                savedPosition = new Vector3[bones.Length];
                savedRotation = new Quaternion[bones.Length];
            }
            const float blendSeconds = .2f;
            float weight = actionHold ? Mathf.Clamp01(actionTime / blendSeconds)
                : Mathf.Clamp01(Mathf.Min(actionTime, actionDuration - actionTime) / blendSeconds);
            for (int i = 0; i < bones.Length; i++) { savedPosition[i] = bones[i].localPosition; savedRotation[i] = bones[i].localRotation; }
            actionClip.SampleAnimation(animator.gameObject, t01 * actionClip.length); // poses the whole rig
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                bool take = actionFull ? b != transform && b != motionRoot : upperBody[i];
                float w = take ? weight : 0;
                b.localPosition = Vector3.Lerp(savedPosition[i], b.localPosition, w);
                b.localRotation = Quaternion.Slerp(savedRotation[i], b.localRotation, w);
            }
        }

        public void StopAnimation()
        {
            if (graph.IsValid()) graph.Destroy();
            throttle = turn = Speed = 0;
            Action = "Idle";
        }

        // ---------- label and practice HUD ----------

        GUIStyle labelStyle, noteStyle;

        void OnGUI()
        {
            var cam = Camera.main;
            if (!cam) return;
            Vector3 s = cam.WorldToScreenPoint(transform.position + Vector3.up * 3.8f);
            if (s.z <= 0) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                noteStyle = new GUIStyle(labelStyle) { fontSize = 15 };
            }
            float x = s.x, y = Screen.height - s.y;
            Outlined(new Rect(x - 40, y - 30, 80, 30), "P" + player, labelStyle, color);

            // Hearts (red squares) and water (blue squares); hollow when used up.
            const float pip = 12, gap = 4;
            float rowWidth = (maxHearts + maxWater) * (pip + gap) + 10;
            float px = x - rowWidth / 2;
            for (int i = 0; i < maxHearts; i++, px += pip + gap) Pip(new Rect(px, y, pip, pip), new Color(.95f, .15f, .2f), i < Hearts);
            px += 10;
            for (int i = 0; i < maxWater; i++, px += pip + gap) Pip(new Rect(px, y, pip, pip), new Color(.3f, .7f, 1), i < Water);

            string note = KnockedOut ? (Message.Length > 0 ? Message : "KNOCKED OUT") : Drinking ? "DRINKING..." : Message;
            if (note.Length > 0) Outlined(new Rect(x - 150, y + 14, 300, 24), note, noteStyle, Color.white);
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

        static void Outlined(Rect r, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = Color.black;
            for (int dx = -2; dx <= 2; dx += 2)
                for (int dy = -2; dy <= 2; dy += 2)
                    if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }
    }
}
