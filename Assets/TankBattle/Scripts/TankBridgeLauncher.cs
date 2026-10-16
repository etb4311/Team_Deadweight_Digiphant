using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using DigiPhant;
using TeamDeadweight;

namespace DeadweightTanks
{
    // Starts TankBattle/Tracking/tank_bridge.py (lane tracking) when the tank scene plays, the way the demo's
    // camera preview starts the regular bridge. Restarts it a few seconds after it stops. Editor only.
    // The scene's DigiPhantCameraPreview has autoStartBridge off, so the regular bridge is never launched here.
    [RequireComponent(typeof(DigiPhantController))]
    public class TankBridgeLauncher : MonoBehaviour, IStatusLine
    {
        public bool autoStart = true;
        [Tooltip("Seconds to wait before starting again after the tracker stops.")]
        public float retrySeconds = 5;

        public string Status { get; private set; } = "Waiting to start";

        DigiPhantController controller;
        DigiPhantCameraPreview preview;
        Process bridge;
        float nextAttempt;
        string output = "";
        readonly object outputLock = new object();

        public string StatusTitle => "Tracker";
        public string StatusValue => Status;
        public bool StatusActive => false;

        void OnEnable()
        {
            controller = GetComponent<DigiPhantController>();
            preview = GetComponent<DigiPhantCameraPreview>();
            nextAttempt = Time.realtimeSinceStartup + 1; // let the preview open its port first
        }

        void OnDisable() => Stop();

        void Update()
        {
            if (!Application.isPlaying || Application.isBatchMode || !autoStart) return;
            float now = Time.realtimeSinceStartup;
            if (bridge != null && bridge.HasExited)
            {
                string detail;
                lock (outputLock) detail = output;
                string last = LastLine(detail);
                Status = "Stopped" + (last.Length > 0 ? ": " + last : "") + ". Retrying...";
                if (!string.IsNullOrWhiteSpace(detail)) UnityEngine.Debug.LogWarning("Tank tracker: " + detail);
                bridge.Dispose();
                bridge = null;
                nextAttempt = now + retrySeconds;
            }
            bool live = preview && preview.HasLiveFrame;
            if (bridge == null && !live && now >= nextAttempt && controller.inputMode == InputMode.Camera)
            {
                nextAttempt = now + retrySeconds;
                Launch();
            }
            else if (bridge != null && live) Status = "Running (lanes)";
            else if (bridge == null && live) Status = "Using a tracker started elsewhere";
        }

        void Launch()
        {
#if UNITY_EDITOR
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string folder = Path.Combine(root, "TankBattle", "Tracking");
            string python = Path.Combine(root, "DigiPhantStarter", "Tracking",
                Application.platform == RuntimePlatform.WindowsEditor ? ".venv/Scripts/python.exe" : ".venv/bin/python");
            if (!File.Exists(python) || !File.Exists(Path.Combine(folder, "tank_bridge.py")))
            { Status = "Setup needed: missing Python venv or TankBattle/Tracking/tank_bridge.py"; return; }
            int players = Mathf.Clamp(controller.performerCount, 2, 3);
            int cameraIndex = preview ? Mathf.Clamp(preview.cameraIndex, 0, 8) : 0;
            try
            {
                lock (outputLock) output = "";
                // WorkingDirectory makes every argument a fixed filename or a validated integer.
                var info = new ProcessStartInfo(python,
                    $"-u tank_bridge.py --no-window --players {players} --camera {cameraIndex} --port {controller.port}" +
                    (controller.upperBodyOnly ? " --upper-body-only" : ""))
                {
                    WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                bridge = new Process { StartInfo = info };
                bridge.OutputDataReceived += Capture;
                bridge.ErrorDataReceived += Capture;
                bridge.Start();
                bridge.BeginOutputReadLine();
                bridge.BeginErrorReadLine();
                Status = "Starting — allow camera access if macOS asks";
            }
            catch (Exception e)
            {
                bridge?.Dispose(); bridge = null;
                Status = "Launch failed: " + e.Message;
            }
#else
            Status = "Start TankBattle/Tracking/tank_bridge.py on this computer.";
#endif
        }

        void Capture(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null) return;
            lock (outputLock)
            {
                output += args.Data + "\n";
                if (output.Length > 4000) output = output.Substring(output.Length - 4000);
            }
        }

        static string LastLine(string text)
        {
            var lines = text.Trim().Split('\n');
            string last = lines.Length > 0 ? lines[lines.Length - 1].Trim() : "";
            return last.Length > 90 ? last.Substring(0, 90) + "…" : last;
        }

        void Stop()
        {
            if (bridge == null) return;
            try { if (!bridge.HasExited) bridge.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            bridge.Dispose();
            bridge = null;
        }
    }
}
