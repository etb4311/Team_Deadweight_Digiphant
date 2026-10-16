# DigiPhant setup — Team Deadweight

What was actually set up in this Unity project, what has been checked, and how to
rebuild it from a fresh checkout. Follows the starter's
`DigiPhantStarter/AGENT_SETUP.md` and `DigiPhantStarter/STUDENT_GIT.md`.

**Status (2026-10-08):** installed and statically checked. No live checks
(Unity Play, camera, performers, recording) have been run yet. See
[Verification](#verification).

## Environment

| Item | Value |
| --- | --- |
| OS | macOS 26.6.2, Apple Silicon (arm64) |
| Unity | 6000.6.3f1, fresh Universal 3D (URP) template project |
| Render pipeline | URP 17.6.0 |
| Input | Active Input Handling = Input System Package (New) only (`activeInputHandler: 1`) |
| Python | 3.14.8, Homebrew `python@3.14` (`/opt/homebrew/bin/python3.14`) |
| Python environment | `DigiPhantStarter/Tracking/.venv` |
| Python packages | mediapipe 0.10.35, opencv-contrib-python 4.14.0.94 (supplied pins, unchanged), numpy 2.5.3; `pip check` clean |
| Camera index | 0 (default, not yet confirmed live) |
| Group size / mappings | Reference defaults; group has not chosen yet |

Python 3.14 was installed through Homebrew because only the system Python 3.9.6
was present, and that is too old for the pinned packages.

## Starter and layout

| Item | Value |
| --- | --- |
| Starter repository | https://github.com/kommanderpi/studentstarter.git |
| Starter commit | `4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f` |
| Clone location | `DigiPhantStarter/` beside `Assets/` (the launcher expects this) |
| Imported assets | `Assets/Elephant` + `Assets/Elephant.meta`, `Assets/DigiPhant` + `Assets/DigiPhant.meta`, copied unmodified |
| Not imported | `SavannahCourse` (put off for now by the team; optional, can be added later) |

```text
Team_Deadweight_Digiphant/          (project root, this repository)
  Assets/
    DigiPhant/                      reference scripts + scene (tracked, unmodified)
    Elephant/                       vendor elephant (NOT tracked; restore from starter)
    StudentWork/                    our scenes/scripts go here (not created yet)
  DigiPhantStarter/                 instructor clone (NOT tracked)
    Tracking/
      bridge.py                     webcam + MediaPipe + UDP sender
      pose_landmarker_full.task     bundled pose model
      .venv/                        local Python environment (NOT tracked)
  Recordings/                       performance takes (NOT tracked; upload to Drive)
```

## How the data flows

```text
webcam -> bridge.py (MediaPipe pose, performer assignment, 6 movement signals per person)
       -> UDP JSON on localhost -> Unity receiver (DigiPhantController)
       -> calibration + mappings -> elephant bone rotations / DigiPhantLocomotion
```

The bridge alone opens the camera. It also sends a small annotated JPEG preview,
which Unity shows in the Game view. Everything stays on this machine; no camera
images are sent online.

| Port (localhost) | Owner | Purpose |
| --- | --- | --- |
| 5055 | Unity | Receives tracking JSON; sends commands back from this socket |
| 5056 | Python bridge | Sends tracking/preview; receives Unity commands (group size, mode) |
| 5057 | Unity | Receives camera preview JPEGs |

These match `DigiPhantStarter/INTEGRATION.md`.

## Scene to open

`Assets/DigiPhant/Scenes/DigiPhant.unity` (the reference scene).

**Duplicate it into `Assets/StudentWork/` before editing**, so the original stays
as a working reference. Put new scripts in `Assets/StudentWork/` as well.

The reference scene turns off the vendor `Elephant.cs` controller, the
CharacterController and the Animator (prefab overrides); `DigiPhantLocomotion`
handles animation. In any new scene that uses `Elephant.prefab`, turn off the
vendor **Elephant** component yourself (see [Limitations](#known-limitations)).

## Running it

1. Close other apps using the webcam. Quit any separately launched bridge (Q).
2. Open the scene and press **Play**. In the Editor, Unity starts
   `DigiPhantStarter/Tracking/.venv/bin/python bridge.py` itself and shows the
   camera preview. Allow camera access if macOS asks (it may name Unity or Python).
3. Pick the group size (**1** to test alone, **3** for the group), **Full body**
   or **Seated / upper body**, then **Camera**. People are assigned P1, P2, P3
   from left to right in the unmirrored preview once that many are visible.
4. Click **Set neutral pose (10 seconds)**, get into position and hold still.
5. Test one control at a time, then a group action.
6. **Stop:** press Play again. This also stops the bridge Unity started. A bridge
   you started separately keeps running; quit it with Q.

**Calibrate again** after changing group size, body mode, or after **Reassign people**.

**Recovering tracking:**
- If tracking is lost, the affected controls ease back to rest, and walking/turning
  stops. They pick up again once tracking comes back.
- If the wrong person controls a part, click **Reassign people** (or R in the
  standalone window), then set neutral again.
- If the preview shows "Camera stopped", check System Settings → Privacy &
  Security → Camera, close any old tracking window, then click **Retry**.
- If the wrong camera opens, try camera index 1 (the `cameraIndex` field on
  *DigiPhant Camera Preview*, or `--camera 1` for the standalone bridge).

Auto-launch only works in the Editor. A built player needs the bridge started
separately.

**Default 3-person roles (reference):** P1 front legs and walk/turn (left hand
height, lean); P2 rear legs and tail; P3 head, trunk and ears. Full details are in
`Assets/DigiPhant/README.md`.

## Hand motors (team drive scheme)

`Assets/StudentWork/Scripts/HandMotors.cs` replaces the walk/turn mappings with
two hand "motors" for one performer (P1 by default). Add it to the object that
has *DigiPhant Controller* and *DigiPhant Locomotion* (**Add Component → Hand
Motors**). Turn the component off to go back to the Inspector mappings.

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
- Calibration checks that the hands named in the *forward* mapping are visible,
  not the HandMotors performer's hands. Keep that mapping on the same performer.
- **Testing without a camera:** in **Camera** mode, hold the arrow keys:
  ↑ both up, ↓ both down, ← left up, → right up. The top-right label shows
  `Hand motors P1: <state>`. The arrow keys don't work in Test sliders mode.
- **Automated check:** **DigiPhant → Validate Hand Motors** (outside Play
  mode). It offers to save open scenes first. It then tests on the team scene
  and reloads it from disk, which throws away the test changes.

## Course camera

`Assets/StudentWork/Scripts/CourseCamera.cs` controls the stage camera for
driving the course. Add it to **DigiPhant Controls** (**Add Component → Course
Camera**) in the team scene and save. Turn it off to get the original camera
back.

| View | What you see | Why |
| --- | --- | --- |
| **Chase** (default) | High camera behind the elephant, turning with it | Up the screen is always forward, and screen-left always matches a left turn, which suits the hand motors |
| **North up** | Bird's-eye that follows the elephant but never rotates | The course always looks the same; steering looks reversed when the elephant heads down the screen |
| **Whole course** | The entire loop | Planning a route; the elephant is small |

- **C** cycles the views. **M** shows or hides the minimap in the bottom-right
  corner: a top-down map of the whole course, where the orange arrow is the
  elephant and points the way it faces. The minimap is hidden in the
  whole-course view, and the arrow disappears if the elephant leaves the
  mapped area.
- The current view is shown in the top-right labels as `Camera: <view>`.
- To tune the chase view, change `chasePitch` (how steep), `chaseDistance`,
  `lookAhead` (how far ahead it aims) and `turnFollow` (how fast it swings
  round). The minimap and whole-course view show `courseHalfSize` units around
  the course centre.
- While Course Camera is on, it turns off the locomotion's own camera follow
  and puts it back when disabled.
- Known limitation: if *DigiPhant Locomotion* is turned off and on again while
  Course Camera is running, the original camera position is lost until the
  next Play session.

## Deadweight theme (UI look)

`Assets/StudentWork/Scripts/DeadweightTheme.cs` restyles every DigiPhant panel
in a Shibuya-punk look inspired by Persona 5. It uses an original palette and
shapes only: ink-black panels, hot red slanted section banners, and larger white
text. Add it to **DigiPhant Controls** (**Add Component → Deadweight Theme**)
and save. Turn it off to get Unity's default grey panels back.

- **Control panel (left):** grouped into sections: Camera preview, Recording,
  Group, Input, Performers, Neutral pose, Movement and Body sliders. The buttons
  and behaviour are the same as before.
- **Status card (top right):** gestures, drift, hand motors and camera view
  share one card. A row turns red while it is active, e.g. a gesture playing or
  the elephant turning. Key hints sit at the bottom. Without the theme, each
  script shows its old plain label instead. When the control panel is hidden,
  the card moves to the top left so the camera preview can use the top right.
- **Minimap:** gets a red frame and a `MAP` tag.
- **Buttons:** dark with a red edge when available, dark red on hover, and white
  while pressed. In the option rows (group size, body mode, input), the chosen
  option is solid red with a white shadow and underline, and the other options
  have grey text.
- **Outlined text:** all light text has a black border so it stands out.
  `outlineWidth` (default 2 px, 0 turns it off) and `outline` set its thickness
  and colour.
- **Size:** `uiScale` (default 1.3) scales everything together: text, buttons,
  banners, checkboxes, sliders, the status card and the panel width. You can
  change it during Play; the UI rebuilds on the next frame.
- **Tuning in the Inspector:** colours (`ink`, `red`, `paper`, `muted`),
  `bodySize` (15 px before scaling) and the key-hint text.
- **Header font:** Anton, in `Assets/StudentWork/Resources/Fonts/`. It is
  licensed under the SIL Open Font License; see `Anton-OFL.txt` beside it.
- The starter panels pick the theme up through a small hook,
  `Assets/DigiPhant/Runtime/DigiPhantGUI.cs`. With no theme, they look as
  before apart from the new section grouping.

## Recording

Click **Record performance**, then **Stop recording and save**. The take saves to
`<project root>/Recordings/<id>/performance.mp4` (path from
`DigiPhantRecording.cs`; the encoder uses the same `.venv`). Check the MP4 plays,
then upload it to the team Drive by hand. Nothing uploads automatically. Keep the
Game view visible during a take. See `DigiPhantStarter/RECORDING.md`.

## Changes made

| File / setting | Change | Reason |
| --- | --- | --- |
| `.gitignore` | Added `/DigiPhantStarter/`, `/Assets/Elephant/`, `/Assets/Elephant.meta`, `**/.venv/`, `**/__pycache__/` | Keep heavy shared assets and local environments out of the repo (STUDENT_GIT.md). Recordings were already ignored by `/[Rr]ecordings/`. |
| `Assets/Elephant`, `Assets/DigiPhant` (+ `.meta`) | Copied from the starter, unmodified | Reference integration |
| `DigiPhantStarter/Tracking/.venv` | Created | Python environment for the bridge |
| `Assets/DigiPhant/Runtime/DigiPhantLocomotion.cs` | Added an optional `driveOverride` hook used in Camera mode | Lets `HandMotors` supply forward and turn values |
| `Assets/StudentWork/Scripts/HandMotors.cs`, `Assets/StudentWork/Editor/HandMotorsValidation.cs` | New | Hand motors drive scheme and its check |
| `Assets/StudentWork/Scripts/CourseCamera.cs` | New | Overhead chase / north-up / whole-course camera and minimap |
| `Assets/StudentWork/Scripts/DeadweightTheme.cs`, `Assets/StudentWork/Resources/Fonts/` | New | Punk UI theme, status card, Anton font (OFL) |
| `Assets/DigiPhant/Runtime/DigiPhantGUI.cs` (new); `DigiPhantController.cs`, `DigiPhantCameraPreview.cs`, `DigiPhantRecording.cs`, `DigiPhantLocomotion.cs` GUI code | Theme hook; control panel regrouped into sections and widened to 380 px | Readable, themeable UI |
| `DigiPhantStarter/Tracking/bridge.py` (via `StudentTracking/bridge-student-changes.patch`) | Gesture recognizer is rebuilt after a MediaPipe graph error instead of stopping the bridge | MediaPipe sometimes failed with "Packet isn't the sole owner of the holder" |

Apart from the rows above, no scripts or project settings were changed. The launcher
(`DigiPhantCameraPreview.cs:97-99`) and recorder (`DigiPhantRecording.cs:36-37`)
already look for `../DigiPhantStarter/Tracking/.venv/bin/python` relative to
`Application.dataPath`, which matches this layout.

Some uncommitted changes that Unity generated were already there before setup
(HubForceResolve self-delete, ProjectSettings cloud link, QualitySettings bump).
They were left alone. **Nothing has been committed yet.**

## Verification

**Verified (static checks and automated tests):**
- [x] All 124 `asset-checksums.json` entries match SHA-256 (96 Elephant, 27
      DigiPhant, 1 pose model). A reviewer checked this separately.
- [x] `pip check` is clean; packages match the supplied pins.
- [x] The 11 supplied Python unit tests pass.
- [x] `bridge.py --help` runs.
- [x] Launcher and recorder paths match the project layout.
- [x] Reference scene GUID references resolve, and material shaders are URP Lit.

**Not yet verified (live checks still to do):**
- [ ] Unity import and a clean compile (the editor needs a refresh first).
- [ ] Opening the reference scene; elephant renders; Test sliders move the rig.
- [ ] Granting macOS camera permission, plus a standalone bridge test from the
      project root (click the preview window, then **Q** quits, **R** resets IDs):
      ```sh
      DigiPhantStarter/Tracking/.venv/bin/python DigiPhantStarter/Tracking/bridge.py --people 1 --upper-body-only
      ```
- [ ] Camera index 0 is the correct camera.
- [ ] Play mode: auto-launch, preview, performer assignment, neutral calibration,
      one elephant response, losing and recovering tracking.
- [ ] Three-person test with the group's chosen mappings.
- [ ] Recording a take, MP4 export and playback with camera and elephant visible,
      then uploading to Drive.
- [ ] First commit and a reconstruction test in a fresh checkout (steps below).

## Known limitations

- **Vendor demo scene throws errors under the new Input System.**
  `Assets/Elephant/Scenes/SampleScene.unity` has `Elephant.cs` turned on. That
  script uses legacy `Input`, so it throws `InvalidOperationException` in Play
  when only the new Input System is active. Don't play that scene, or switch
  Active Input Handling to **Both** (the team's decision). The reference DigiPhant
  scene isn't affected.
- In custom scenes using `Elephant.prefab`, turn off the vendor **Elephant**
  component. It causes the same error and fights the rig driver.
- Tracking is 2D and relative to the torso. Crossing paths, people blocking each
  other, and bystanders in view can confuse performer assignment.
- There is no foot-planting IK. Feet can slide while walking.
- Only one DigiPhant Play session can use the ports at a time.

## Rebuild from a fresh checkout (macOS)

Run from the root of a fresh clone of this repository.

```sh
# 1. Instructor starter at the recorded commit, beside Assets
git clone https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
git -C DigiPhantStarter checkout 4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f

# 2. Restore the elephant with its original .meta files.
#    Do NOT copy DigiPhant: it is tracked in this repo (keeps any adaptations).
cp -R DigiPhantStarter/Elephant Assets/Elephant
cp DigiPhantStarter/Elephant.meta Assets/Elephant.meta

# 3. Python environment (Python 3.14; on macOS: brew install python@3.14)
python3.14 -m venv DigiPhantStarter/Tracking/.venv
DigiPhantStarter/Tracking/.venv/bin/python -m pip install -r DigiPhantStarter/Tracking/requirements.txt
DigiPhantStarter/Tracking/.venv/bin/python -m pip check
DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s DigiPhantStarter/Tracking -p 'test_*.py'

# 4. Pose model is already in the clone; confirm its fingerprint
shasum -a 256 DigiPhantStarter/Tracking/pose_landmarker_full.task
# expected: 4eaa5eb7a98365221087693fcc286334cf0858e2eb6e15b506aa4a7ecdcec4ad
```

5. No tracking-code changes to restore (none were made).
6. Open the folder in Unity Hub with Unity 6000.6.3f1, wait for import, then open
   `Assets/DigiPhant/Scenes/DigiPhant.unity` (or our `Assets/StudentWork` scene
   once we have one).

If we later change `bridge.py` or another file inside the ignored clone, save a
patch in this repo (`git -C DigiPhantStarter diff > StudentTracking/bridge.patch`)
and add an "apply patch" step here.

## Changelog

- `2026-10-08`: Initial setup recorded. Starter cloned at `4e6dab5`, assets
  imported unmodified, Python 3.14 venv created, static checks pass, live checks
  still to do.
- `2026-10-08`: Added hand motors drive scheme (`HandMotors.cs`, locomotion
  `driveOverride` hook). The bridge now recovers from gesture recognizer
  crashes; the bridge patch was regenerated.
- `2026-10-09`: Added Course Camera (overhead chase default, minimap).
- `2026-10-09`: The minimap now renders to a texture. As a separate on-screen
  camera it hid the control panel and camera preview. Added Deadweight theme
  and regrouped the control panel.
- `2026-10-09`: Started Tank Battle in the separate `TankBattle/` folder, which
  doesn't touch the regular demo. Phase 1 adds camera lanes in
  `TankBattle/Tracking/tank_bridge.py`. See `TankBattle/README.md`.
