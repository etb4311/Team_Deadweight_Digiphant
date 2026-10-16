# Tank Battle (in development)

A 2–3 player mini-game: each player drives their own elephant with the hand-motor
controls, drinks from the pond to fill up with water (✌️ Victory), and shoots water balls
from the trunk (✊ Fist). It's built in phases, and each phase is tested before the next.

**Kept separate from the regular demo.** Tank Battle code lives only in this folder (Python)
and, from phase 2, in `Assets/TankBattle/` (Unity). It does not change
`DigiPhantStarter/Tracking/bridge.py` or the existing scenes and scripts. The tank bridge
imports helper functions from `bridge.py` without modifying it.

## Agreed design

| Area | Decision |
| --- | --- |
| Mode | Separate `TankBattle` scene, opened from the regular panel; the 1–4 performer modes stay untouched |
| Players | 2 or 3. The camera image is split into equal vertical lanes, and the lane you stand in is your player number |
| Camera | One overhead view of the whole arena |
| Arena | New walled arena, 44 × 30 (an elephant is ~4 m across, so 36 × 24 was too cramped), with a central pond, cover and colour-coded spawns |
| Drive | Hand motors per player, with collisions |
| Reload | Hold ✌️ in the pond: a ~2 s drink that can't be interrupted fills all 3 shots |
| Shoot | Hold ✊: the attack animation fires a water ball the way you face |
| Hits | 3 hearts. Shots bounce once and can only hit the shooter after bouncing. Knocked out at 0 |
| Match | Lobby, then neutral pose, then 3-2-1, last elephant standing, best of 3 |

Phases:
1. Camera lanes ✅
2. Arena + keyboard-driven elephants ✅
3. Pond, drinking and ammo ✅
4. Water shots, hits and knockouts ✅
5. Rounds and HUD ✅ (built; awaiting your Play-mode test)
6. Mode switching and a live test

## Phase 1: camera lanes

`Tracking/tank_bridge.py` replaces the regular bridge while you play Tank Battle.

- The image is split into 2 or 3 equal vertical lanes, counted left to right **as the
  unmirrored preview shows them**. P1 is the left lane in the preview, which is the
  right-hand seat from the players' side.
- **Each lane is independent.** A player is picked up as soon as they're clearly inside
  their lane; nobody waits for the whole group. If one player leaves, only their elephant
  stops.
- **Lane edges:** a newcomer must be clearly inside a lane, not standing on a line, so
  someone near a boundary doesn't flicker between two players.
- **Preview:** lane lines, a P-label per lane (`step in` when empty), and each player's
  skeleton in their colour: P1 red, P2 blue, P3 yellow. Recognised gestures appear at the
  wrists.
- **Same messages as the regular bridge.** The same ports and packet format, so Unity's
  controller, camera preview and gesture readers work unchanged.
- **Gesture crash:** the gesture recogniser restarts itself if MediaPipe crashes, as in
  the regular bridge.

**Seated play is recommended.** Three people side by side need a wide shot; standing,
that's about 3 m from the camera. Seated mode (`--upper-body-only`) measures hand height
from the shoulders.

### Test it now (no new Unity code needed)

From the project root:

```sh
DigiPhantStarter/Tracking/.venv/bin/python TankBattle/Tracking/tank_bridge.py --players 3 --upper-body-only
```

1. Sit side by side and check the preview window: each person should be labelled with
   their lane's P-number and colour. Then try these:
   - one person leaves; only their label should change to `step in`;
   - someone sits on a lane line; they shouldn't be picked up;
   - two people swap seats; they swap player numbers.
2. **Optional, with Unity:** keep the tank bridge running, open `TeamDeadweight.unity`,
   choose group size **3**, **Seated** and **Camera**, then press Play.
   - Because the tank bridge is already sending pictures, Unity doesn't start the
     regular bridge.
   - The panel shows P1–P3 as visible or lost per lane, and the status card shows each
     player's gestures.
   - The single demo elephant still follows its normal mapping; the per-player elephants
     come in phase 2.
3. Press **Q** in the preview window to quit and **R** to reset the lanes.

**Watch for gesture crashes.** If `GestureRecognizer failed; restarting it.` appears often
while 3 people are in view, note roughly how often. The fallback is to recognise gestures
separately in each lane.

### Tests

```sh
cd TankBattle/Tracking && ../../DigiPhantStarter/Tracking/.venv/bin/python -m unittest test_tank_bridge
```

## Phase 2: arena and elephants

Everything is in `Assets/TankBattle/`. Nothing outside that folder is changed: the elephant
prefab, clips and DigiPhant scripts are only read.

