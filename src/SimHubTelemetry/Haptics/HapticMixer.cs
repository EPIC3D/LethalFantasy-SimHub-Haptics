using System;
using System.Collections.Generic;
using System.Threading;

namespace MyFirstPlugin
{
    // Mixes event pulses into normalized HF8 outputs on the UDP sender thread.
    // Footsteps travel between motor zones with delays, while growth creates a
    // symmetric rumble with a short hold and release.
    internal static class HapticMixer
    {
        private const float DirectionBalance = 0.35f;
        private const float GrowthHoldSeconds = 0.06f;
        private const float GrowthReleaseSeconds = 0.20f;
        private const double GrowthHistorySeconds = 0.75;

        private static readonly object Sync = new object();
        private static readonly List<FootstepPulse> FootstepPulses = new List<FootstepPulse>();
        private static readonly List<HeartbeatPulse> HeartbeatPulses = new List<HeartbeatPulse>();
        private static readonly List<GrowthSample> GrowthSamples = new List<GrowthSample>();
        private static bool _outputsWereActive;
        private static int _footstepPatchCalls;
        private static int _growthScaleUpdateCalls;
        private static int _growthEvents;
        private static int _heartbeatEvents;

        internal static void RecordFootstepPatchCall()
        {
            Interlocked.Increment(ref _footstepPatchCalls);
        }

        internal static void RecordGrowthScaleUpdate()
        {
            Interlocked.Increment(ref _growthScaleUpdateCalls);
        }

        internal static void ScheduleFootstep(float intensity, int durationMs, float side)
        {
            ScheduleImpact(
                "Footstep",
                intensity,
                durationMs,
                side,
                HapticTuning.FootstepSeatGain,
                HapticTuning.FootstepLowerBackGain,
                HapticTuning.FootstepMidBackGain,
                HapticTuning.FootstepUpperBackGain,
                HapticTuning.FootstepBassGain);
        }

        internal static void ScheduleImpact(
            string category,
            float intensity,
            int durationMs,
            float side,
            float seatScale,
            float lowerBackScale,
            float midBackScale,
            float upperBackScale,
            float bassScale = 0f)
        {
            if (intensity <= 0f)
                return;

            lock (Sync)
            {
                // Record the event start, duration, and horizontal direction. Clamp
                // duration to protect the mixer from unrealistic inputs.
                FootstepPulses.Add(new FootstepPulse(
                    Plugin.GetSessionTimeSeconds(),
                    Clamp01(intensity),
                    Clamp(durationMs / 1000f, 0.1f, 2f),
                    Clamp(side, -1f, 1f),
                    seatScale,
                    lowerBackScale,
                    midBackScale,
                    upperBackScale,
                    bassScale));
            }

            Plugin.LogDebug(category + " haptic pulse scheduled: intensity=" +
                Clamp01(intensity).ToString("0.00") + ", duration=" + durationMs +
                " ms, side=" + Clamp(side, -1f, 1f).ToString("0.00") + ".");
        }

        internal static void ReportGrowth(float intensity)
        {
            lock (Sync)
            {
                // Preserve actual per-frame growth rhythm so delayed motor zones
                // can replay the measured signal as a traveling wave.
                Interlocked.Increment(ref _growthEvents);
                double sampleTime = Plugin.GetSessionTimeSeconds();
                GrowthSamples.Add(new GrowthSample(sampleTime, Clamp01(intensity)));
                for (int i = GrowthSamples.Count - 1; i >= 0; i--)
                {
                    if (sampleTime - GrowthSamples[i].Time > GrowthHistorySeconds)
                        GrowthSamples.RemoveAt(i);
                }
            }
        }

        internal static void ScheduleHeartbeatPulse(bool leftSide, bool elevated)
        {
            lock (Sync)
            {
                if (!_heartbeatEnabled)
                    return;
            }

            double startedAt = Plugin.GetSessionTimeSeconds();
            float duration = HapticTuning.HeartbeatPulseDurationMs / 1000f;

            lock (Sync)
            {
                if (!_heartbeatEnabled)
                    return;

                HeartbeatPulses.Add(new HeartbeatPulse(
                    startedAt,
                    duration,
                    elevated
                        ? HapticTuning.ElevatedHeartbeatMotorIntensity
                        : HapticTuning.HeartbeatMotorIntensity,
                    leftSide));
            }

            Interlocked.Increment(ref _heartbeatEvents);
        }

