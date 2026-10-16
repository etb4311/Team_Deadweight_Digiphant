using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using DigiPhant;
using TeamDeadweight;

namespace DeadweightTanks
{
    // Lets one person test the camera controls alone. While on:
    //  - the tank tracker drops its lanes: whoever is nearest the middle of the picture drives the chosen player;
    //  - "Set neutral pose" only needs that player's hands (normally it waits for every player's).
    // The other elephants stay on their keys. F1 toggles solo, F2 picks the next player. Changes only this
    // scene's controller, and only while playing; turning solo off restores the lanes and the full list.
    [RequireComponent(typeof(DigiPhantController))]
    public class TankSoloTest : MonoBehaviour, IStatusLine
    {
        public bool solo;
        [Range(1, 3)] public int soloPlayer = 1;

        DigiPhantController controller;
        BoneControl[] all;
        bool applied;
        int appliedPlayer;

        public string StatusTitle => "Solo test";
        public string StatusValue => solo
            ? $"P{soloPlayer} · whole picture, sit anywhere (F1 off, F2 next)"
            : "off (F1 on)";
        public bool StatusActive => solo;

        UdpClient sender;
        float nextSend;

        void Start()
        {
            controller = GetComponent<DigiPhantController>();
            all = controller.controls;
        }

        void OnDisable()
        {
            if (controller && all != null) controller.controls = all;
            if (applied) Tell(0); // put the tracker back on lanes
            applied = false;
            sender?.Close(); sender = null;
        }

        // Tell the tank tracker which player the whole picture drives (0 = lanes). Repeated every second,
        // so a tracker that restarts picks it up again.
        void Tell(int player)
        {
            if (!controller) return;
            try
            {
                sender ??= new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                byte[] data = Encoding.UTF8.GetBytes("{\"version\":1,\"soloPlayer\":" + player + "}");
                sender.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, controller.port + 1));
            }
            catch (Exception e) when (e is SocketException || e is ObjectDisposedException) { }
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k != null && k.f1Key.wasPressedThisFrame) solo = !solo;
            if (k != null && k.f2Key.wasPressedThisFrame) soloPlayer = soloPlayer % 3 + 1;
            if (solo && Time.realtimeSinceStartup >= nextSend) { nextSend = Time.realtimeSinceStartup + 1; Tell(soloPlayer); }
            if (solo == applied && (!solo || appliedPlayer == soloPlayer)) return;
            Tell(solo ? soloPlayer : 0);
            nextSend = Time.realtimeSinceStartup + 1;

            controller.controls = solo ? all.Where(c => c.performer == soloPlayer).ToArray() : all;
            applied = solo;
            appliedPlayer = soloPlayer;
            controller.InvalidateCalibration(); // the old neutral pose covered different players
        }
    }
}
