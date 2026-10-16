"""DigiPhant TANK BATTLE camera bridge: lane mode for 2-3 independent players.

The camera image is split into equal vertical lanes (unmirrored, left to right). Whoever stands in
lane N is player N, and each lane is tracked on its own: one player stepping out only freezes their
own elephant. Packets use the same ports and format as the regular bridge, so Unity's controller,
preview and gesture readers work unchanged.

The regular bridge (DigiPhantStarter/Tracking/bridge.py) is imported for its helpers only; it is
never modified. Run with the starter's Python environment, for example from the project root:
    DigiPhantStarter/Tracking/.venv/bin/python TankBattle/Tracking/tank_bridge.py --players 3 --upper-body-only
"""
import argparse
import contextlib
import errno
import json
import math
from pathlib import Path
import socket
import sys
import time

STARTER = Path(__file__).resolve().parents[2] / 'DigiPhantStarter' / 'Tracking'
sys.path.insert(0, str(STARTER))
from bridge import (GESTURE_MODEL, RestartingTask, encode_preview, extract_features,  # noqa: E402
                    nearest_wrist, send_quietly)

PLAYER_COLORS = [(60, 60, 230), (230, 140, 40), (40, 200, 240)]  # BGR: red, blue, yellow


class LaneTracker:
    """Fixed vertical lanes: each lane follows at most one body, independently of the others.

    A newcomer must stand clearly inside a lane (outside the dead band at its edges), so someone
    walking along a boundary does not flicker between players. A body already being followed may
    drift into the dead band as long as it stays in its lane and moves less than max_jump per frame.
    """
    def __init__(self, count, dead_band=.03, max_jump=.2):
        if count not in (2, 3):
            raise ValueError('Tank battle supports 2 or 3 players')
        self.count, self.dead_band, self.max_jump = count, dead_band, max_jump
        self.positions = [None] * count

    def reset(self):
        self.positions = [None] * self.count

    def bounds(self, lane):
        return lane / self.count, (lane + 1) / self.count

    def assign(self, centers):
        """centers: (x, y) per detected body in normalised image coordinates -> {player: index}."""
        result, taken = {}, set()
        for lane in range(self.count):
            low, high = self.bounds(lane)
            previous = self.positions[lane]
            followed = [i for i, c in enumerate(centers) if i not in taken and low <= c[0] < high
                        and previous is not None and math.dist(c, previous) <= self.max_jump]
            if followed:
                best = min(followed, key=lambda i: math.dist(centers[i], previous))
            else:
                newcomers = [i for i, c in enumerate(centers) if i not in taken
                             and low + self.dead_band <= c[0] < high - self.dead_band]
                middle = ((low + high) / 2, .5)
                best = min(newcomers, key=lambda i: math.dist(centers[i], middle), default=None)
            if best is None:
                self.positions[lane] = None
                continue
            taken.add(best)
            self.positions[lane] = centers[best]
            result[lane + 1] = best
        return result


class SoloTracker:
    """Solo testing: no lanes. The person nearest the middle of the picture drives `player`, and keeps it
    while they move about (up to max_jump per frame), wherever they sit."""
    def __init__(self, player, max_jump=.25):
        if player not in (1, 2, 3):
            raise ValueError('Solo player must be 1, 2 or 3')
        self.player, self.max_jump = player, max_jump
        self.position = None

    def reset(self):
        self.position = None

    def assign(self, centers):
        if not centers:
            self.position = None
            return {}
        target = self.position if self.position is not None else (.5, .5)
        best = min(range(len(centers)), key=lambda i: math.dist(centers[i], target))
        if self.position is not None and math.dist(centers[best], self.position) > self.max_jump:
            best = min(range(len(centers)), key=lambda i: math.dist(centers[i], (.5, .5)))  # lost them: re-pick
        self.position = centers[best]
        return {self.player: best}