        // Continuous interaction signals; a value older than this is treated as zero
        // so a missed end event cannot leave motors running.
        private const double InteractionStaleSeconds = 0.25;
        private const double LapHistorySeconds = 1.5;
        private static readonly List<GrowthSample> LapSamples = new List<GrowthSample>();
        private static float _petIntensity;
        private static float _petSide;
        private static double _petUpdatedAt = -1.0;
        private static float _massagePressure;
        private static double _massageUpdatedAt = -1.0;

        internal static void ReportPet(float intensity, float side)
        {
            lock (Sync)
            {
                _petIntensity = Clamp01(intensity);
                _petSide = Clamp(side, -1f, 1f);
                _petUpdatedAt = Plugin.GetSessionTimeSeconds();
            }
        }

        internal static void ReportLapCaress(float intensity)
        {
            lock (Sync)
            {
                double sampleTime = Plugin.GetSessionTimeSeconds();
                LapSamples.Add(new GrowthSample(sampleTime, Clamp01(intensity)));
                for (int i = LapSamples.Count - 1; i >= 0; i--)
                {
                    if (sampleTime - LapSamples[i].Time > LapHistorySeconds)
                        LapSamples.RemoveAt(i);
                }
            }
        }

        private static float _liftStrength;
        private static float _liftDirection;
        private static double _liftUpdatedAt = -1.0;

        internal static void ReportLift(float strength, float direction)
        {
            lock (Sync)
            {
                _liftStrength = Clamp01(strength);
                _liftDirection = direction;
                _liftUpdatedAt = Plugin.GetSessionTimeSeconds();
            }
        }

        internal static void ReportMassagePressure(float pressure)
        {
            lock (Sync)
            {
                _massagePressure = Clamp01(pressure);
                _massageUpdatedAt = Plugin.GetSessionTimeSeconds();
            }
        }

        private static bool _heartbeatEnabled;

        internal static void SetHeartbeatEnabled(bool enabled)
        {
            lock (Sync)
            {
                _heartbeatEnabled = enabled;
                if (!enabled)
                    HeartbeatPulses.Clear();
            }
        }

        internal static string GetDiagnosticSummary()
        {
            return "patch calls: footsteps=" + Interlocked.CompareExchange(ref _footstepPatchCalls, 0, 0) +
                ", scale updates=" + Interlocked.CompareExchange(ref _growthScaleUpdateCalls, 0, 0) +
                ", positive growth samples=" + Interlocked.CompareExchange(ref _growthEvents, 0, 0) +
                ", heartbeat pulses=" + Interlocked.CompareExchange(ref _heartbeatEvents, 0, 0);
        }

        internal static void Tick(double now)
        {
            lock (Sync)
            {
                TickLocked(now);
            }
        }

