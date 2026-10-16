# DigiPhant setup — Team Deadweight

**Team:** Adin, Evan, Grey

What was actually set up in this Unity project, what we added on top of the
instructor's starter and why, what has been checked, and how to rebuild it from
a fresh checkout. Follows the starter's `DigiPhantStarter/AGENT_SETUP.md` and
`DigiPhantStarter/STUDENT_GIT.md`.

**Status (2026-10-16):** Installed and running. The camera, pose tracking and
gesture recognition have run live. Tank Battle was recorded in a 2-minute solo
test. Two things are still unproven: a full three-person run with every
control, and a fresh-checkout rebuild. See [Verification](#verification).

## Contents

- [Environment](#environment)
- [Starter and layout](#starter-and-layout)
- [How the data flows](#how-the-data-flows)
- [Scenes to open](#scenes-to-open)
- [Running it](#running-it)
- [Additions since 10.08](#additions-since-1008)
- [Recording](#recording)
- [Changes made](#changes-made)
- [Verification](#verification)
- [Known limitations](#known-limitations)
- [Rebuild from a fresh checkout (macOS)](#rebuild-from-a-fresh-checkout-macos)
- [Changelog](#changelog)

## Environment

| Item | Value |
| --- | --- |
| OS | macOS 26.6.2, Apple Silicon (arm64) |
| Unity | 6000.6.3f1, Universal 3D (URP) template project |
| Render pipeline | URP 17.6.0 |
| Input | Active Input Handling = Input System Package (New) only (`activeInputHandler: 1`) |
| Python | 3.14.8, Homebrew `python@3.14` (`/opt/homebrew/bin/python3.14`) |
| Python environment | `DigiPhantStarter/Tracking/.venv` |
| Python packages | mediapipe 0.10.35, opencv-contrib-python 4.14.0.94 (supplied pins, unchanged), numpy 2.5.3; `pip check` clean |
| Extra MediaPipe models | `gesture_recognizer.task` and `efficientdet_lite0.tflite` (see [Rebuild](#rebuild-from-a-fresh-checkout-macos)) |
| Camera | Built-in MacBook Air camera, index 0. **Turn off iPhone Continuity Camera** (see [Limitations](#known-limitations)) |
| Group | 3 performers, seated (upper-body mode) |

Python 3.14 was installed through Homebrew because only the system Python 3.9.6
was present, and that is too old for the pinned packages.

## Starter and layout

| Item | Value |
| --- | --- |
| Starter repository | https://github.com/kommanderpi/studentstarter.git |
| Starter commit | `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f` |
| Clone location | `DigiPhantStarter/` beside `Assets/` (the launcher expects this) |
| Imported from the starter | `Assets/Elephant`, `Assets/DigiPhant`, `Assets/SavannahCourse` (each with its `.meta`) |

```text
Team_Deadweight_Digiphant/          (project root, this repository)
  Assets/
    DigiPhant/                      reference scripts + scene (tracked; small adaptations, see Changes made)
    Elephant/                       vendor elephant (NOT tracked; restore from starter)
    SavannahCourse/                 instructor's obstacle course (tracked)
    StudentWork/                    our scene and scripts for the obstacle course
      Scenes/TeamDeadweight.unity   the performance scene
      Scripts/                      HandMotors, GestureActions, PhoneDrift, CourseCamera, DeadweightTheme, LatestUdp
    TankBattle/                     the Tank Battle mode (scene, scripts, editor tools)
  StudentTracking/
    bridge-student-changes.patch    our changes to the starter's bridge.py
    models.txt                      download links + fingerprints for the extra models
  TankBattle/
    README.md                       Tank Battle design, phases and tests
    Tracking/tank_bridge.py         lane-based tracker for Tank Battle
  DigiPhantStarter/                 instructor clone (NOT tracked)
    Tracking/
      bridge.py                     webcam + MediaPipe + UDP sender (patched)
      pose_landmarker_full.task     bundled pose model
      gesture_recognizer.task       downloaded (NOT tracked)
      efficientdet_lite0.tflite     downloaded (NOT tracked)
      .venv/                        local Python environment (NOT tracked)
  Recordings/                       performance takes (NOT tracked; upload to Drive)
```

## How the data flows

```text
webcam -> bridge.py (MediaPipe pose + gesture recognizer + phone detector,
                     performer assignment, 6 movement signals per person)
       -> UDP JSON on localhost -> Unity (DigiPhantController, GestureActions, PhoneDrift)
       -> calibration + mappings -> elephant bones, locomotion, animation clips, drift
```

The tracker alone opens the camera. It also sends a small annotated JPEG preview,
which Unity shows in the Game view. Everything stays on this machine; no camera
images are sent online.

| Port (localhost) | Owner | Purpose |
| --- | --- | --- |
| 5055 | Unity | Receives tracking JSON; sends commands back from this socket |
| 5056 | Python tracker | Sends tracking/preview; receives Unity commands (group size, mode) |
| 5057 | Unity | Receives camera preview JPEGs |
| 5058 | Unity (`GestureActions`) | Receives hand gestures per performer (**added by us**) |
| 5059 | Unity (`PhoneDrift`) | Receives phone sightings (**added by us**) |

5055–5057 match `DigiPhantStarter/INTEGRATION.md`. Tank Battle's tracker uses
the same ports, so only one mode can run at a time.

## Scenes to open

| Scene | What it is |
| --- | --- |
| `Assets/StudentWork/Scenes/TeamDeadweight.unity` | **Our performance scene:** 3 people drive one elephant through the savannah course |
| `Assets/TankBattle/Scenes/TankBattle.unity` | **Tank Battle:** 2–3 players, one elephant each, in a walled arena |
| `Assets/DigiPhant/Scenes/DigiPhant.unity` | Instructor's reference scene (has the savannah course injected; backup `DigiPhant_BeforeSavannah.unity` beside it) |

The reference scene turns off the vendor `Elephant.cs` controller, the
CharacterController and the Animator (prefab overrides); `DigiPhantLocomotion`
handles animation. In any new scene that uses `Elephant.prefab`, turn off the
vendor **Elephant** component yourself (see [Limitations](#known-limitations)).

**Switching modes:** stop Play, open the other scene, press Play. Each scene
starts its own tracker. Recalibrate after every switch.

## Running it

1. Close other apps using the webcam and turn off iPhone Continuity Camera.
   Quit any separately launched tracker (Q).
2. Open `TeamDeadweight.unity` and press **Play**. Unity starts
   `DigiPhantStarter/Tracking/.venv/bin/python bridge.py` itself and shows the
   camera preview. Allow camera access if macOS asks (it may name Unity or Python).
3. Pick **3** people, **Seated / upper body**, then **Camera**. People are
   assigned P1, P2, P3 from left to right in the unmirrored preview **once all
   three are visible**.
4. Click **Set neutral pose (10 seconds)** and hold "cactus arms" (elbows out,
   hands level with the shoulders).
5. Test one control at a time, then a group action.
6. **Stop:** press Play again. This also stops the tracker Unity started. A
   tracker you started separately keeps running; quit it with Q.

**Calibrate again** after changing group size, body mode, or after **Reassign people**.

**Testing without a camera (Camera mode):** arrow keys drive the hand motors
(↑ both hands up, ↓ both down, ← / → one hand up), **E** = eat, **F** = drink,
**D** = drift on/off, **C** = camera view, **M** = minimap.

**Recovering tracking:**
- If tracking is lost, the affected controls ease back to rest and walking stops.
  They pick up again once tracking comes back.
- If the wrong person controls a part, click **Reassign people** (or R in the
  standalone window), then set neutral again.
- If the preview shows "Camera stopped", check Continuity Camera first, then
  System Settings → Privacy & Security → Camera, then click **Retry**.

Auto-launch only works in the Editor. A built player needs the tracker started
separately.

## Additions since 10.08

Everything below was added on top of the instructor's starter. Each entry says
what it is and why we added it.

| Date | Addition | Why, in one line |
| --- | --- | --- |
| 10.08 | [Savannah course](#savannah-course) | A space for the elephant to perform in |
| 10.08 | [Travel radius 15 → 50](#travel-radius) | The elephant couldn't reach the end of the course |
| 10.08 | [Three-person control scheme](#three-person-control-scheme) | Give each performer a distinct role on one shared elephant |
| 10.08 | [Tracker: gestures and phone](#tracker-changes) | The pose model can't see hand shapes or objects |
| 10.08 | [Gesture actions: eat and drink](#gesture-actions-eat-and-drink) | Turn held hand signs into whole animations |
| 10.08 | [Phone drift](#phone-drift) | A two-person move: one person removes grip, another steers |
| 10.08 | [Hand motors](#hand-motors) | Shoulder-tilt steering interfered with hand height; now the hands do both |
| 10.09 | [Course camera](#course-camera) | A view where a left turn always looks like a left turn |
| 10.09 | [Deadweight theme](#deadweight-theme) | A readable interface with its own identity |
| 10.09 | [Tank Battle](#tank-battle) | A competitive multiplayer mode (inspired by Wii Play Tanks and Kinect) that tests the CV pipeline |

### Savannah course

- **What:** The instructor's static obstacle course (`Assets/SavannahCourse`),
  injected into the scene with **DigiPhant → Savannah → Inject Course into Open
  Scene**. The tool saved `DigiPhant_BeforeSavannah.unity` first. Layout 4, seed
  `20261005`: 18 trees, 4 landmarks, no colliders or scripts.
- **Why:** The elephant needed a world to move through, and a shared course
  gives the performance a route and a goal. **Validate Shareable Environment**
  and **Validate Generator** both passed.

### Travel radius

- **What:** `stageRadius` on *DigiPhant Locomotion* raised from 15 to 50 in the
  Inspector. No code change.
- **Why:** The injection tool warned that the course needs about 30 units of
  travel. At 15 the elephant stopped halfway.

### Three-person control scheme

- **What:** `TeamDeadweight.unity` is a copy of the reference scene set to 3
  performers in seated mode, with these roles:

  | Person | Elephant motion | Gesture |
  | --- | --- | --- |
  | P1 | Forward, backward, steering | Arms and shoulders ([hand motors](#hand-motors)) |
  | P2 | Eat | Fist, held 0.3 s |
  | P2 | Drink | Peace sign, held 0.3 s |
  | P3 | Drift | Phone held up near P3's wrist |

  The reference three-person body controls (legs, tail, head, ears, trunk)
  stay active alongside these.
- **Why:** We sketched a control matrix (person × motion × gesture) so that
  each performer has a role you can recognize. Drift was chosen for P3 over
  speed or spin because it needs two people: P3 decides when the elephant loses
  grip and P1 decides which way it slides.

### Tracker changes

- **What:** Our changes to the starter's `bridge.py`, saved as
  `StudentTracking/bridge-student-changes.patch`:
  - **Gesture Recognizer** (`gesture_recognizer.task`) on every frame. Each
    hand is matched to the nearest assigned performer's wrist (within 0.15 of
    the image width). Sent to port 5058.
  - **Object Detector** (`efficientdet_lite0.tflite`, "cell phone" only)
    about 3 times a second, matched to the nearest wrist (within 0.25). Sent to
    port 5059.
  - **2 hands per performer** (`num_hands = 2 × people`).
  - **Recovery:** if MediaPipe's gesture graph fails, the tracker restarts that
    model instead of stopping.
  - **Missing models:** if a model file is missing, that feature is turned off
    and pose tracking keeps working.
- **Why:** The pose model only knows where the wrists are, not what the fingers
  are doing, so hand signs and objects need their own models. The hand limit
  and the restart fix a live crash: `Packet isn't the sole owner of the holder`
  in the gesture model with several hands in view. Another team's working setup
  used 2 hands per person where ours used 8.

### Gesture actions: eat and drink

- **What:** `Assets/StudentWork/Scripts/GestureActions.cs`.
  - **Triggers:** P2's fist plays the elephant's **eat** clip and a peace sign
    plays **drink** (either hand, score ≥ 0.6, held 0.3 s).
  - **Once per gesture:** each gesture fires once and re-arms only after the
    gesture has been gone for 0.3 s.
  - **Head and trunk only:** the clip plays on the neck, head, trunk and ears,
    blended in and out over 0.4 s.
- **Why:**
  - **Gestures become moments:** a held sign triggers a whole behaviour
    instead of nudging a number.
  - **Head and trunk only** so the legs keep walking and P1 keeps travel
    control.
  - **The 0.3 s re-arm** fixes a bug found in review: a one-frame tracking
    dropout made eat replay by itself.

### Phone drift

- **What:** `Assets/StudentWork/Scripts/PhoneDrift.cs`. While P3's phone is
  seen (score ≥ 0.5, held through gaps of up to 0.7 s), the elephant's direction
  of travel lags behind the direction it faces (`driftGrip` 1.2 vs `normalGrip`
  15), so P1's steering makes it slide. The script runs after locomotion each
  frame and keeps the travel-radius limit.
- **Why:** Drift is faked without physics, the way arcade racing games do it.
  The elephant is moved directly each frame, so filtering its direction is
  simpler and more predictable than adding a physics body. With no steering
  from P1, the phone has no visible effect: it's designed as a team move.

### Hand motors

`Assets/StudentWork/Scripts/HandMotors.cs` replaces the walk/turn mappings with
two hand "motors" for one performer (P1 by default). It works through a small
`driveOverride` hook we added to `DigiPhantLocomotion`. Turn the component off
to go back to the Inspector mappings.

**Why:** The reference mapping steered with shoulder tilt, and that interfered
with the hands. Hand height is measured from the shoulders, so tilting your
shoulders to turn changed your hand height compared with the neutral pose,
even though your hands hadn't moved relative to your shoulders. Turning
therefore also changed walking speed. Hand motors take the shoulders out of the
controls: both walking and turning come from the hands, so each hand is one
clear, independent control.

**Neutral pose:** "cactus arms": elbows out to the sides, hands level with the
shoulders. Hold this during **Set neutral pose**. Every move below is measured
from it.

| Left hand | Right hand | Elephant |
| --- | --- | --- |
| up | up | walks forward; higher hands go faster and run |
| down | down | walks backward |
| up | not up | turns left on the spot (after `turnDelay`) |
| not up | up | turns right on the spot (after `turnDelay`) |
| near neutral | near neutral | stands still |

- `upThreshold` / `downThreshold` (default 0.35) set how far a hand must move
  from neutral. The unit is torso length (shoulder width in Seated mode).
- `fullSpeedHeight` (default 1) is the hand height for full speed. Speed ramps
  up from zero at the threshold. The elephant runs once speed passes the
  locomotion `runThreshold`.
- `turnDelay` (default 0.15 s) is how long one hand must be up on its own
  before turning starts. This stops a swerve when both hands go up or come down
  at slightly different times.
- If either hand is not tracked, the elephant stops.
- **Seated mode:** dropping the hands toward the lap can take the wrists out of
  frame. That reads as lost tracking and the elephant stops instead of reversing.
  If so, lower `downThreshold`.
- **Testing without a camera:** in **Camera** mode, hold the arrow keys. The
  status card shows `Hand motors P1: <state>`.
- **Automated check:** **DigiPhant → Validate Hand Motors** (outside Play
  mode). It offers to save open scenes first, tests on the team scene, then
  reloads it from disk.

### Course camera

`Assets/StudentWork/Scripts/CourseCamera.cs` controls the stage camera for
driving the course. Turn it off to get the original camera back.

**Why:** The starter's camera slides with the elephant but never turns, so
steering felt inconsistent with the hand motors.

| View | What you see | Why |
| --- | --- | --- |
| **Chase** (default) | High camera behind the elephant, turning with it | Up the screen is always forward, and screen-left always matches a left turn, which suits the hand motors |
| **North up** | Bird's-eye that follows the elephant but never rotates | The course always looks the same; steering looks reversed when the elephant heads down the screen |
| **Whole course** | The entire loop | Planning a route; the elephant is small |

- **C** cycles the views. **M** shows or hides the minimap: a top-down map of
  the whole course, where the orange arrow is the elephant.
- The minimap renders into a texture. As a separate on-screen camera it hid the
  control panel and camera preview.
- To tune the chase view, change `chasePitch`, `chaseDistance`, `lookAhead` and
  `turnFollow`.
- While Course Camera is on, it turns off the locomotion's own camera follow
  and puts it back when disabled.
- Known limitation: if *DigiPhant Locomotion* is turned off and on again while
  Course Camera is running, the original camera position is lost until the
  next Play session.

### Deadweight theme

`Assets/StudentWork/Scripts/DeadweightTheme.cs` restyles every DigiPhant panel
in a punk look inspired by Persona 5, using an original palette and shapes:
ink-black panels, hot-red slanted section banners and larger white text. Turn
it off to get Unity's default grey panels back.

**Why:** A readable interface the team could style. The starter's panels were
one long grey list. The theme groups them into sections, adds a status card for
what the system is reading, and gives the piece a look of its own.

- **Control panel (left):** grouped into Camera preview, Recording, Group,
  Input, Performers, Neutral pose, Movement and Body sliders.
- **Status card (top right):** gestures, drift, hand motors and camera view on
  one card. A row turns red while it is active.
- **Size:** `uiScale` (default 1.3) scales the whole interface; it can change
  during Play.
- **Header font:** Anton, in `Assets/StudentWork/Resources/Fonts/`, under the SIL
  Open Font License (`Anton-OFL.txt` beside it).
- The starter panels pick the theme up through a small hook we added,
  `Assets/DigiPhant/Runtime/DigiPhantGUI.cs`.

### Tank Battle

- **What:** A separate 2–3 player game in `Assets/TankBattle/` and
  `TankBattle/`, without changing the obstacle-course scene. Each player drives
  their own elephant with hand motors, drinks at the pond with ✌️ to load
  water, and shoots water balls with ✊. 3 hearts each, best of 3 rounds.
- **How it differs:** `TankBattle/Tracking/tank_bridge.py` splits the camera
  image into vertical lanes, and the lane you sit in is your player number.
  Each lane is tracked on its own, so nobody waits for the whole group.
- **Why:**
  - **Inspiration:** Hand motors had already given the obstacle-course
    elephant tank-style controls, so we were thinking in tank movement. That
    led to the Wii Play tank battle games and to multiplayer Xbox Kinect games:
    a multiplayer game where people **compete instead of collaborate**.
  - **A test of the pipeline:** Head-to-head play needs every player's
    controls to be responsive and reliable, so the game shows how accurate the
    computer vision pipeline really is.
  - **New gesture design problems:** Competition raised challenges the
    shared performance didn't:
    - where exactly the neutral pose should sit for the best control;
    - which gestures suit which in-game actions (✌️ to drink, ✊ to shoot);
    - how gesture inputs interfere with each other (closing a hand to shoot
      could briefly read as "one hand up" and swing the aim), and how to fix
      that (steady aim) for a seamless game.
  - **Independent players:** The obstacle course needs all three people
    present before anyone can move. Lanes let each player join and leave on
    their own.
- **Built in five tested phases:** lanes, arena, pond and ammo, shots and
  hits, rounds and HUD. Each phase has a check under the **Tank Battle** menu.
  Full design, controls and tests: [`TankBattle/README.md`](TankBattle/README.md).

## Recording

Click **Record performance**, then **Stop recording and save**. The take saves to
`<project root>/Recordings/<id>/performance.mp4` (path from
`DigiPhantRecording.cs`; the encoder uses the same `.venv`). Check the MP4 plays,
then upload it to the team Drive by hand. Nothing uploads automatically. Keep the
Game view visible during a take. See `DigiPhantStarter/RECORDING.md`.

A 2-minute solo Tank Battle test was recorded on 10.09 (500 frames, MP4
exported). The team's performance videos are linked from the [README](README.md).

## Changes made

| File / setting | Change | Reason |
| --- | --- | --- |
| `.gitignore` | Added `/DigiPhantStarter/`, `/Assets/Elephant/`, `/Assets/Elephant.meta`, `**/.venv/`, `**/__pycache__/`, `.DS_Store` | Keep heavy shared assets and local environments out of the repo (STUDENT_GIT.md). Recordings were already ignored by `/[Rr]ecordings/` |
| `Assets/Elephant`, `Assets/DigiPhant`, `Assets/SavannahCourse` (+ `.meta`) | Copied from the starter | Reference integration and course |
| `DigiPhantStarter/Tracking/.venv` | Created | Python environment for the tracker |
| `stageRadius` on *DigiPhant Locomotion* | 15 → 50 | Reach the end of the course |
| `Assets/StudentWork/Scenes/TeamDeadweight.unity` | New (copy of the reference scene) | Our 3-person performance scene |
| `Assets/StudentWork/Scripts/GestureActions.cs`, `PhoneDrift.cs`, `LatestUdp.cs` | New | Eat/drink and drift; `LatestUdp` is their shared receiver |
| `Assets/StudentWork/Scripts/HandMotors.cs`, `Assets/StudentWork/Editor/HandMotorsValidation.cs` | New | Hand motors drive scheme and its check |
| `Assets/StudentWork/Scripts/CourseCamera.cs` | New | Chase / north-up / whole-course camera and minimap |
| `Assets/StudentWork/Scripts/DeadweightTheme.cs`, `Assets/StudentWork/Resources/Fonts/` | New | Theme, status card, Anton font (OFL) |
| `Assets/DigiPhant/Runtime/DigiPhantLocomotion.cs` | Added an optional `driveOverride` hook used in Camera mode | Lets `HandMotors` supply forward and turn values |
| `Assets/DigiPhant/Runtime/DigiPhantGUI.cs` (new); GUI code in `DigiPhantController.cs`, `DigiPhantCameraPreview.cs`, `DigiPhantRecording.cs`, `DigiPhantLocomotion.cs` | Theme hook; control panel regrouped into sections and widened to 380 px | Readable, themeable UI |
| `DigiPhantStarter/Tracking/bridge.py`, `test_bridge.py` (via `StudentTracking/bridge-student-changes.patch`) | Gesture Recognizer and phone detector, ports 5058/5059, 2 hands per performer, gesture-model restart after a MediaPipe error | See [Tracker changes](#tracker-changes) |
| `Assets/TankBattle/`, `TankBattle/` | New | Tank Battle mode |

The launcher (`DigiPhantCameraPreview.cs`) and recorder (`DigiPhantRecording.cs`)
already look for `../DigiPhantStarter/Tracking/.venv/bin/python` relative to
`Application.dataPath`, which matches this layout.

## Verification

**Checked (static and automated):**
- [x] All 124 `asset-checksums.json` entries match SHA-256 (96 Elephant, 27
      DigiPhant, 1 pose model).
- [x] `pip check` is clean; packages match the supplied pins.
- [x] The Python tests pass with our patch applied (12: the 11 supplied plus one we added for the gesture-model restart).
- [x] The saved patch is identical to the live `bridge.py` changes.
- [x] Launcher and recorder paths match the project layout.
- [x] Unity compiles all scripts without errors.
- [x] Savannah course: **Validate Shareable Environment** and **Validate
      Generator** passed.

**Checked live:**
- [x] Camera permission granted; the tracker runs from Unity's Play button.
- [x] Pose tracking and the gesture model run live. The gesture crash happened
      live and led to the fix above.
- [x] Tank Battle solo test recorded, with the MP4 exported (10.09).
- [x] Team videos of people using it are on Drive (see [README](README.md)).

**Not yet verified:**
- [ ] A full three-person run of the obstacle course with every control. Our
      team reflection notes we didn't get it fully working with 3 people.
- [ ] How reliably fists, peace signs and the phone are recognized with three
      people seated close together.
- [ ] A rebuild from a fresh checkout using the steps below.

## Known limitations

- **iPhone Continuity Camera breaks the camera.** With the phone listed as a
  camera, the tracker fails its first frame reads and quits with `Camera stopped
  delivering frames.` This happened on 10.08 and again on 10.13. Turn
  Continuity Camera off (iPhone: Settings → General → AirPlay & Continuity).
- **The obstacle course waits for everyone.** With the group size at 3, nobody
  is assigned until three bodies are visible. To rehearse with fewer people,
  use a stand-in (a photo on a screen) or choose 2 people while testing.
- **Gestures overlap body controls.** Raising a hand to sign or show the phone
  also moves the rear legs (P2) or curls the trunk (P3) through the reference
  controls.
- **Drift needs steering.** Holding the phone only removes grip; it's visible
  only while P1 turns or stops.
- **One mode at a time.** Both trackers use the same ports.
- **Vendor demo scene throws errors under the new Input System.**
  `Assets/Elephant/Scenes/SampleScene.unity` has `Elephant.cs` turned on, which
  uses legacy `Input`. Don't play that scene. Our scenes aren't affected.
- Tracking is 2D and relative to the torso. Crossing paths, people blocking each
  other and bystanders in view can confuse performer assignment.
- There is no foot-planting IK. Feet can slide while walking.

## Rebuild from a fresh checkout (macOS)

Run from the root of a fresh clone of this repository.

```sh
# 1. Instructor starter at the recorded commit, beside Assets
git clone https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
git -C DigiPhantStarter checkout 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f

# 2. Restore the elephant with its original .meta files.
#    Do NOT copy DigiPhant or SavannahCourse: they are tracked in this repo.
cp -R DigiPhantStarter/Elephant Assets/Elephant
cp DigiPhantStarter/Elephant.meta Assets/Elephant.meta

# 3. Apply our tracker changes and download the two extra models
git -C DigiPhantStarter apply ../StudentTracking/bridge-student-changes.patch
curl -L -o DigiPhantStarter/Tracking/gesture_recognizer.task \
  https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task
curl -L -o DigiPhantStarter/Tracking/efficientdet_lite0.tflite \
  https://storage.googleapis.com/mediapipe-models/object_detector/efficientdet_lite0/int8/1/efficientdet_lite0.tflite
shasum -a 256 DigiPhantStarter/Tracking/*.task DigiPhantStarter/Tracking/*.tflite
# compare with StudentTracking/models.txt

# 4. Python environment (Python 3.14; on macOS: brew install python@3.14)
python3.14 -m venv DigiPhantStarter/Tracking/.venv
DigiPhantStarter/Tracking/.venv/bin/python -m pip install -r DigiPhantStarter/Tracking/requirements.txt
DigiPhantStarter/Tracking/.venv/bin/python -m pip check
DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s DigiPhantStarter/Tracking -p 'test_*.py'

# 5. Pose model is already in the clone; confirm its fingerprint
shasum -a 256 DigiPhantStarter/Tracking/pose_landmarker_full.task
# expected: 4eaa5eb7a98365221087693fcc286334cf0858e2eb6e15b506aa4a7ecdcec4ad
```

6. Open the folder in Unity Hub with Unity 6000.6.3f1, wait for import, then
   open `Assets/StudentWork/Scenes/TeamDeadweight.unity`, or
   `Assets/TankBattle/Scenes/TankBattle.unity` for Tank Battle.

If `bridge.py` changes again, regenerate the patch so the repo keeps the change:
`git -C DigiPhantStarter diff > StudentTracking/bridge-student-changes.patch`.

## Changelog

- `2026-10-08`: Initial setup recorded. Starter cloned at `4e6dab5`, assets
  imported unmodified, Python 3.14 venv created, static checks pass.
- `2026-10-08`: Savannah course injected; travel radius 15 → 50.
- `2026-10-08`: Three-person control scheme: `TeamDeadweight.unity`,
  `GestureActions` (eat/drink), `PhoneDrift`, gesture recognizer and phone
  detector in the tracker. Fixed eat/drink replaying after a dropout.
- `2026-10-08`: Gesture crash fixed (2 hands per performer, model restart). Added
  hand motors (`HandMotors.cs`, locomotion `driveOverride` hook).
- `2026-10-09`: Added Course Camera (chase view by default, minimap).
- `2026-10-09`: The minimap now renders to a texture. As a separate on-screen
  camera it hid the control panel and camera preview. Added Deadweight theme
  and regrouped the control panel.
- `2026-10-09`: Built Tank Battle in five phases in `Assets/TankBattle/` and
  `TankBattle/`. Recorded a 2-minute solo test. See `TankBattle/README.md`.
- `2026-10-13`: Camera failure traced to iPhone Continuity Camera again; added
  to Known limitations.
- `2026-10-16`: This document updated to cover everything added since 10.08.