def requested_solo(data):
    """Unity's solo-test request: 1-3 = whole frame drives that player, 0 = back to lanes. None if absent."""
    try:
        value = json.loads(data)
        if isinstance(value, dict) and value.get('version') == 1:
            solo = value.get('soloPlayer')
            if type(solo) is int and 0 <= solo <= 3:
                return solo
    except (ValueError, UnicodeError):
        pass
    return None


def requested_players(data):
    """Unity's group-size request, if it is a valid tank battle size (2 or 3)."""
    try:
        value = json.loads(data)
        if isinstance(value, dict) and value.get('version') == 1:
            count = value.get('performerCount')
            if type(count) is int and count in (2, 3):
                return count
    except (ValueError, UnicodeError):
        pass
    return None


def has_count(data):
    try:
        value = json.loads(data)
        return isinstance(value, dict) and 'performerCount' in value
    except (ValueError, UnicodeError):
        return False


def draw_lanes(frame, tracker, assigned, cv2):
    h, w = frame.shape[:2]
    if isinstance(tracker, SoloTracker):
        label = f'SOLO TEST: whole picture drives P{tracker.player}' + ('' if tracker.player in assigned else ' - step in')
        cv2.putText(frame, label, (10, h - 14), cv2.FONT_HERSHEY_SIMPLEX, .7, (20, 20, 20), 5, cv2.LINE_AA)
        cv2.putText(frame, label, (10, h - 14), cv2.FONT_HERSHEY_SIMPLEX, .7, PLAYER_COLORS[tracker.player - 1], 2, cv2.LINE_AA)
        return
    for lane in range(tracker.count):
        low, high = tracker.bounds(lane)
        color = PLAYER_COLORS[lane]
        if lane:
            x = int(low * w)
            cv2.line(frame, (x, 0), (x, h), (20, 20, 20), 5)
            cv2.line(frame, (x, 0), (x, h), (235, 235, 235), 2)
        label = f'P{lane + 1}' + ('' if lane + 1 in assigned else ' - step in')
        org = (int(low * w) + 10, h - 14)
        cv2.putText(frame, label, org, cv2.FONT_HERSHEY_SIMPLEX, .7, (20, 20, 20), 5, cv2.LINE_AA)
        cv2.putText(frame, label, org, cv2.FONT_HERSHEY_SIMPLEX, .7, color, 2, cv2.LINE_AA)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--players', type=int, choices=(2, 3), default=3)
    parser.add_argument('--solo', type=int, choices=(0, 1, 2, 3), default=0,
                        help='Solo testing: the whole picture drives this player (0 = lanes)')
    parser.add_argument('--no-window', action='store_true', help='Preview is displayed inside Unity instead')
    parser.add_argument('--upper-body-only', action='store_true', help='Seated play: shoulders and hands only')
    parser.add_argument('--camera', type=int, default=0)
    parser.add_argument('--port', type=int, default=5055)
    parser.add_argument('--model', type=Path, default=STARTER / 'pose_landmarker_full.task')
    args = parser.parse_args()
    if not 1024 <= args.port <= 65531:
        parser.error('--port must be between 1024 and 65531')
    if not args.model.exists():
        parser.error(f'Pose model missing at {args.model}. Run the regular bridge once with --download-model.')
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision

    def pose_options():
        # Room for a bystander or two beyond the players; extras are ignored outside the lanes.
        return vision.PoseLandmarkerOptions(
            base_options=python.BaseOptions(model_asset_path=str(args.model)),
            running_mode=vision.RunningMode.VIDEO, num_poses=args.players + 2,
            min_pose_detection_confidence=.5, min_pose_presence_confidence=.5, min_tracking_confidence=.5)

    def gesture_options():
        return vision.GestureRecognizerOptions(
            base_options=python.BaseOptions(model_asset_path=str(GESTURE_MODEL)),
            running_mode=vision.RunningMode.VIDEO, num_hands=2 * args.players)

    if not GESTURE_MODEL.exists():
        print('Gesture model missing; drinking and shooting gestures disabled. See StudentTracking/models.txt.')
    solo_player = args.solo
    tracker = SoloTracker(solo_player) if solo_player else LaneTracker(args.players)
    camera = cv2.VideoCapture(args.camera)
    if not camera.isOpened():
        raise SystemExit('Camera could not open. Check permissions or try --camera 1.')
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sender.bind(('127.0.0.1', args.port + 1))
        sender.setblocking(False)
    except OSError:
        camera.release()
        sender.close()
        raise SystemExit('Another camera bridge is already running. Close it first (Q in its window).')
    edges = [(11, 12), (11, 23), (12, 24), (23, 24), (11, 13), (13, 15), (12, 14), (14, 16),
             (23, 25), (25, 27), (24, 26), (26, 28), (0, 7), (0, 8), (7, 11), (8, 12)]
    previous_timestamp, last_preview = -1, 0.0
    print(f'TANK BATTLE lanes: {args.players} players, left to right in the unmirrored preview.')
    print('R: reset lanes (then set neutral pose in Unity). Q: quit.')
    try:
        with contextlib.ExitStack() as stack:
            detector = stack.enter_context(vision.PoseLandmarker.create_from_options(pose_options()))
            recognizer = None
            if GESTURE_MODEL.exists():
                recognizer = RestartingTask(lambda: vision.GestureRecognizer.create_from_options(gesture_options()))
                stack.callback(recognizer.close)
            while True:
                for _ in range(32):
                    try:
                        data, address = sender.recvfrom(1024)
                    except BlockingIOError:
                        break
                    if address[0] != '127.0.0.1':
                        continue
                    # Solo-test switch: accepted from any local sender (Unity's solo-test component has its own socket).
                    solo = requested_solo(data)
                    if solo is not None:
                        if solo != solo_player:
                            solo_player = solo
                            tracker = SoloTracker(solo) if solo else LaneTracker(args.players)
                            print(f'Solo test: whole picture drives P{solo}.' if solo else 'Solo test off: lanes are back.')
                        continue
                    if address != ('127.0.0.1', args.port):
                        continue
                    try:
                        command = json.loads(data)
                        if isinstance(command, dict) and command.get('version') == 1:
                            upper = command.get('upperBodyOnly')
                            if type(upper) is bool and upper != args.upper_body_only:
                                args.upper_body_only = upper
                                tracker.reset()
                                print('Movement mode changed. Set neutral pose in Unity.')
                            if command.get('reset') is True:
                                tracker.reset()
                    except (ValueError, UnicodeError):
                        pass
                    count = requested_players(data)
                    if count is None and has_count(data):
                        print('Tank battle needs 2 or 3 players: choose 2 or 3 in Unity. Keeping', args.players)
                    if count is not None and count != args.players:
                        # Lanes change size, and gestures need room for the new number of hands.
                        args.players = count
                        if not solo_player:
                            tracker = LaneTracker(count)
                        if recognizer:
                            recognizer.close()
                            recognizer = RestartingTask(lambda: vision.GestureRecognizer.create_from_options(gesture_options()))
                            stack.callback(recognizer.close)
                        print(f'Unity selected {count} players. Lanes redrawn; set neutral pose in Unity.')
                ok, frame = camera.read()
                if not ok:
                    raise RuntimeError('Camera stopped delivering frames.')
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                timestamp = max(previous_timestamp + 1, time.monotonic_ns() // 1_000_000)
                previous_timestamp = timestamp
                image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                result = detector.detect_for_video(image, timestamp)
                observations = []
                for landmarks in result.pose_landmarks:
                    center, values, confidence = extract_features(landmarks, args.upper_body_only)
                    if confidence[4] >= .5:
                        observations.append((center, values, confidence, landmarks))
                assignments = tracker.assign([o[0] for o in observations])
                packet = {'version': 1, 'performerCount': args.players, 'upperBodyOnly': args.upper_body_only,
                          'people': [dict(slot=slot, values=observations[i][1], confidence=observations[i][2])
                                     for slot, i in assignments.items()]}
                sender.sendto(json.dumps(packet, allow_nan=False).encode(), ('127.0.0.1', args.port))

                wrists = {}
                for slot, i in assignments.items():
                    wrists[slot, 'left'] = (observations[i][3][15].x, observations[i][3][15].y)
                    wrists[slot, 'right'] = (observations[i][3][16].x, observations[i][3][16].y)
                hands = {}
                if recognizer:
                    gesture_result = recognizer.run('recognize_for_video', image, timestamp)
                    for hand, gestures in zip(gesture_result.hand_landmarks, gesture_result.gestures) if gesture_result else ():
                        key = nearest_wrist((hand[0].x, hand[0].y), wrists, .15)
                        if key and gestures[0].category_name != 'None' and gestures[0].score > hands.get(key, ('None', 0.))[1]:
                            hands[key] = (gestures[0].category_name, gestures[0].score)
                    people = [{'slot': slot, 'left': hands.get((slot, 'left'), ('None', 0.))[0],
                               'leftScore': hands.get((slot, 'left'), ('None', 0.))[1],
                               'right': hands.get((slot, 'right'), ('None', 0.))[0],
                               'rightScore': hands.get((slot, 'right'), ('None', 0.))[1]} for slot in assignments]
                    send_quietly(sender, json.dumps({'version': 1, 'people': people}).encode(), args.port + 3)

                # Preview: lanes, every body (players in their colour), and recognised gestures.
                h, w = frame.shape[:2]
                draw_lanes(frame, tracker, assignments, cv2)
                players = {id(observations[i][3]): slot for slot, i in assignments.items()}
                for landmarks in result.pose_landmarks:
                    slot = players.get(id(landmarks))
                    color = PLAYER_COLORS[slot - 1] if slot else (150, 150, 150)
                    points = {k: (min(w - 1, int(p.x * w)), min(h - 1, int(p.y * h))) for k, p in enumerate(landmarks)
                              if min(p.visibility, p.presence) >= .5 and math.isfinite(p.x) and math.isfinite(p.y)
                              and 0 <= p.x <= 1 and 0 <= p.y <= 1}
                    for a, b in edges:
                        if a in points and b in points:
                            cv2.line(frame, points[a], points[b], (20, 20, 20), 5, cv2.LINE_AA)
                            cv2.line(frame, points[a], points[b], color, 3, cv2.LINE_AA)
                    if slot and 11 in points:
                        cv2.putText(frame, f'P{slot}', points[11], cv2.FONT_HERSHEY_SIMPLEX, .9, (20, 20, 20), 5, cv2.LINE_AA)
                        cv2.putText(frame, f'P{slot}', points[11], cv2.FONT_HERSHEY_SIMPLEX, .9, color, 2, cv2.LINE_AA)
                for (slot, side), (name, score) in hands.items():
                    x, y = wrists[slot, side]
                    cv2.putText(frame, name, (int(x * w), int(y * h)), cv2.FONT_HERSHEY_SIMPLEX, .6,
                                PLAYER_COLORS[slot - 1], 2, cv2.LINE_AA)
                cv2.putText(frame, f'TANK BATTLE SOLO TEST | P{solo_player}' if solo_player else
                            f'TANK BATTLE | {len(assignments)}/{args.players} players in lanes',
                            (12, 26), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)
                now = time.monotonic()
                if now - last_preview >= .1:
                    jpeg = encode_preview(frame, cv2)
                    if jpeg is not None:
                        send_quietly(sender, jpeg, args.port + 2)
                    last_preview = now
                if not args.no_window:
                    cv2.imshow('DigiPhant TANK BATTLE lanes (unmirrored)', frame)
                key = (cv2.waitKey(1) & 0xff) if not args.no_window else -1
                if key == ord('q'):
                    break
                if key == ord('r'):
                    tracker.reset()
    finally:
        sender.sendto(b'{"version":1,"people":[]}', ('127.0.0.1', args.port))
        sender.close()
        camera.release()
        cv2.destroyAllWindows()


if __name__ == '__main__':
    main()