        private static void TickLocked(double now)
        {
            float upperLeft = 0f;
            float upperRight = 0f;
            float midLeft = 0f;
            float midRight = 0f;
            float lowerLeft = 0f;
            float lowerRight = 0f;
            float seatLeft = 0f;
            float seatRight = 0f;
            float bass = 0f;

            // Evaluate each active footstep pulse independently. Remove expired
            // pulses and keep the stronger value per channel when events overlap.
            for (int i = FootstepPulses.Count - 1; i >= 0; i--)
            {
                FootstepPulse pulse = FootstepPulses[i];
                float age = (float)(now - pulse.StartedAt);
                if (age >= pulse.Duration + UpperBackDelay)
                {
                    FootstepPulses.RemoveAt(i);
                    continue;
                }

                AddFootstepZone(pulse, age, SeatDelay, pulse.Duration, pulse.SeatScale,
                    ref seatLeft, ref seatRight);
                AddFootstepZone(pulse, age, LowerBackDelay, pulse.Duration, pulse.LowerBackScale,
                    ref lowerLeft, ref lowerRight);
                AddFootstepZone(pulse, age, MidBackDelay, pulse.Duration, pulse.MidBackScale,
                    ref midLeft, ref midRight);
                AddFootstepZone(pulse, age, UpperBackDelay, pulse.Duration, pulse.UpperBackScale,
                    ref upperLeft, ref upperRight);

                // The Bass Shaker receives a reduced version of the base footstep
                // pulse and does not follow the upward HF8 zone delays.
                if (age >= 0f && age < pulse.Duration)
                    bass = Math.Max(bass, pulse.Intensity * pulse.BassScale *
                        GetEnvelope(age, pulse.Duration));
            }

            for (int i = HeartbeatPulses.Count - 1; i >= 0; i--)
            {
                HeartbeatPulse pulse = HeartbeatPulses[i];
                float age = (float)(now - pulse.StartedAt);
                if (age < 0f)
                    continue;
                if (age >= pulse.Duration)
                {
                    HeartbeatPulses.RemoveAt(i);
                    continue;
                }

                float envelope = GetEnvelope(age, pulse.Duration);
                float strength = pulse.MotorIntensity * envelope;
                if (pulse.LeftSide)
                {
                    upperLeft = Math.Max(upperLeft, strength);
                    midLeft = Math.Max(midLeft, strength);
                }
                else
                {
                    upperRight = Math.Max(upperRight, strength);
                    midRight = Math.Max(midRight, strength);
                }
            }

            // Read the measured growth signal at staggered past times to create
            // a bottom-to-top wave while retaining pauses and changing growth rate.
            float seatGrowth = GetGrowthIntensityAt(now - SeatDelay);
            float lowerGrowth = GetGrowthIntensityAt(now - LowerBackDelay);
            float midGrowth = GetGrowthIntensityAt(now - MidBackDelay);
            float upperGrowth = GetGrowthIntensityAt(now - UpperBackDelay);
            AddGrowthZone(upperGrowth, 1f, ref upperLeft, ref upperRight);
            AddGrowthZone(midGrowth, 1f, ref midLeft, ref midRight);
            AddGrowthZone(lowerGrowth, 1f, ref lowerLeft, ref lowerRight);
            AddGrowthZone(seatGrowth, 1f, ref seatLeft, ref seatRight);
            bass = Math.Max(bass, seatGrowth * HapticTuning.GrowthBassGain);

            // Petting: only the two upper-back motors, balanced toward the hand side.
            if (now - _petUpdatedAt < InteractionStaleSeconds && _petIntensity > 0f)
            {
                // Motors need a minimum drive to spin up, so any real stroke starts
                // from PetMotorFloor instead of near zero.
                float floor = HapticTuning.PetMotorFloor;
                float pet = _petIntensity < 0.02f ? 0f :
                    (floor + (1f - floor) * _petIntensity) * HapticTuning.PetIntensity;
                // Pan between the shoulders; the far side keeps at least 40 % so
                // both motors always run.
                upperLeft = Math.Max(upperLeft, Clamp01(pet * (0.7f - 0.3f * _petSide)));
                upperRight = Math.Max(upperRight, Clamp01(pet * (0.7f + 0.3f * _petSide)));
            }

            // Lap caress: whole body, the measured hand signal travels top to bottom.
            // Drop the lap history once reports have stopped (missed end event).
            if (LapSamples.Count > 0 && now - LapSamples[LapSamples.Count - 1].Time >
                InteractionStaleSeconds + HapticTuning.LapWaveDelaySeconds * 3f + 0.3)
                LapSamples.Clear();
            if (LapSamples.Count > 0)
            {
                float lapDelay = HapticTuning.LapWaveDelaySeconds;
                float lapGain = HapticTuning.LapIntensity;
                AddGrowthZone(GetSampleIntensityAt(LapSamples, now) * lapGain, 1f,
                    ref upperLeft, ref upperRight);
                AddGrowthZone(GetSampleIntensityAt(LapSamples, now - lapDelay) * lapGain, 1f,
                    ref midLeft, ref midRight);
                AddGrowthZone(GetSampleIntensityAt(LapSamples, now - lapDelay * 2f) * lapGain, 1f,
                    ref lowerLeft, ref lowerRight);
                AddGrowthZone(GetSampleIntensityAt(LapSamples, now - lapDelay * 3f) * lapGain, 1f,
                    ref seatLeft, ref seatRight);
            }

            // Lift / set-down: a G-force feel concentrated on the seat. Upward
            // acceleration presses harder than the lighter downward phase.
            if (now - _liftUpdatedAt < InteractionStaleSeconds && _liftStrength > 0f)
            {
                float lift = _liftStrength * HapticTuning.LiftIntensity *
                    (_liftDirection >= 0f ? 1f : 0.6f);
                AddGrowthZone(lift, 1.0f, ref seatLeft, ref seatRight);
                AddGrowthZone(lift, 0.7f, ref lowerLeft, ref lowerRight);
                AddGrowthZone(lift, 0.35f, ref midLeft, ref midRight);
                AddGrowthZone(lift, 0.15f, ref upperLeft, ref upperRight);
                bass = Math.Max(bass, lift * HapticTuning.LiftBassGain);
            }

            // Foot massage: every zone pulses together like rising and falling pressure.
            if (now - _massageUpdatedAt < InteractionStaleSeconds && _massagePressure > 0f)
            {
                float pressure = _massagePressure * HapticTuning.FootMassagePressureIntensity;
                AddGrowthZone(pressure, 1f, ref upperLeft, ref upperRight);
                AddGrowthZone(pressure, 1f, ref midLeft, ref midRight);
                AddGrowthZone(pressure, 1f, ref lowerLeft, ref lowerRight);
                AddGrowthZone(pressure, 1f, ref seatLeft, ref seatRight);
                bass = Math.Max(bass, pressure * HapticTuning.FootMassageBassGain);
            }

            // Each SimHub output receives an independent value in the 0..1 range.
            TelemetryInputs.BassShaker = Clamp01(bass);
            TelemetryInputs.HapticUpperBackLeft = Clamp01(upperLeft);
            TelemetryInputs.HapticUpperBackRight = Clamp01(upperRight);
            TelemetryInputs.HapticMidBackLeft = Clamp01(midLeft);
            TelemetryInputs.HapticMidBackRight = Clamp01(midRight);
            TelemetryInputs.HapticLowerBackLeft = Clamp01(lowerLeft);
            TelemetryInputs.HapticLowerBackRight = Clamp01(lowerRight);
            TelemetryInputs.HapticSeatLeft = Clamp01(seatLeft);
            TelemetryInputs.HapticSeatRight = Clamp01(seatRight);

            bool outputsAreActive = bass > 0f || upperLeft > 0f || upperRight > 0f ||
                midLeft > 0f || midRight > 0f || lowerLeft > 0f || lowerRight > 0f ||
                seatLeft > 0f || seatRight > 0f;
            if (outputsAreActive != _outputsWereActive)
            {
                _outputsWereActive = outputsAreActive;
                Plugin.LogDebug((outputsAreActive ? "Haptic outputs active: " :
                    "Haptic outputs returned to zero: ") + FormatOutputs());
            }
        }

