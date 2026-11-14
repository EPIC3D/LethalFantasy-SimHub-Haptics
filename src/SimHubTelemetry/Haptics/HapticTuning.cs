using BepInEx.Configuration;
using UnityEngine;

namespace MyFirstPlugin
{
    // Central BepInEx settings for tuning event strength, zone balance, and
    // propagation without changing code. Values are read at runtime and clamped
    // again at their use sites.
    internal static class HapticTuning
    {
        private static ConfigEntry<float> _footstepIntensityMultiplier;
        private static ConfigEntry<float> _footstepSeatGain;
        private static ConfigEntry<float> _footstepLowerBackGain;
        private static ConfigEntry<float> _footstepMidBackGain;
        private static ConfigEntry<float> _footstepUpperBackGain;
        private static ConfigEntry<float> _footstepBassGain;
        private static ConfigEntry<float> _zoneDelaySeconds;
        private static ConfigEntry<float> _growthRateForFullResponse;
        private static ConfigEntry<float> _growthIntensityAt30Meters;
        private static ConfigEntry<float> _growthIntensityAt90Meters;
        private static ConfigEntry<float> _growthIntensityAt1440Meters;
        private static ConfigEntry<float> _growthIntensityAt23040Meters;
        private static ConfigEntry<float> _growthBassGain;
        private static ConfigEntry<float> _heartbeatMotorIntensity;
        private static ConfigEntry<float> _elevatedHeartbeatMotorIntensity;
        private static ConfigEntry<float> _heartbeatPulseDurationMs;
        private static ConfigEntry<float> _trapExplosionIntensity;
        private static ConfigEntry<float> _stompHitIntensity;
        private static ConfigEntry<float> _playerDamageIntensity;
        private static ConfigEntry<float> _flyingStunIntensity;
        private static ConfigEntry<float> _stunKnockupIntensity;
        private static ConfigEntry<float> _knockupIntensity;

        internal static void Bind(ConfigFile config)
        {
            _footstepIntensityMultiplier = config.Bind(
                "Haptics - Footsteps", "IntensityMultiplier", 2.0f,
                "Overall footstep strength multiplier (0-5).");
            _footstepSeatGain = config.Bind(
                "Haptics - Footsteps", "SeatGain", 1.0f,
                "Seat motor gain for footsteps (0-2).");
            _footstepLowerBackGain = config.Bind(
                "Haptics - Footsteps", "LowerBackGain", 1.35f,
                "Lower-back motor gain for footsteps (0-2).");
            _footstepMidBackGain = config.Bind(
                "Haptics - Footsteps", "MidBackGain", 1.0f,
                "Mid-back motor gain for footsteps (0-2).");
            _footstepUpperBackGain = config.Bind(
                "Haptics - Footsteps", "UpperBackGain", 0.9f,
                "Upper-back motor gain for footsteps (0-2).");
            _footstepBassGain = config.Bind(
                "Haptics - Footsteps", "BassGain", 0.85f,
                "Spare Bass Shaker gain for footsteps (0-2).");
            _zoneDelaySeconds = config.Bind(
                "Haptics - Propagation", "DelayBetweenZonesSeconds", 0.06f,
                "Delay between adjacent motor zones in a traveling wave (0-0.2 seconds).");

            _growthRateForFullResponse = config.Bind(
                "Haptics - Growth", "ScaleRateForFullResponse", 0.05f,
                "Giantess scale change per second that reaches the full size-based growth level.");
            _growthIntensityAt30Meters = config.Bind(
                "Haptics - Growth", "IntensityAt30Meters", 0.01f,
                "Growth intensity cap at 30 meters (0-1).");
            _growthIntensityAt90Meters = config.Bind(
                "Haptics - Growth", "IntensityAt90Meters", 0.05f,
                "Growth intensity cap at 90 meters (0-1).");
            _growthIntensityAt1440Meters = config.Bind(
                "Haptics - Growth", "IntensityAt1440Meters", 0.50f,
                "Growth intensity cap at 1440 meters (0-1).");
            _growthIntensityAt23040Meters = config.Bind(
                "Haptics - Growth", "IntensityAt23040Meters", 1.0f,
                "Growth intensity cap at 23040 meters (0-1).");
            _growthBassGain = config.Bind(
                "Haptics - Growth", "BassGain", 0.18f,
                "Spare Bass Shaker gain for growth (0-1).");

            _heartbeatMotorIntensity = config.Bind(
                "Haptics - Heartbeat", "MotorIntensity", 0.28f,
                "Peak normalized heartbeat intensity for a normal heartbeat on the active side (0-1).");
            _elevatedHeartbeatMotorIntensity = config.Bind(
                "Haptics - Heartbeat", "ElevatedMotorIntensity", 0.30f,
                "Peak normalized heartbeat intensity for an elevated heartbeat on the active side (0-1).");
            _heartbeatPulseDurationMs = config.Bind(
                "Haptics - Heartbeat", "PulseDurationMs", 180f,
                "Duration of each heartbeat haptic pulse, following the game's beat timing (30-300 ms).");

            _trapExplosionIntensity = config.Bind(
                "Haptics - Impacts", "TrapExplosionIntensity", 0.40f,
                "Normalized trap explosion pulse strength (0-1).");
            _stompHitIntensity = config.Bind(
                "Haptics - Impacts", "StompHitIntensity", 0.55f,
                "Normalized player stomp-hit pulse strength (0-1).");
            _playerDamageIntensity = config.Bind(
                "Haptics - Impacts", "PlayerDamageIntensity", 0.30f,
                "Normalized player damage pulse strength (0-1).");
            _flyingStunIntensity = config.Bind(
                "Haptics - Impacts", "FlyingStunIntensity", 0.42f,
                "Normalized flying-stun pulse strength (0-1).");
            _stunKnockupIntensity = config.Bind(
                "Haptics - Impacts", "StunKnockupIntensity", 0.36f,
                "Normalized stun-knockup pulse strength (0-1).");
            _knockupIntensity = config.Bind(
                "Haptics - Impacts", "KnockupIntensity", 0.30f,
                "Normalized knockup pulse strength (0-1).");
        }

