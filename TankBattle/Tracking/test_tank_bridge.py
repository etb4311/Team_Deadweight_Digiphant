import unittest
from tank_bridge import LaneTracker, SoloTracker, requested_players, requested_solo


class LaneTests(unittest.TestCase):
    def test_lane_is_player_regardless_of_detection_order(self):
        t = LaneTracker(3)
        self.assertEqual(t.assign([(.85, .5), (.15, .5), (.5, .5)]), {1: 1, 2: 2, 3: 0})

    def test_players_are_independent(self):
        t = LaneTracker(3)
        t.assign([(.15, .5), (.5, .5), (.85, .5)])
        # P2 leaves: P1 and P3 keep control, nobody else is reassigned.
        self.assertEqual(t.assign([(.16, .5), (.84, .5)]), {1: 0, 3: 1})
        # Starting does not wait for everyone.
        self.assertEqual(LaneTracker(3).assign([(.5, .5)]), {2: 0})

    def test_newcomer_must_be_clearly_inside_a_lane(self):
        t = LaneTracker(3)
        self.assertEqual(t.assign([(.34, .5)]), {})       # on the P1/P2 boundary
        self.assertEqual(t.assign([(.38, .5)]), {2: 0})

    def test_followed_player_may_drift_to_the_edge_but_not_across(self):
        t = LaneTracker(3)
        t.assign([(.5, .5)])
        self.assertEqual(t.assign([(.34, .5)]), {2: 0})   # dead band is fine once followed
        self.assertEqual(t.assign([(.32, .5)]), {})       # crossed into P1's lane, too close to the edge to join it

    def test_follows_the_same_body_when_two_share_a_lane(self):
        t = LaneTracker(2)
        t.assign([(.25, .5)])
        self.assertEqual(t.assign([(.10, .5), (.27, .5)]), {1: 1})

    def test_two_players(self):
        self.assertEqual(LaneTracker(2).assign([(.75, .5), (.25, .5)]), {1: 1, 2: 0})
        with self.assertRaises(ValueError):
            LaneTracker(4)

    def test_requested_players(self):
        self.assertEqual(requested_players(b'{"version":1,"performerCount":2}'), 2)
        self.assertIsNone(requested_players(b'{"version":1,"performerCount":4}'))
        self.assertIsNone(requested_players(b'not json'))


class SoloTests(unittest.TestCase):
    def test_anywhere_in_the_picture_drives_the_solo_player(self):
        self.assertEqual(SoloTracker(1).assign([(.5, .5)]), {1: 0})
        self.assertEqual(SoloTracker(2).assign([(.1, .5)]), {2: 0})   # even at the far edge

    def test_picks_the_central_person_and_keeps_them(self):
        t = SoloTracker(1)
        self.assertEqual(t.assign([(.1, .5), (.55, .5)]), {1: 1})
        self.assertEqual(t.assign([(.62, .5), (.45, .5)]), {1: 0})    # follows the same person as they move
        self.assertEqual(t.assign([]), {})

    def test_solo_request(self):
        self.assertEqual(requested_solo(b'{"version":1,"soloPlayer":2}'), 2)
        self.assertEqual(requested_solo(b'{"version":1,"soloPlayer":0}'), 0)
        self.assertIsNone(requested_solo(b'{"version":1,"performerCount":3}'))
        self.assertIsNone(requested_solo(b'{"version":1,"soloPlayer":5}'))
        with self.assertRaises(ValueError):
            SoloTracker(4)


if __name__ == '__main__':
    unittest.main()
