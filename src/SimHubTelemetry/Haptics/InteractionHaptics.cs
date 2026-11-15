using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MyFirstPlugin
{
    // Measures the giantess hand / foot motion during the Akipo interaction events
    // (Pet, Lap, Foot massage) and feeds continuous signals into the mixer.
    //
    // Units of the game world are not assumed: speeds are normalized against the
    // peak observed during the current event, and proximity against the closest
    // distance observed, so the signals stay in 0..1 at any giantess scale.
    internal static class InteractionHaptics
    {
        private static readonly int PetScratchIntro = Animator.StringToHash("PetScratchIntro");
        private static readonly int PetScratchLoop = Animator.StringToHash("PetScratchLoop");
        private static readonly int PetScratchOutro = Animator.StringToHash("PetScratchOutro");
        private static readonly int LapCaressLoop = Animator.StringToHash("LapCaressLoop");
        private static readonly int LapLoop = Animator.StringToHash("LapLoop");
        private static float _lapBreathPhase;
        private static readonly int FootMassageLoop = Animator.StringToHash("FootMassageLoop");
        private static readonly int FootMassageCaressLoop = Animator.StringToHash("FootMassageCaressLoop");

        private static readonly HandTracker PetLeft = new HandTracker();
        private static readonly HandTracker PetRight = new HandTracker();
        private static readonly HandTracker LapLeft = new HandTracker();
        private static readonly HandTracker LapRight = new HandTracker();
        private static readonly FootTracker MassageFoot = new FootTracker();
        private static float _petSmoothed;
        private static float _petSide;
        private static float _lapSmoothed;
        private static float _massageSmoothed;
        private static bool _missingBonesLogged;
        private static double _nextLogAt;

        internal static void UpdatePet(GiantessAI giantessAI)
        {
            Animator animator = giantessAI?.animator;
            if (animator == null || !IsInState(animator, PetScratchIntro, PetScratchLoop, PetScratchOutro))
            {
                ResetPet();
                return;
            }

            float dt = Time.deltaTime;
            Vector3 camera = PlayerDriver.GetCameraPosition();
            if (!TryGetBones(animator, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                    out Transform left, out Transform right))
                return;

            float leftScore = PetLeft.Update(left.position, camera, dt);
            float rightScore = PetRight.Update(right.position, camera, dt);
            Transform active = leftScore >= rightScore ? left : right;
            float score = Mathf.Max(leftScore, rightScore);
            _petSmoothed = Smooth(_petSmoothed, score, dt, 0.08f, 0.25f);

            // Continuous left/right position of the stroking hand relative to the
            // view, smoothed so the feeling glides between the shoulders.
            Vector3 viewRight = PlayerDriver.GetCameraForwardDirection();
            viewRight = Vector3.Cross(Vector3.up, new Vector3(viewRight.x, 0f, viewRight.z)).normalized;
            Vector3 offset = active.position - camera;
            float lateral = offset.magnitude > 0.001f
                ? Vector3.Dot(offset, viewRight) / offset.magnitude : 0f;
            _petSide = Smooth(_petSide, Mathf.Clamp(lateral * 2f, -1f, 1f), dt, 0.15f, 0.15f);
            HapticMixer.ReportPet(_petSmoothed, _petSide);
            LogSignal("Pet", score, _petSmoothed, Mathf.Min(PetLeft.Distance, PetRight.Distance));
        }

        internal static void UpdateLap(GiantessAI giantessAI)
        {
            Animator animator = giantessAI?.animator;
            bool caressing = animator != null && IsInState(animator, LapCaressLoop);
            bool held = animator != null && IsInState(animator, LapLoop);
            if (!caressing && !held)
            {
                ResetLap();
                return;
            }

            float dt = Time.deltaTime;
            float score = 0f;
            if (caressing)
            {
                Vector3 camera = PlayerDriver.GetCameraPosition();
                if (!TryGetBones(animator, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                        out Transform left, out Transform right))
                    return;

                // Motion is measured relative to the player, so the hand that holds the
                // player (moving together with the camera) contributes almost nothing.
                score = Mathf.Max(
                    LapLeft.Update(left.position, camera, dt),
                    LapRight.Update(right.position, camera, dt));
            }
            else
            {
                LapLeft.Reset();
                LapRight.Reset();
            }

            // While resting in the lap (e.g. during dialogue, when the hand barely
            // moves) a soft breathing-like swell keeps the presence noticeable.
            _lapBreathPhase = (_lapBreathPhase + dt / HapticTuning.LapIdleBreathSeconds) % 1f;
            float breath = 0.5f - 0.5f * Mathf.Cos(_lapBreathPhase * 2f * Mathf.PI);
            float idle = HapticTuning.LapIdleLevel * breath;
            _lapSmoothed = Smooth(_lapSmoothed, Mathf.Max(score, idle), dt, 0.10f, 0.30f);

            HapticMixer.ReportLapCaress(_lapSmoothed);
            LogSignal(caressing ? "Lap caress" : "Lap resting", score, _lapSmoothed,
                caressing ? Mathf.Min(LapLeft.Distance, LapRight.Distance) : 0f);
        }

        internal static void UpdateFootMassage(GiantessAI giantessAI)
        {
            Animator animator = giantessAI?.animator;
            if (animator == null || !IsInState(animator, FootMassageLoop, FootMassageCaressLoop))
            {
                ResetMassage();
                return;
            }

            Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (foot == null)
            {
                LogMissingBones();
                return;
            }

            float dt = Time.deltaTime;
            float motion = MassageFoot.Update(foot, PlayerDriver.GetCameraPosition(), dt);
            // A power curve deepens the valleys (more contrast); attack and release
            // still turn the foot motion into a swelling pressure.
            float shaped = Mathf.Pow(motion, HapticTuning.FootMassageContrast);
            _massageSmoothed = Smooth(_massageSmoothed, shaped, dt, 0.12f, 0.28f);

            HapticMixer.ReportMassagePressure(_massageSmoothed);
            LogSignal("Foot massage", motion, _massageSmoothed, MassageFoot.Distance);
        }

        // Vertical acceleration felt while an interaction carries the player
        // (being lifted or set down). Depending on how a scene starts, either the
        // camera (gliding onto the hand) or the carrying body part moves, so each
        // has its own meter and the stronger one wins.
        internal enum LiftSource { Camera, HoldingHand, Foot, CameraAndHand }

        private static readonly LiftMeter CameraMeter = new LiftMeter();
        private static readonly LiftMeter BodyMeter = new LiftMeter();
        private static Transform _liftSource;
        private static int _liftFrame = -1;

        internal static void UpdateLift(Animator animator, LiftSource mode)
        {
            // Several patched behaviours may run in one frame; measure only once.
            if (_liftFrame == Time.frameCount)
                return;
            _liftFrame = Time.frameCount;
            float dt = Time.deltaTime;

            float camera = 0f;
            if (mode == LiftSource.Camera || mode == LiftSource.CameraAndHand)
                camera = CameraMeter.Update(PlayerDriver.GetCameraPosition(), dt);
            else
                CameraMeter.Reset();

            float body = 0f;
            if (mode != LiftSource.Camera && animator != null)
            {
                Transform source = mode == LiftSource.Foot
                    ? animator.GetBoneTransform(HumanBodyBones.LeftFoot)
                    : PickHoldingHand(animator);
                if (source != _liftSource)
                {
                    _liftSource = source;
                    BodyMeter.Reset();
                }
                if (source != null)
                    body = BodyMeter.Update(source.position, dt);
            }
            else
            {
                _liftSource = null;
                BodyMeter.Reset();
            }

            // Signed strengths: positive = upward acceleration (presses into the seat).
            float signal = Mathf.Abs(camera) >= Mathf.Abs(body) ? camera : body;
            HapticMixer.ReportLift(Mathf.Abs(signal), signal >= 0f ? 1f : -1f);
            if (Mathf.Abs(signal) > 0.05f)
                LogSignal("Lift (" + (signal == camera ? "camera" : "body") + ")",
                    Mathf.Abs(signal), Mathf.Abs(signal), 0f);
        }

        // The hand closest to the player holds them; a 25 % margin keeps the
        // choice stable while the other hand passes by.
        private static Transform PickHoldingHand(Animator animator)
        {
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (left == null || right == null)
                return left ?? right;
            Vector3 camera = PlayerDriver.GetCameraPosition();
            float dl = Vector3.Distance(left.position, camera);
            float dr = Vector3.Distance(right.position, camera);
            if (_liftSource == left && dl <= dr * 1.25f)
                return left;
            if (_liftSource == right && dr <= dl * 1.25f)
                return right;
            return dl <= dr ? left : right;
        }

        internal static void ResetLift()
        {
            _liftSource = null;
            CameraMeter.Reset();
            BodyMeter.Reset();
            HapticMixer.ReportLift(0f, 0f);
        }

        private sealed class LiftMeter
        {
            private const float SettleSeconds = 0.4f;
            private const float RestartSettleSeconds = 0.15f;
            private Vector3 _last;
            private float _velocity;
            private float _acceleration;
            private float _settle;
            private bool _hasData;
            private float _rawVelocity;
            private bool _hasRawVelocity;

            internal void Reset()
            {
                _hasData = false;
                _hasRawVelocity = false;
                _velocity = 0f;
                _acceleration = 0f;
            }

            // Returns a signed 0..1 strength (sign = direction of acceleration).
            internal float Update(Vector3 position, float dt)
            {
                if (!_hasData || dt <= 0f)
                {
                    _last = position;
                    _hasData = true;
                    _settle = SettleSeconds;
                    return 0f;
                }

                Vector3 delta = position - _last;
                _last = position;
                float velocity = delta.y / dt;
                float previous = _velocity;
                float previousRaw = _rawVelocity;
                bool hadRaw = _hasRawVelocity;
                _rawVelocity = velocity;
                _hasRawVelocity = true;
                // Camera cuts and scene transitions (the player is moved during a
                // fade to black) look like huge jumps: restart and settle again.
                // The jump test compares raw frame velocities, so steady motion
                // after a restart is not mistaken for another jump.
                if (delta.magnitude > HapticTuning.LiftTeleportDistance ||
                    (hadRaw && Mathf.Abs(velocity - previousRaw) / dt >
                        HapticTuning.LiftMaxPlausibleAcceleration * 4f))
                {
                    Restart(0f);
                    _hasRawVelocity = false;
                    return 0f;
                }

                _velocity = Smooth(_velocity, velocity, dt, 0.06f, 0.06f);
                float acceleration = (_velocity - previous) / dt;
                _acceleration = Smooth(_acceleration, acceleration, dt, 0.08f, 0.08f);
                if (Mathf.Abs(_acceleration) > HapticTuning.LiftMaxPlausibleAcceleration)
                {
                    Plugin.LogDebug("Lift ignored: implausible acceleration " +
                        Mathf.Abs(_acceleration).ToString("0.0") + " m/s^2 (scene transition?).");
                    // Continue from the current motion so a steady carry is not
                    // treated as a fresh jump on the next frame.
                    Restart(_velocity);
                    return 0f;
                }

                _settle -= dt;
                if (_settle > 0f)
                    return 0f;

                float full = HapticTuning.LiftAccelerationForFullResponse;
                float threshold = full * 0.08f;
                float magnitude = Mathf.Abs(_acceleration);
                float strength = magnitude <= threshold ? 0f :
                    Mathf.Clamp01((magnitude - threshold) / (full - threshold));
                return _acceleration >= 0f ? strength : -strength;
            }

            // Mid-interaction restarts only need a short pause; the long settle is
            // reserved for the very start (scene fade-in).
            private void Restart(float velocity)
            {
                _velocity = velocity;
                _acceleration = 0f;
                _settle = RestartSettleSeconds;
            }
        }

        // Full reset (scene change): every interaction signal and the held state.
        internal static void ResetAll()
        {
            ResetPet();
            ResetLap();
            ResetMassage();
            ResetLift();
            GrabHaptics.ForceRelease();
        }

        internal static void ResetPet()
        {
            if (!PetLeft.HasData && !PetRight.HasData && _petSmoothed == 0f)
                return;
            PetLeft.Reset();
            PetRight.Reset();
            _petSmoothed = 0f;
            _petSide = 0f;
            HapticMixer.ReportPet(0f, 0f);
        }

        internal static void ResetLap()
        {
            _lapBreathPhase = 0f;
            if (!LapLeft.HasData && !LapRight.HasData && _lapSmoothed == 0f)
                return;
            LapLeft.Reset();
            LapRight.Reset();
            _lapSmoothed = 0f;
            HapticMixer.ReportLapCaress(0f);
        }

        internal static void ResetMassage()
        {
            if (!MassageFoot.HasData && _massageSmoothed == 0f)
                return;
            MassageFoot.Reset();
            _massageSmoothed = 0f;
            HapticMixer.ReportMassagePressure(0f);
        }

        private static bool IsInState(Animator animator, params int[] hashes)
        {
            int current = animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            int next = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0).shortNameHash : 0;
            foreach (int hash in hashes)
            {
                if (current == hash || next == hash)
                    return true;
            }
            return false;
        }

        private static bool TryGetBones(Animator animator, HumanBodyBones a, HumanBodyBones b,
            out Transform first, out Transform second)
        {
            first = animator.GetBoneTransform(a);
            second = animator.GetBoneTransform(b);
            if (first != null && second != null)
                return true;
            LogMissingBones();
            return false;
        }

        private static void LogMissingBones()
        {
            if (_missingBonesLogged)
                return;
            _missingBonesLogged = true;
            Plugin.Log?.LogWarning("Giantess animator bones were not found; interaction haptics are unavailable.");
        }

        private static float Smooth(float current, float target, float dt, float attack, float release)
        {
            float tau = target > current ? attack : release;
            return current + (target - current) * (1f - Mathf.Exp(-Mathf.Max(dt, 0f) / tau));
        }

        private static void LogSignal(string name, float raw, float smoothed, float distance)
        {
            double now = Plugin.GetSessionTimeSeconds();
            if (now < _nextLogAt)
                return;
            _nextLogAt = now + 1.0;
            Plugin.LogDebug(name + " signal: raw=" + raw.ToString("0.00") + ", smoothed=" +
                smoothed.ToString("0.00") + ", distance=" + distance.ToString("0.00") + ".");
        }

        // Self-normalizing speed meter: the value is the current speed divided by a
        // slowly decaying peak, so the output is scale independent.
        private sealed class SpeedNormalizer
        {
            private float _peak;
            private float _eventMaxPeak;

            internal float Normalize(float speed, float dt)
            {
                // Both references forget over a few seconds, so a fast approach motion
                // does not keep the slower stroking loop that follows artificially weak.
                _eventMaxPeak = Mathf.Max(speed, _eventMaxPeak * Mathf.Exp(-Mathf.Max(dt, 0f) / 4f));
                _peak = Mathf.Max(speed, Mathf.Lerp(_peak, speed, Mathf.Clamp01(dt * 0.6f)));
                // A floor relative to the largest motion seen keeps tiny jitter quiet.
                float floor = Mathf.Max(1e-4f, _eventMaxPeak * 0.15f);
                return Mathf.Clamp01(speed / Mathf.Max(_peak, floor));
            }

            internal void Reset()
            {
                _peak = 0f;
                _eventMaxPeak = 0f;
            }
        }

        private sealed class HandTracker
        {
            private readonly SpeedNormalizer _speed = new SpeedNormalizer();
            private Vector3 _lastRelative;
            private float _closest = float.MaxValue;
            internal bool HasData;
            internal float Distance = float.MaxValue;

            // Returns motion (relative to the player) multiplied by proximity.
            internal float Update(Vector3 hand, Vector3 player, float dt)
            {
                Vector3 relative = hand - player;
                Distance = relative.magnitude;
                if (!HasData || dt <= 0f)
                {
                    _lastRelative = relative;
                    _closest = Distance;
                    HasData = true;
                    return 0f;
                }

                float speed = (relative - _lastRelative).magnitude / dt;
                _lastRelative = relative;

                // The closest distance seen relaxes slowly so a hand that moved away
                // for good is no longer treated as touching.
                _closest = Mathf.Min(Distance, _closest * (1f + 0.02f * dt));
                float far = _closest * HapticTuning.InteractionProximityFalloffRatio;
                float proximity = Mathf.InverseLerp(far, _closest, Distance);

                return _speed.Normalize(speed, dt) * proximity;
            }

            internal void Reset()
            {
                _speed.Reset();
                _closest = float.MaxValue;
                Distance = float.MaxValue;
                HasData = false;
            }
        }

        private sealed class FootTracker
        {
            private readonly SpeedNormalizer _linear = new SpeedNormalizer();
            private readonly SpeedNormalizer _angular = new SpeedNormalizer();
            private Vector3 _lastWorld;
            private Vector3 _lastRelative;
            private Quaternion _lastRotation;
            internal bool HasData;
            internal float Distance;

            // The player may be attached to the foot, so world motion, motion
            // relative to the player and foot rotation (flexing) are all considered.
            internal float Update(Transform foot, Vector3 player, float dt)
            {
                Vector3 world = foot.position;
                Vector3 relative = world - player;
                Quaternion rotation = foot.rotation;
                Distance = relative.magnitude;
                if (!HasData || dt <= 0f)
                {
                    _lastWorld = world;
                    _lastRelative = relative;
                    _lastRotation = rotation;
                    HasData = true;
                    return 0f;
                }

                float linear = Mathf.Max(
                    (world - _lastWorld).magnitude,
                    (relative - _lastRelative).magnitude) / dt;
                float angular = Quaternion.Angle(_lastRotation, rotation) / dt;
                _lastWorld = world;
                _lastRelative = relative;
                _lastRotation = rotation;

                return Mathf.Max(_linear.Normalize(linear, dt), _angular.Normalize(angular, dt));
            }

            internal void Reset()
            {
                _linear.Reset();
                _angular.Reset();
                HasData = false;
            }
        }
    }

    // Menu pickup (Akipo "E" grab, Twins background grab): the "grab" animation
    // message attaches the player to the hand. While held, lifting is measured and
    // pokes are detected as quick dips of a giantess hand toward the player.
    internal static class GrabHaptics
    {
        private const double PokeCooldownSeconds = 0.3;
        private static readonly System.Collections.Generic.Dictionary<int, PokeDetector> Detectors =
            new System.Collections.Generic.Dictionary<int, PokeDetector>();
        private static bool _held;
        private static Animator _holder;

        internal static void OnGrab(GiantessAI giantessAI)
        {
            _held = true;
            _holder = giantessAI?.animator;
            InteractionHaptics.ResetLift();
            Detectors.Clear();
            HapticMixer.ScheduleImpact("Grabbed by hand", HapticTuning.GrabIntensity, 350, 0f,
                1.0f, 0.9f, 0.7f, 0.5f, 0.4f);
        }

        internal static void OnEnd(GiantessAI giantessAI)
        {
            if (!_held)
                return;
            // With the Twins, only the giantess that holds the player ends the hold.
            Animator ending = giantessAI?.animator;
            if (_holder != null && ending != null && ending != _holder)
                return;
            _held = false;
            _holder = null;
            Detectors.Clear();
            InteractionHaptics.ResetLift();
        }

        internal static void ForceRelease()
        {
            _held = false;
            _holder = null;
            Detectors.Clear();
        }

        internal static void Update(GiantessAI giantessAI)
        {
            if (!_held)
                return;
            Animator animator = giantessAI?.animator;
            if (animator == null)
                return;
            // Only the giantess that picked the player up carries them (Twins).
            if (_holder == null || animator == _holder)
                InteractionHaptics.UpdateLift(animator, InteractionHaptics.LiftSource.HoldingHand);
            int id = animator.GetInstanceID();
            if (!Detectors.TryGetValue(id, out PokeDetector detector))
            {
                detector = new PokeDetector();
                Detectors.Add(id, detector);
            }

            // Measured from the player's body, not the camera: the menu swings the
            // camera (e.g. outfit tab), which must not look like a poking hand.
            Vector3 camera = PlayerDriver.GetPlayerPosition();
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftIndexDistal) ??
                animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightIndexDistal) ??
                animator.GetBoneTransform(HumanBodyBones.RightHand);
            detector.Check(left, camera, 0);
            detector.Check(right, camera, 1);
        }

        // A poke is a local distance minimum that is clearly closer than where the
        // hand was a moment earlier. The holding hand keeps a constant distance and
        // never produces such a dip.
        private sealed class PokeDetector
        {
            private readonly float[] _last = { -1f, -1f };
            private readonly bool[] _approaching = new bool[2];
            private readonly float[] _recentFar = new float[2];
            private double _lastPokeAt = -10.0;

            internal void Check(Transform hand, Vector3 camera, int index)
            {
                if (hand == null)
                    return;
                float distance = Vector3.Distance(hand.position, camera);
                float last = _last[index];
                _last[index] = distance;
                if (last < 0f)
                {
                    _recentFar[index] = distance;
                    return;
                }

                // Remember the farther position, slowly forgetting it (about 1 s).
                _recentFar[index] = Mathf.Max(distance,
                    Mathf.Lerp(_recentFar[index], distance, Mathf.Clamp01(Time.deltaTime)));

                bool approaching = distance < last;
                bool turnedBack = _approaching[index] && !approaching;
                _approaching[index] = approaching;
                if (!turnedBack)
                    return;

                double now = Plugin.GetSessionTimeSeconds();
                float depth = _recentFar[index] > 0f ? 1f - last / _recentFar[index] : 0f;
                if (depth < HapticTuning.PokeMinimumDipRatio || now - _lastPokeAt < PokeCooldownSeconds)
                    return;

                _lastPokeAt = now;
                _recentFar[index] = distance;
                float strength = HapticTuning.PokeIntensity * Mathf.Clamp01(0.6f + depth);
                HapticMixer.ScheduleImpact("Poke (dip " + depth.ToString("0.00") + ")", strength, 260,
                    FootstepHaptics.GetSourceSide(hand.position), 0.3f, 0.7f, 1.0f, 0.6f, 0.2f);
            }
        }
    }

    [HarmonyPatch(typeof(GiantessBehaviourGrabPlayer), "OnMessageReceived")]
    internal static class GrabPlayerMessageHaptics
    {
        [HarmonyPostfix]
        private static void Postfix(GiantessAI __0, string __1)
        {
            if (__1 == "grab")
                GrabHaptics.OnGrab(__0);
        }
    }

    [HarmonyPatch(typeof(GiantessBehaviourGrabPlayerBackground), "OnMessageReceived")]
    internal static class GrabPlayerBackgroundMessageHaptics
    {
        [HarmonyPostfix]
        private static void Postfix(GiantessAI __0, string __1)
        {
            if (__1 == "grab")
                GrabHaptics.OnGrab(__0);
        }
    }

    [HarmonyPatch(typeof(GiantessBehaviourGrabPlayer), "BehaviourUpdate")]
    internal static class GrabPlayerUpdateHaptics
    {
        [HarmonyPostfix]
        private static void Postfix(GiantessAI __0) => GrabHaptics.Update(__0);
    }

    [HarmonyPatch(typeof(GiantessBehaviourGrabPlayerBackground), "BehaviourUpdate")]
    internal static class GrabPlayerBackgroundUpdateHaptics
    {
        [HarmonyPostfix]
        private static void Postfix(GiantessAI __0) => GrabHaptics.Update(__0);
    }

    [HarmonyPatch(typeof(GiantessBehaviourGrabPlayer), "BehaviourEnd")]
    internal static class GrabPlayerEndHaptics
    {
        [HarmonyPrefix]
        private static void Prefix(GiantessAI __0) => GrabHaptics.OnEnd(__0);
    }

    [HarmonyPatch(typeof(GiantessBehaviourGrabPlayerBackground), "BehaviourEnd")]
    internal static class GrabPlayerBackgroundEndHaptics
    {
        [HarmonyPrefix]
        private static void Prefix(GiantessAI __0) => GrabHaptics.OnEnd(__0);
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventPetAkipo), "BehaviourUpdate")]
    internal static class PetHaptics
    {
        [HarmonyPostfix]
        private static void Postfix(GiantessAI __0) => InteractionHaptics.UpdatePet(__0);
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventPetAkipo), "BehaviourEnd")]
    internal static class PetEndHaptics
    {
        [HarmonyPrefix]
        private static void Prefix() => InteractionHaptics.ResetPet();
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventLapAkipo), "BehaviourUpdate")]
    internal static class LapHaptics
    {
        [HarmonyPostfix]
        private static void Postfix(GiantessAI __0)
        {
            InteractionHaptics.UpdateLap(__0);
            InteractionHaptics.UpdateLift(__0?.animator, InteractionHaptics.LiftSource.CameraAndHand);
        }
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventLapAkipo), "BehaviourEnd")]
    internal static class LapEndHaptics
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            InteractionHaptics.ResetLap();
            InteractionHaptics.ResetLift();
        }
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventHugAkipo), "BehaviourUpdate")]
    internal static class HugLiftHaptics
    {
        [HarmonyPostfix]
        private static void Postfix(GiantessAI __0) =>
            InteractionHaptics.UpdateLift(__0?.animator, InteractionHaptics.LiftSource.CameraAndHand);
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventHugAkipo), "BehaviourEnd")]
    internal static class HugLiftEndHaptics
    {
        [HarmonyPrefix]
        private static void Prefix() => InteractionHaptics.ResetLift();
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventFootMassageAkipo), "BehaviourUpdate")]
    internal static class FootMassageHaptics
    {
        [HarmonyPostfix]
        private static void Postfix(GiantessAI __0)
        {
            InteractionHaptics.UpdateFootMassage(__0);
            InteractionHaptics.UpdateLift(__0?.animator, InteractionHaptics.LiftSource.Foot);
        }
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventFootMassageAkipo), "BehaviourEnd")]
    internal static class FootMassageEndHaptics
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            InteractionHaptics.ResetMassage();
            InteractionHaptics.ResetLift();
        }
    }

    // Foot massage "step" (stomp on the player, stronger with the requested level)
    // and "crush" (shield break) animation events become whole-body hits.
    [HarmonyPatch(typeof(GiantessBehaviourEventFootMassageAkipo), "OnMessageReceived")]
    internal static class FootMassageHitHaptics
    {
        private static readonly FieldInfo CurrentDamageField =
            AccessTools.Field(typeof(GiantessBehaviourEventFootMassageAkipo), "currentDamage");
        private static readonly FieldInfo IncreaseDamageField =
            AccessTools.Field(typeof(GiantessBehaviourEventFootMassageAkipo), "increaseDamage");

        [HarmonyPrefix]
        private static void Prefix(GiantessBehaviourEventFootMassageAkipo __instance, string __1)
        {
            if (__1 == "step")
            {
                int level = GetStepLevel(__instance);
                float intensity = HapticTuning.FootMassageStepIntensity * (0.55f + 0.15f * level);
                // +100 ms compared to a plain impact gives the motors time to spin up.
                HapticMixer.ScheduleImpact("Foot massage step (level " + level + ")",
                    intensity, 480 + 40 * level, 0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.6f);
            }
            else if (__1 == "crush")
            {
                HapticMixer.ScheduleImpact("Foot massage crush",
                    HapticTuning.FootMassageCrushIntensity, 800, 0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f);
            }
        }

        private static int GetStepLevel(GiantessBehaviourEventFootMassageAkipo instance)
        {
            if (instance == null || CurrentDamageField == null || IncreaseDamageField == null)
                return 0;
            float damage = Convert.ToSingle(CurrentDamageField.GetValue(instance));
            float step = Convert.ToSingle(IncreaseDamageField.GetValue(instance));
            if (step <= 0f)
                return 0;
            return Mathf.Clamp(Mathf.RoundToInt(damage / step), 0, 3);
        }
    }
}