        internal static void Reset()
        {
            lock (Sync)
            {
                FootstepPulses.Clear();
                HeartbeatPulses.Clear();
                GrowthSamples.Clear();
                LapSamples.Clear();
                _petIntensity = 0f;
                _petUpdatedAt = -1.0;
                _massagePressure = 0f;
                _massageUpdatedAt = -1.0;
                _liftStrength = 0f;
                _liftUpdatedAt = -1.0;
                _outputsWereActive = false;
                TelemetryInputs.BassShaker = 0f;
                TelemetryInputs.HapticUpperBackLeft = 0f;
                TelemetryInputs.HapticUpperBackRight = 0f;
                TelemetryInputs.HapticMidBackLeft = 0f;
                TelemetryInputs.HapticMidBackRight = 0f;
                TelemetryInputs.HapticLowerBackLeft = 0f;
                TelemetryInputs.HapticLowerBackRight = 0f;
                TelemetryInputs.HapticSeatLeft = 0f;
                TelemetryInputs.HapticSeatRight = 0f;
            }
        }

        private static void AddFootstepZone(
            FootstepPulse pulse,
            float age,
            float delay,
            float duration,
            float zoneScale,
            ref float left,
            ref float right)
        {
            float localAge = age - delay;
            if (localAge < 0f || localAge >= duration)
                return;

            // Each motor zone gets a delayed envelope relative to the event start;
            // the source side slightly adjusts the left/right balance.
            float strength = pulse.Intensity * zoneScale * GetEnvelope(localAge, duration);
            left = Math.Max(left, strength * (1f - DirectionBalance * pulse.Side));
            right = Math.Max(right, strength * (1f + DirectionBalance * pulse.Side));
        }