        internal static float FootstepIntensityMultiplier =>
            Read(_footstepIntensityMultiplier, 2.0f, 0f, 5f);
        internal static float FootstepSeatGain => Read(_footstepSeatGain, 1f, 0f, 2f);
        internal static float FootstepLowerBackGain => Read(_footstepLowerBackGain, 1.35f, 0f, 2f);
        internal static float FootstepMidBackGain => Read(_footstepMidBackGain, 1f, 0f, 2f);
        internal static float FootstepUpperBackGain => Read(_footstepUpperBackGain, 0.9f, 0f, 2f);
        internal static float FootstepBassGain => Read(_footstepBassGain, 0.85f, 0f, 2f);
        internal static float ZoneDelaySeconds => Read(_zoneDelaySeconds, 0.06f, 0f, 0.2f);
        internal static float GrowthRateForFullResponse => Read(_growthRateForFullResponse, 0.05f, 0.001f, 1f);
        internal static float GrowthIntensityAt30Meters => Read(_growthIntensityAt30Meters, 0.01f, 0f, 1f);
        internal static float GrowthIntensityAt90Meters => Read(_growthIntensityAt90Meters, 0.05f, 0f, 1f);
        internal static float GrowthIntensityAt1440Meters => Read(_growthIntensityAt1440Meters, 0.5f, 0f, 1f);
        internal static float GrowthIntensityAt23040Meters => Read(_growthIntensityAt23040Meters, 1f, 0f, 1f);
        internal static float GrowthBassGain => Read(_growthBassGain, 0.18f, 0f, 1f);
        internal static float HeartbeatMotorIntensity => Read(_heartbeatMotorIntensity, 0.28f, 0f, 1f);
        internal static float ElevatedHeartbeatMotorIntensity =>
            Read(_elevatedHeartbeatMotorIntensity, 0.30f, 0f, 1f);
        internal static int HeartbeatPulseDurationMs =>
            Mathf.RoundToInt(Read(_heartbeatPulseDurationMs, 100f, 30f, 300f));
        internal static float TrapExplosionIntensity => Read(_trapExplosionIntensity, 0.4f, 0f, 1f);
        internal static float StompHitIntensity => Read(_stompHitIntensity, 0.55f, 0f, 1f);
        internal static float PlayerDamageIntensity => Read(_playerDamageIntensity, 0.3f, 0f, 1f);
        internal static float FlyingStunIntensity => Read(_flyingStunIntensity, 0.42f, 0f, 1f);
        internal static float StunKnockupIntensity => Read(_stunKnockupIntensity, 0.36f, 0f, 1f);
        internal static float KnockupIntensity => Read(_knockupIntensity, 0.3f, 0f, 1f);

        internal static float GetGrowthMaxIntensity(float heightMeters)
        {
            float intensityAt30 = GrowthIntensityAt30Meters;
            float intensityAt90 = Mathf.Max(intensityAt30, GrowthIntensityAt90Meters);
            float intensityAt1440 = Mathf.Max(intensityAt90, GrowthIntensityAt1440Meters);
            float intensityAt23040 = Mathf.Max(intensityAt1440, GrowthIntensityAt23040Meters);

            if (heightMeters <= 30f)
                return intensityAt30;
            if (heightMeters <= 90f)
                return InterpolateGrowthIntensity(heightMeters, 30f, 90f, intensityAt30, intensityAt90);
            if (heightMeters <= 1440f)
                return InterpolateGrowthIntensity(heightMeters, 90f, 1440f, intensityAt90, intensityAt1440);
            return InterpolateGrowthIntensity(heightMeters, 1440f, 23040f, intensityAt1440, intensityAt23040);
        }

        private static float InterpolateGrowthIntensity(
            float heightMeters,
            float startHeight,
            float endHeight,
            float startIntensity,
            float endIntensity)
        {
            float progress = Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(startHeight, endHeight, heightMeters));
            return Mathf.Lerp(startIntensity, endIntensity, progress);
        }

        private static float Read(
            ConfigEntry<float> entry,
            float fallback,
            float minimum,
            float maximum)
        {
            return entry == null ? fallback : Mathf.Clamp(entry.Value, minimum, maximum);
        }
    }
}
