using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace MyFirstPlugin
{
    [BepInPlugin(
        "com.epicopy.copilot.simhubtelemetry",
        "Epicopy + Copilot SimHub Telemetry",
        "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string SimHubHost = "127.0.0.1";
        private const int SimHubPort = 5000;
        private const int ExpectedPacketLength = 91;
        private const int SendIntervalMs = 15;
        private const double DebugStatusIntervalSeconds = 5.0;

        public static ManualLogSource Log;

        private static volatile bool _debugLogging;
        private static UdpClient _udpClient;
        private static SHTelemetryPacket _telemetryPacket;
        private static byte[] _packetBuffer;
        private static IntPtr _packetPointer;
        private static Thread _senderThread;
        private static Stopwatch _sessionClock;
        private static volatile bool _senderRunning;
        private static Harmony _harmony;
        private static bool _isShuttingDown;
        private static bool _applicationQuitting;

        private void Awake()
        {
            Log = Logger;

            _debugLogging = Config.Bind(
                "Logging",
                "EnableDebugLogging",
                false,
                "Enables periodic UDP sender status logs and packet diagnostics.").Value;
            HapticTuning.Bind(Config);

            try
            {
                int packetSize = Marshal.SizeOf(typeof(SHTelemetryPacket));
                if (packetSize != ExpectedPacketLength)
                    throw new InvalidOperationException("SimHub telemetry packet size mismatch: " + packetSize);

                _udpClient = new UdpClient();
                _udpClient.Connect(SimHubHost, SimHubPort);
                _telemetryPacket = SHTelemetryPacket.CreateDefault();
                // SimHub uses these IDs to distinguish this sender and telemetry session.
                _telemetryPacket.EmitterInstanceId = CreateRandomUInt64();
                _telemetryPacket.SessionId = CreateRandomUInt64();
                _telemetryPacket.IsSessionRunning = 1;
                _telemetryPacket.IsUserInControl = 1;

                _packetBuffer = new byte[packetSize];
                _packetPointer = Marshal.AllocHGlobal(packetSize);
                _sessionClock = Stopwatch.StartNew();

                _senderRunning = true;
                _senderThread = new Thread(SendTelemetryLoop)
                {
                    IsBackground = true,
                    Name = "SimHub telemetry sender"
                };
                _senderThread.Start();
                Application.quitting += HandleApplicationQuitting;

                HapticMixer.Reset();
                _harmony = new Harmony("com.epicopy.copilot.simhubtelemetry");
                _harmony.PatchAll(typeof(Plugin).Assembly);

                string[] patchedMethods = _harmony.GetPatchedMethods()
                    .Select(method => method.DeclaringType.Name + "." + method.Name)
                    .ToArray();
                if (patchedMethods.Length == 0)
                    throw new InvalidOperationException("Harmony loaded, but no game event patches were installed.");

                Log.LogInfo("Haptic event patches active: " + string.Join(", ", patchedMethods) + ".");
                Log.LogInfo("SimHub UDP telemetry sender initialized for " +
                    SimHubHost + ":" + SimHubPort + ".");
                LogDebug("Packet size: " + packetSize + " bytes; send interval: " +
                    SendIntervalMs + " ms.");
            }
            catch (Exception ex)
            {
                StopTelemetrySender("initialization failure");
                _harmony?.UnpatchSelf();
                _harmony = null;
                Log.LogError("SimHub telemetry initialization failed: " + ex);
            }
        }

        private void OnDestroy()
        {
            Log?.LogWarning("BepInEx plugin component was destroyed; sender and mixer continue " +
                "on the background thread. applicationIsQuitting=" +
                _applicationQuitting + ".");
        }

        internal static void HandleApplicationQuitting()
        {
            _applicationQuitting = true;
            StopTelemetrySender("Unity Application.quitting");
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private static void SendTelemetryLoop()
        {
            bool firstPacketLogged = false;
            bool sendErrorLogged = false;
            double nextStatusLogAt = 0.0;

            while (_senderRunning)
            {
                // Keep the packet current at 60+ Hz, including between detected events.
                double sessionTime = _sessionClock.Elapsed.TotalSeconds;
                HapticMixer.Tick(sessionTime);
                _telemetryPacket.PacketsCounter++;
                _telemetryPacket.SessionTimeSeconds = sessionTime;
                CopyChannelInputsToPacket();

                try
                {
                    Marshal.StructureToPtr(_telemetryPacket, _packetPointer, false);
                    Marshal.Copy(_packetPointer, _packetBuffer, 0, _packetBuffer.Length);
                    _udpClient.Send(_packetBuffer, _packetBuffer.Length);

                    if (!firstPacketLogged)
                    {
                        Log.LogInfo("First SimHub UDP telemetry packet sent (" +
                            _packetBuffer.Length + " bytes).");
                        firstPacketLogged = true;
                    }

                    if (nextStatusLogAt == 0.0)
                    {
                        LogDebug("First UDP telemetry packet sent.");
                        nextStatusLogAt = sessionTime + DebugStatusIntervalSeconds;
                    }
                    else if (sessionTime >= nextStatusLogAt)
                    {
                        LogDebug("UDP sender alive; packets sent: " +
                            _telemetryPacket.PacketsCounter + ", " +
                            HapticMixer.GetDiagnosticSummary() + ", channels: " +
                            FormatChannelValues() + ".");
                        nextStatusLogAt = sessionTime + DebugStatusIntervalSeconds;
                    }

                    if (sendErrorLogged)
                    {
                        LogDebug("UDP telemetry sending recovered.");
                        sendErrorLogged = false;
                    }
                }
                catch (Exception ex)
                {
                    if (!sendErrorLogged)
                    {
                        Log.LogError("UDP telemetry send failed: " + ex);
                        sendErrorLogged = true;
                    }
                }

                Thread.Sleep(SendIntervalMs);
            }
        }

        // Copy each independently calculated 0..1 output into its matching .simdef field.
        private static void CopyChannelInputsToPacket()
        {
            _telemetryPacket.BassShaker = Clamp01(TelemetryInputs.BassShaker);
            _telemetryPacket.HapticUpperBackLeft = Clamp01(TelemetryInputs.HapticUpperBackLeft);
            _telemetryPacket.HapticUpperBackRight = Clamp01(TelemetryInputs.HapticUpperBackRight);
            _telemetryPacket.HapticMidBackLeft = Clamp01(TelemetryInputs.HapticMidBackLeft);
            _telemetryPacket.HapticMidBackRight = Clamp01(TelemetryInputs.HapticMidBackRight);
            _telemetryPacket.HapticLowerBackLeft = Clamp01(TelemetryInputs.HapticLowerBackLeft);
            _telemetryPacket.HapticLowerBackRight = Clamp01(TelemetryInputs.HapticLowerBackRight);
            _telemetryPacket.HapticSeatLeft = Clamp01(TelemetryInputs.HapticSeatLeft);
            _telemetryPacket.HapticSeatRight = Clamp01(TelemetryInputs.HapticSeatRight);
        }

        private static string FormatChannelValues()
        {
            return "Bass=" + _telemetryPacket.BassShaker.ToString("0.00") +
                ", UpperBackL=" + _telemetryPacket.HapticUpperBackLeft.ToString("0.00") +
                ", UpperBackR=" + _telemetryPacket.HapticUpperBackRight.ToString("0.00") +
                ", MidBackL=" + _telemetryPacket.HapticMidBackLeft.ToString("0.00") +
                ", MidBackR=" + _telemetryPacket.HapticMidBackRight.ToString("0.00") +
                ", LowerBackL=" + _telemetryPacket.HapticLowerBackLeft.ToString("0.00") +
                ", LowerBackR=" + _telemetryPacket.HapticLowerBackRight.ToString("0.00") +
                ", SeatL=" + _telemetryPacket.HapticSeatLeft.ToString("0.00") +
                ", SeatR=" + _telemetryPacket.HapticSeatRight.ToString("0.00");
        }

        internal static void LogDebug(string message)
        {
            if (_debugLogging)
                Log?.LogInfo("[Debug] " + message);
        }

        internal static double GetSessionTimeSeconds()
        {
            return _sessionClock != null ? _sessionClock.Elapsed.TotalSeconds : 0.0;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }

        private static ulong CreateRandomUInt64()
        {
            ulong value = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            return value == 0 ? 1UL : value;
        }

        private static void StopTelemetrySender(string reason)
        {
            if (_isShuttingDown)
                return;

            _isShuttingDown = true;
            Application.quitting -= HandleApplicationQuitting;
            Log?.LogInfo("Stopping SimHub UDP sender; reason: " + reason + ".");
            _senderRunning = false;

            if (_senderThread != null && _senderThread != Thread.CurrentThread)
            {
                _senderThread.Join();
                _senderThread = null;
            }

            _udpClient?.Close();
            _udpClient = null;

            if (_packetPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_packetPointer);
                _packetPointer = IntPtr.Zero;
            }

            LogDebug("UDP telemetry sender stopped.");
        }
    }

    // Normalized outputs written by the event-specific haptic mixer.
    public static class TelemetryInputs
    {
        public static volatile float BassShaker;
        public static volatile float HapticUpperBackLeft;
        public static volatile float HapticUpperBackRight;
        public static volatile float HapticMidBackLeft;
        public static volatile float HapticMidBackRight;
        public static volatile float HapticLowerBackLeft;
        public static volatile float HapticLowerBackRight;
        public static volatile float HapticSeatLeft;
        public static volatile float HapticSeatRight;
    }

    // Keep this packed field order and types in sync with the SimHub-generated .simdef packet.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SHTelemetryPacket
    {
        public uint GameSignature;
        public uint TelemetrySignature;
        public ushort LayoutMajorVersion;
        public ushort LayoutMinorVersion;
        public ulong EmitterInstanceId;
        public byte PacketId;
        public ulong PacketsCounter;
        public byte IsSessionRunning;
        public byte IsSessionPaused;
        public ulong SessionId;
        public byte IsReplay;
        public byte IsUserInControl;
        public byte IsAIInControl;
        public byte IsSpectator;
        public double SessionTimeSeconds;
        public uint PhysicsDiscontinuityCounter;
        public float BassShaker;
        public float HapticUpperBackLeft;
        public float HapticUpperBackRight;
        public float HapticMidBackLeft;
        public float HapticMidBackRight;
        public float HapticLowerBackLeft;
        public float HapticLowerBackRight;
        public float HapticSeatLeft;
        public float HapticSeatRight;

        public static SHTelemetryPacket CreateDefault()
        {
            return new SHTelemetryPacket
            {
                GameSignature = 0x5B503624u,
                TelemetrySignature = 0x1AD4446Fu,
                LayoutMajorVersion = 1,
                LayoutMinorVersion = 0
            };
        }
    }
}
