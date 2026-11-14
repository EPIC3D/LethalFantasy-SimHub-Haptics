using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace MyFirstPlugin
{
    // Measures actual scale changes after Giantess.UpdateScale. It maps the
    // growth rate to a 0..1 intensity, so separate growth bursts and pauses
    // between them follow the game's state.
    [HarmonyPatch(typeof(Giantess), "UpdateScale")]
    internal static class GrowthHaptics
    {
        private const float MinimumScaleChange = 0.00001f;
        private static readonly Dictionary<int, float> LastScales = new Dictionary<int, float>();
        private static float _nextDebugLogAt;
        private static readonly Dictionary<int, FrameIntensity> CurrentIntensities =
            new Dictionary<int, FrameIntensity>();

        private struct FrameIntensity
        {
            internal readonly int Frame;
            internal readonly float Intensity;

            internal FrameIntensity(int frame, float intensity)
            {
                Frame = frame;
                Intensity = intensity;
            }
        }

        private static float GetStrongestCurrentIntensity()
        {
            int frame = Time.frameCount;
            float strongest = 0f;
            foreach (FrameIntensity value in CurrentIntensities.Values)
            {
                // Only values from this or the previous frame count as "current".
                if (frame - value.Frame <= 1 && value.Intensity > strongest)
                    strongest = value.Intensity;
            }

            return strongest;
        }

        [HarmonyPostfix]
        private static void Postfix(Giantess __instance)
        {
            HapticMixer.RecordGrowthScaleUpdate();

            if (__instance == null)
                return;

            int instanceId = __instance.GetInstanceID();
            float currentScale = __instance.scale;
            if (!LastScales.TryGetValue(instanceId, out float previousScale))
            {
                LastScales.Add(instanceId, currentScale);
                return;
            }

            float scaleChange = currentScale - previousScale;
            LastScales[instanceId] = currentScale;
            if (scaleChange <= MinimumScaleChange)
            {
                CurrentIntensities[instanceId] = new FrameIntensity(Time.frameCount, 0f);
                return;
            }

            // Unscaled delta time measures real-time growth even if gameplay time
            // is slowed; clamp the divisor to avoid division by a tiny value.
            float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            float growthRate = scaleChange / deltaTime;

            // Apply a curved response to the measured growth rate and scale-based
            // intensity target, both of which are configurable.
            float rateProgress = Mathf.Clamp01(growthRate / HapticTuning.GrowthRateForFullResponse);
            float rateCurve = Mathf.SmoothStep(0f, 1f, rateProgress);
            float heightMeters = currentScale * 30f;
            float maxIntensity = HapticTuning.GetGrowthMaxIntensity(heightMeters);
            float ownIntensity = rateCurve * maxIntensity;

            // Several giantesses (e.g. Twins) may grow at once. Each reports per frame; send
            // the strongest current value so the output does not flicker between them.
            CurrentIntensities[instanceId] = new FrameIntensity(Time.frameCount, ownIntensity);
            float intensity = GetStrongestCurrentIntensity();
            HapticMixer.ReportGrowth(intensity);

            if (Time.realtimeSinceStartup >= _nextDebugLogAt)
            {
                Plugin.LogDebug("Growth event: scale rate=" + growthRate.ToString("0.000") +
                    " scale/s, height=" + heightMeters.ToString("0") +
                    " m, intensity=" + intensity.ToString("0.00") + ".");
                _nextDebugLogAt = Time.realtimeSinceStartup + 1f;
            }
        }
    }
}