| File | What it does |
| --- | --- |
| `Scripts/TankControls.cs` | Hand-motor rules (a copy, independent of Hand Motors) and each player's test keys |
| `Scripts/TankElephant.cs` | One player's elephant: driving, collisions, gait animation, P-label |
| `Scripts/TankArenaCamera.cs` | Fixed overhead view that fits the whole arena |
| `Editor/TankBattleBuilder.cs` | **Tank Battle → Build Arena Scene** and **Tank Battle → Validate Arena (Phase 2)** |

**Build the scene once:** **Tank Battle → Build Arena Scene**. It creates
`Assets/TankBattle/Scenes/TankBattle.unity`, with its materials in `Assets/TankBattle/Generated/`.
Running it again regenerates the tank scene only.

**The scene contains:**
- **Arena:** 44 × 30 with 2 m walls.
- **Pond:** in the centre. You can walk into it; its trigger zone is used for drinking from
  phase 3.
- **Cover and scenery:** log piles and bushes copied from the Savannah course models,
  plus stone boulders. Acacia trees stand outside the walls, and the ground, walls and
  water use the Savannah course materials. The Savannah prefab is only read, never changed.
- **Elephants:** three, P1 red, P2 blue and P3 yellow, each on a coloured ring with a P-label,
  spawning round the pond.
- **Input:** a DigiPhant controller set to 3 players, Seated and Camera, with the Deadweight
  theme. **The lane tracker starts automatically** in Play mode (`TankBridgeLauncher`) and
  restarts within 5 s if it stops. The status card shows a **Tracker** row. The regular bridge
  is never started in this scene.
- **Calibration:** the controller lists each player's two hands as required inputs, so
  **Set neutral pose** waits until everyone's hands are visible.

### Test with the keyboard

Open `TankBattle.unity` and press Play.

| Player | Move | Run | (Drink, phase 3) | (Shoot, phase 4) |
| --- | --- | --- | --- | --- |
| P1 | W A S D | Left Shift | Q | E |
| P2 | I J K L | Y | U | O |
| P3 | Arrow keys | Right Shift | , | . |

These behave like the hands:
- forward = both hands up;
- back = both down;
- left or right = one hand up, which turns on the spot after a short pause.

Check that elephants:
- stop at walls, cover and each other;
- turn on the spot;
- animate while walking, running and turning.

### Test with the camera

1. Open `TankBattle.unity` and press Play. The lane tracker starts by itself; allow camera
   access if macOS asks.
2. If the preview stays empty, read the **Tracker** row on the status card and any
   `Tank tracker:` warning in the Console.
3. Wait until P1–P3 show as visible, then click **Set neutral pose** while everyone holds
   cactus arms.
4. Each lane drives its own elephant. Keys still override a player while held.

### Testing on your own

- **Keyboard only:** drive any elephant with its keys; the others just stand still.
- **Camera, solo:**
  1. Press Play, then **F1** (solo test on). **No lanes in solo:** whoever is nearest the
     middle of the picture drives the chosen player, so sit anywhere in front of the laptop.
     - The preview reads `SOLO TEST: whole picture drives P1`.
     - The status card shows `Solo test: P1 · whole picture`.
     - **F2** picks P2 or P3 instead.
  2. Click **Set neutral pose**. In solo mode it only waits for your hands.
  3. Your hands drive that elephant, and the other elephants stay on their keys.
  4. **F1** again turns solo off: the lanes come back, and calibration needs every player.
  - Starting the tracker by hand with `--solo 1` does the same from the command line.
- Solo test only changes the tank scene's controller while playing; stopping Play restores it.

### Panel notes for this scene

- **Group size:** keep it at **3**, or **2** for two players. Choosing 1 or 4 reassigns the
  players' calibration inputs; rebuild the scene to restore them. The tank bridge only
  accepts 2 or 3.
- **Movement section:** leave **Enable locomotion** off. It belongs to the single demo
  elephant, which isn't in this scene; the tanks move themselves.

### Automated check

**Tank Battle → Validate Arena (Phase 2)** (outside Play mode) checks that:
- the vendor script is gone;
- walking and turning on the spot work;
- an elephant can't drive through a wall or another elephant.

It reloads the scene afterwards and logs `Tank Battle phase 2 validation passed.`

## Phases 3–4: drinking, water shots and hits