        private static void AddGrowthZone(
            float intensity,
            float zoneScale,
            ref float left,
            ref float right)
        {
            float strength = intensity * zoneScale;
            left = Math.Max(left, strength);
            right = Math.Max(right, strength);
        }

        private static float GetGrowthIntensityAt(double targetTime)
        {
            return GetSampleIntensityAt(GrowthSamples, targetTime);
        }

        private static float GetSampleIntensityAt(List<GrowthSample> samples, double targetTime)
        {
            GrowthSample latest = default;
            bool hasSample = false;
            for (int i = samples.Count - 1; i >= 0; i--)
            {
                if (samples[i].Time <= targetTime)
                {
                    latest = samples[i];
                    hasSample = true;
                    break;
                }
            }

            if (!hasSample)
                return 0f;

            float age = (float)(targetTime - latest.Time);
            if (age <= GrowthHoldSeconds)
                return latest.Intensity;

            float releaseProgress = (age - GrowthHoldSeconds) / GrowthReleaseSeconds;
            return latest.Intensity * (1f - SmoothStep(releaseProgress));
        }

        private static float GetEnvelope(float age, float duration)
        {
            // Hold pulse strength for the first three quarters, then use a
            // smoothstep fade over the final quarter to avoid an abrupt stop.
            float fadeStart = duration * 0.75f;
            if (age <= fadeStart)
                return 1f;

            return 1f - SmoothStep((age - fadeStart) / (duration - fadeStart));
        }

        private static float Clamp01(float value)
        {
            return Clamp(value, 0f, 1f);
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static float SmoothStep(float value)
        {
            float t = Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        private static string FormatOutputs()
        {
            return "Bass=" + TelemetryInputs.BassShaker.ToString("0.00") +
                ", UpperBackL=" + TelemetryInputs.HapticUpperBackLeft.ToString("0.00") +
                ", UpperBackR=" + TelemetryInputs.HapticUpperBackRight.ToString("0.00") +
                ", MidBackL=" + TelemetryInputs.HapticMidBackLeft.ToString("0.00") +
                ", MidBackR=" + TelemetryInputs.HapticMidBackRight.ToString("0.00") +
                ", LowerBackL=" + TelemetryInputs.HapticLowerBackLeft.ToString("0.00") +
                ", LowerBackR=" + TelemetryInputs.HapticLowerBackRight.ToString("0.00") +
                ", SeatL=" + TelemetryInputs.HapticSeatLeft.ToString("0.00") +
                ", SeatR=" + TelemetryInputs.HapticSeatRight.ToString("0.00") + ".";
        }

        private const float SeatDelay = 0f;
        private static float LowerBackDelay => HapticTuning.ZoneDelaySeconds;
        private static float MidBackDelay => HapticTuning.ZoneDelaySeconds * 2f;
        private static float UpperBackDelay => HapticTuning.ZoneDelaySeconds * 3f;

        private struct FootstepPulse
        {
            internal readonly double StartedAt;
            internal readonly float Intensity;
            internal readonly float Duration;
            internal readonly float Side;
            internal readonly float SeatScale;
            internal readonly float LowerBackScale;
            internal readonly float MidBackScale;
            internal readonly float UpperBackScale;
            internal readonly float BassScale;

            internal FootstepPulse(
                double startedAt,
                float intensity,
                float duration,
                float side,
                float seatScale,
                float lowerBackScale,
                float midBackScale,
                float upperBackScale,
                float bassScale)
            {
                StartedAt = startedAt;
                Intensity = intensity;
                Duration = duration;
                Side = side;
                SeatScale = seatScale;
                LowerBackScale = lowerBackScale;
                MidBackScale = midBackScale;
                UpperBackScale = upperBackScale;
                BassScale = bassScale;
            }
        }

        private struct GrowthSample
        {
            internal readonly double Time;
            internal readonly float Intensity;

            internal GrowthSample(double time, float intensity)
            {
                Time = time;
                Intensity = intensity;
            }
        }

        private struct HeartbeatPulse
        {
            internal readonly double StartedAt;
            internal readonly float Duration;
            internal readonly float MotorIntensity;
            internal readonly bool LeftSide;

            internal HeartbeatPulse(
                double startedAt,
                float duration,
                float motorIntensity,
                bool leftSide)
            {
                StartedAt = startedAt;
                Duration = duration;
                MotorIntensity = motorIntensity;
                LeftSide = leftSide;
            }
        }
    }
}
