using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DeadweightTanks
{
    // Hand-motor rules for one tank (a copy of HandMotors.Mix, kept here so the game and the demo can't affect each other).
    // Hand heights are relative to the neutral pose: positive = above, negative = below.
    //   both up = forward (higher = faster), both down = backward, only left up = turn left, only right up = turn right.
    public static class TankControls
    {
        public static void Mix(float left, float right, float up, float down, float fullSpeedHeight, out float forward, out float turn)
        {
            forward = turn = 0;
            bool leftUp = left > up, rightUp = right > up;
            if (leftUp && rightUp) forward = Mathf.InverseLerp(up, Mathf.Max(up + .01f, fullSpeedHeight), Mathf.Min(left, right));
            else if (left < -down && right < -down) forward = -Mathf.InverseLerp(down, Mathf.Max(down + .01f, fullSpeedHeight), -Mathf.Max(left, right));
            else if (leftUp) turn = -1;
            else if (rightUp) turn = 1;
        }
    }

    // Keyboard stand-in for one player's hands, for testing without the camera.
    [Serializable]
    public class TankKeys
    {
        public Key forward, back, left, right, run, drink, shoot;

        public static TankKeys ForPlayer(int player) => player switch
        {
            1 => new TankKeys { forward = Key.W, back = Key.S, left = Key.A, right = Key.D, run = Key.LeftShift, drink = Key.Q, shoot = Key.E },
            2 => new TankKeys { forward = Key.I, back = Key.K, left = Key.J, right = Key.L, run = Key.Y, drink = Key.U, shoot = Key.O },
            _ => new TankKeys { forward = Key.UpArrow, back = Key.DownArrow, left = Key.LeftArrow, right = Key.RightArrow, run = Key.RightShift, drink = Key.Comma, shoot = Key.Period },
        };

        public string Describe() => $"{forward}{left}{back}{right} move, {run} run, {drink} drink, {shoot} shoot";

        static bool Held(Keyboard k, Key key) => key != Key.None && k[key].isPressed;
        public bool Pressed(Key key) => Keyboard.current is Keyboard k && key != Key.None && k[key].wasPressedThisFrame;

        // Virtual hand heights while any movement key is held; false = no key held, use the camera.
        public bool TryHands(float up, float fullSpeedHeight, out float leftHand, out float rightHand)
        {
            leftHand = rightHand = 0;
            if (!(Keyboard.current is Keyboard k)) return false;
            float walk = (up + Mathf.Max(up, fullSpeedHeight)) / 2, high = Mathf.Max(up, fullSpeedHeight) + .1f;
            if (Held(k, forward)) leftHand = rightHand = Held(k, run) ? high : walk;
            else if (Held(k, back)) leftHand = rightHand = -walk;
            else if (Held(k, left)) leftHand = walk;
            else if (Held(k, right)) rightHand = walk;
            else return false;
            return true;
        }
    }
}
