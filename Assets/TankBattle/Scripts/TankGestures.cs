using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using DigiPhant;

namespace DeadweightTanks
{
    // Reads each player's hand gestures from the tank tracker (controller port + 3) and turns a gesture held for
    // holdSeconds into a single "fire once" event. A held gesture fires once; it re-arms after it has been gone
    // (or changed) for holdSeconds, so a one-frame dropout doesn't fire it twice.
    [RequireComponent(typeof(DigiPhantController))]
    public class TankGestures : MonoBehaviour
    {
        public string drinkGesture = "Victory";
        public string shootGesture = "Closed_Fist";
        [Range(0, 1)] public float minimumScore = .6f;
        public float holdSeconds = .3f;

        [Serializable] class Hands { public int slot; public string left, right; public float leftScore, rightScore; }
        [Serializable] class Packet { public int version; public Hands[] people; }

        class Player { public string held; public float since, lastSeen = -1000; public bool armed, ready; }
        readonly Player[] players = { new Player(), new Player(), new Player() };

        DigiPhantController controller;
        UdpClient socket;
        Packet packet;
        float packetTime = -1000;

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            if (!Application.isPlaying) return;
            try
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, controller.port + 3));
                socket.Client.Blocking = false;
            }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException)
            { Debug.LogWarning("Tank gestures: cannot open port " + (controller.port + 3) + ": " + e.Message); }
        }

        void OnDisable() { socket?.Close(); socket = null; }

        // Name of the gesture this player is showing right now ("" if none / not tracked).
        public string Showing(int player) => player >= 1 && player <= 3 ? players[player - 1].held ?? "" : "";

        // True once per held gesture, after it has been held for holdSeconds.
        public bool Consume(int player, string gesture)
        {
            if (player < 1 || player > 3) return false;
            var p = players[player - 1];
            if (!p.ready || p.held != gesture) return false;
            p.ready = false;
            return true;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            Poll(now);
            for (int i = 0; i < 3; i++)
            {
                var p = players[i];
                string gesture = Current(i + 1, now);
                if (gesture != null && gesture == p.held) p.lastSeen = now;
                else if (p.held == null || now - p.lastSeen >= holdSeconds)
                {
                    p.held = gesture;
                    p.since = p.lastSeen = now;
                    p.armed = true;
                    p.ready = false;
                }
                if (p.held != null && p.armed && now - p.since >= holdSeconds) { p.armed = false; p.ready = true; }
            }
        }

        void Poll(float now)
        {
            if (socket == null) return;
            try
            {
                byte[] latest = null;
                var from = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 64 && socket.Available > 0; i++) latest = socket.Receive(ref from);
                if (latest == null) return;
                packet = JsonUtility.FromJson<Packet>(Encoding.UTF8.GetString(latest));
                packetTime = now;
            }
            catch (SocketException) { }
            catch (ArgumentException) { }
        }

        bool Counts(string name, float score) => score >= minimumScore && (name == drinkGesture || name == shootGesture);

        string Current(int player, float now)
        {
            if (controller.inputMode != InputMode.Camera || packet?.people == null || now - packetTime > controller.trackingTimeout) return null;
            foreach (var h in packet.people)
            {
                if (h == null || h.slot != player) continue;
                if (Counts(h.left, h.leftScore)) return h.left;
                if (Counts(h.right, h.rightScore)) return h.right;
            }
            return null;
        }
    }
}