| Rule | Value (Inspector on each elephant) |
| --- | --- |
| Water tank | 3 shots (`maxWater`); full at the start |
| Drink | ✌️ Victory (or the drink key) **inside the pond**: about 2.2 s, standing still, then a full tank. Refused outside the pond or when already full |
| Shoot | ✊ Fist (or the shoot key): the trunk swings and a water ball flies the way the elephant faces. Uses 1 water; 0.6 s between shots. You can shoot while moving |
| Steady aim | While a fist is showing, and for 0.4 s after a shot, the elephant **stops turning**; it can still walk. Closing your hands can briefly read as one hand up, which used to swing the aim. Turn it off per elephant with `steadyAim` |
| Water ball | In the **shooter's colour** (ball, trail and splash). 14 m/s at 1.5 m high, so it hits elephants, walls, log piles, bushes and boulders. **Bounces twice** (`shotBounces`), then splashes at the next surface |
| Own shots | Can't hit you on the way out, but **can after a bounce**. Firing point-blank into a wall bounces straight back into you |
| Point-blank | The ball starts inside the shooter, so a wall or elephant right in front of the trunk is always hit; it can't be shot through |
| Hits | 3 hearts. After a hit, 1 s of grace: balls still splash on the elephant but do no damage |
| Knockout | At 0 hearts: the die animation plays, and the elephant can't move or be hit. **Practice mode:** it comes back at its spawn after 5 s (`reviveSeconds`; phase 5 replaces this with rounds) |

**Gestures:** hold the sign for about 0.3 s. Each hold fires once; lower or change your hand
to fire again.

**Above each elephant:** red squares are hearts, blue squares are water. Empty squares
are dark. Short messages appear underneath, such as "Go to the pond to drink", "Out of
water — drink at the pond" or "Hit by P2!".

### Test on your own

1. **Tank Battle → Build Arena Scene**: rebuild to add the pond, gestures and clips.
2. **Tank Battle → Validate Combat (Phases 3-4)** should log `combat validation (phases 3-4) passed`.
   It checks:
   - shooting and ammo use;
   - the shot cooldown;
   - hits and knockout;
   - drinking: refused outside the pond, standing still, refilling;
   - the practice revive;
   - the bounce, including hitting your own shooter after it;
   - a shot stopping at its second wall.
3. **Keyboard:** press Play, drive P1 (WASD) towards P2 and press **E** to shoot.
   - Fire 3 shots, then drive into the pond and press **Q** to drink.
   - Shoot a wall at an angle to watch the bounce.
   - P2 (**U** drink, **O** shoot) and P3 (**,** and **.**) work the same.
4. **Gestures, solo:**
   1. Press **F1** (solo test), sit anywhere in front of the camera and set the neutral pose.
   2. Drive with your hands.
   3. Hold ✊ to shoot at a keyboard-driven elephant.
   4. Drive into the pond and hold ✌️ to drink.
   - The preview prints the gesture it sees at your wrist, so you can tell whether it's
     recognised.

## Phase 5: rounds and HUD

`Scripts/TankMatch.cs` runs the match; it sits on **DigiPhant Controls**.

| Stage | What happens |
| --- | --- |
| **Practice** (when Play starts) | Free play for warming up and calibrating: drive, shoot, drink. Knocked-out elephants come back after 5 s |
| **Start** | **Space** (or Enter), or the **Start match** button. Everyone returns to their spawn with full hearts and water |
| **Countdown** | 3-2-1-GO. Everyone is frozen: no driving, drinking or shooting |
| **Round** | No revives. The last elephant standing wins the round; if the last ones go down together, it's a **draw** (no point) |
| **Between rounds** | The winner's banner for 3 s, then the next countdown |
| **Match** | First to **2 round wins** (`winsNeeded`). Then **Space** or **New match** starts again |
| **Backspace** | Back to practice at any time; wins are cleared |

- **Players:** the panel's group size decides who plays. With **2**, P3's elephant is removed; with
  **3**, it comes back. It only changes in practice, never mid-match.
- **HUD:**
  - **Banner:** shows `PRACTICE`, the countdown, `P2 WINS ROUND 1`, `DRAW!` or
    `P1 WINS THE MATCH!` along the top of the arena view, left of the status card.
  - **Player cards:** one per player along the bottom. Each shows the colour stripe, the
    P-number with round-win stars, hearts and water, and a status line (input source,
    `Drinking...`, `Out of water: go drink!` or `OUT`).
  - **Status card:** gains a **Match** row.
- **Settings** on Tank Match: `winsNeeded`, `countdownSeconds`, `roundOverSeconds` and `practiceReviveSeconds`.

### Test

1. **Tank Battle → Build Arena Scene** (adds Tank Match).
2. **Tank Battle → Validate Match (Phase 5)**. It checks:
   - the countdown freeze;
   - round wins;
   - no revives during a round;
   - the match win;
   - waiting after the match;
   - going back to practice;
   - practice revives;
   - draws;
   - the 2-player group size.
3. **Play:** press **Space**, then knock the other elephants out with the keyboard (P1: WASD + E) and
   watch the banners, stars and cards. Solo test (**F1**) works the same, with your hands on one elephant.
