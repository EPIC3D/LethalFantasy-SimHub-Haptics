using HarmonyLib;
using UnityEngine;

namespace MyFirstPlugin
{
    // Creates a normalized pulse from the game's footstep shake data. Intensity
    // depends on shake strength, scale, and distance falloff; foot position adds
    // a left/right motor bias for the mixer.
    [HarmonyPatch(typeof(Armature), "PlayFootstepScreenShake")]
    internal static class FootstepHaptics
    {
        private const float FullScaleShakeAmount = 0.9f;
        private const float MinimumIntensity = 0.15f;
        private static bool _shockwaveWarningLogged;

        [HarmonyPrefix]
        private static void Prefix(
            Armature __instance,
            float volumeModifier,
            float playerDistance,
            float scale)
        {
            HapticMixer.RecordFootstepPatchCall();

            if (__instance == null || __instance.giantess == null ||
                __instance.giantess.footStats == null)
                return;

            // Clamp values from the game settings to their supported range so
            // invalid or extreme values cannot distort the haptic output.
            float shakePower = Mathf.Clamp(__instance.giantess.footStats.screenShake_Power, 0.5f, 3f);
            float shakeRadius = Mathf.Clamp(__instance.giantess.footStats.screenShake_Radius, 0.5f, 3f);
            SetupManager setupManager = SetupManager.Instance;
            if (setupManager == null || setupManager.shockwaveRangeCalculation == null)
            {
                LogShockwaveWarning("Footstep shockwave range is unavailable; event skipped.");
                return;
            }

            // The shockwave range is used for intensity falloff and pulse duration;
            // do not create a misleading pulse if this value is invalid.
            float shockwaveRange = setupManager.shockwaveRangeCalculation.GetValue(scale);
            if (float.IsNaN(shockwaveRange) || float.IsInfinity(shockwaveRange) ||
                shockwaveRange <= 0f)
            {
                LogShockwaveWarning("Footstep shockwave range calculation returned an invalid value: " +
                    shockwaveRange + ".");
                return;
            }

            if (Armature.shakeFalloffCurve == null)
            {
                LogShockwaveWarning("Footstep shake falloff curve is unavailable; event skipped.");
                return;
            }

            // Use the game's camera-shake range and falloff to estimate the
            // strength of the visual shake event.
            float cameraRangeStart = 8f * shakeRadius * scale;
            float cameraRangeEnd = 36f * shakeRadius * scale;
            float cameraDistanceFactor = Mathf.InverseLerp(
                cameraRangeStart, cameraRangeEnd, playerDistance);
            float cameraShakeAmount = 0.25f *
                Armature.shakeFalloffCurve.Evaluate(cameraDistanceFactor) *
                volumeModifier * shakePower;
            float cameraShakeStrength = Mathf.Clamp01(cameraShakeAmount / FullScaleShakeAmount);

            // Convert the giantess game scale to meters, then smoothly increase
            // the target base intensity between 30 m and 1440 m.
            float heightMeters = scale * 30f;
            float heightProgress = Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(30f, 1440f, heightMeters));
            float closeIntensity = Mathf.Lerp(
                MinimumIntensity, 0.20f, cameraShakeStrength);
            float heightIntensity = Mathf.Lerp(closeIntensity, 1f, heightProgress);

            // Use the larger of the game shockwave range and giantess height so
            // the haptic distance scale does not become too small.
            float effectiveRange = Mathf.Max(shockwaveRange, heightMeters);
            float hapticDistanceFactor = Mathf.Clamp01(playerDistance / effectiveRange);
            float distanceFalloff = Armature.shakeFalloffCurve.Evaluate(hapticDistanceFactor);
            float intensity = cameraShakeAmount > 0f
                ? Mathf.Max(MinimumIntensity, heightIntensity * distanceFalloff)
                : 0f;

            // Duration increases with game scale, while the camera-shake distance
            // factor shortens the pulse at greater distances.
            float durationBlend = Mathf.Clamp(0.475f + scale * 0.025f, 0.5f, 1.25f);
            float durationSeconds = Mathf.Lerp(durationBlend, 0.25f, cameraDistanceFactor);
            int durationMs = Mathf.Max(1, Mathf.RoundToInt(durationSeconds * 1000f));
            float side = GetSourceSide(__instance.transform.position);
            HapticMixer.ScheduleFootstep(
                Mathf.Clamp01(intensity * HapticTuning.FootstepIntensityMultiplier),
                durationMs,
                side);

            Plugin.LogDebug("Footstep event: intensity=" + intensity.ToString("0.00") +
                ", duration=" + durationMs + " ms, distance=" + playerDistance.ToString("0.0") +
                " m, range=" + effectiveRange.ToString("0.0") + " m, side=" +
                side.ToString("0.00") + ".");
        }

        internal static float GetSourceSide(Vector3 sourcePosition)
        {
            if (PlayerDriver.LocalPlayer == null)
                return 0f;

            // Project world positions onto the horizontal plane so height
            // differences do not affect the left/right direction.
            Vector3 forward = PlayerDriver.GetCameraForwardDirection();
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return 0f;
            forward.Normalize();
            forward.Normalize();
            Vector3 toSource = sourcePosition - PlayerDriver.GetPlayerPosition();
            toSource.y = 0f;
            if (toSource.sqrMagnitude < 0.0001f)
                return 0f;

            // The dot product in the -1..1 range indicates whether the source is
            // to the player's left, centered, or to the right.
            return ImpactHaptics.GetSideFromDirection(toSource);
        }

        private static void LogShockwaveWarning(string message)
        {
            if (_shockwaveWarningLogged)
                return;

            Plugin.Log?.LogWarning(message);
            _shockwaveWarningLogged = true;
        }
    }
}
