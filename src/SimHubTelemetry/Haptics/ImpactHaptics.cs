using HarmonyLib;
using System;
using UnityEngine;

namespace MyFirstPlugin
{
    // Adds event-specific impact pulses to the shared mixer. Each patch observes
    // a confirmed game event and contributes its source side when available;
    // directionless hits remain symmetric and still travel from seat to upper back.
    [HarmonyPatch]
    internal static class TrapExplosionHaptics
    {
        [HarmonyTargetMethod]
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(TrapExplosiveEnemy), "TriggerEnumerator");
        }

        [HarmonyPrefix]
        private static void Prefix(TrapExplosiveEnemy __instance)
        {
            if (__instance == null)
                return;

            HapticMixer.ScheduleImpact(
                "Trap explosion",
                HapticTuning.TrapExplosionIntensity,
                450,
                FootstepHaptics.GetSourceSide(__instance.transform.position),
                0.85f,
                1.0f,
                0.85f,
                0.75f);
        }
    }

    [HarmonyPatch(typeof(CheckPlayerInMeshCollider), "ProcessGiantessStomp")]
    internal static class StompHaptics
    {
        [HarmonyPostfix]
        private static void Postfix(
            CheckPlayerInMeshCollider __instance,
            Armature.StepEvent stepFootEvent,
            bool __result)
        {
            // Beta 1.0.2 changed CheckPlayerInMeshCollider.armature to the IArmature interface.
            if (!__result || stepFootEvent != Armature.StepEvent.Stomp ||
                __instance == null || !(__instance.armature is Component armature) || armature == null)
                return;

            HapticMixer.ScheduleImpact(
                "Player stomp hit",
                HapticTuning.StompHitIntensity,
                500,
                FootstepHaptics.GetSourceSide(armature.transform.position),
                1.0f,
                1.0f,
                0.80f,
                0.65f);
        }
    }

    [HarmonyPatch(typeof(PlayerDriver), "DamagePlayer")]
    internal static class PlayerDamageHaptics
    {
        [HarmonyPrefix]
        private static void Prefix(object[] __args)
        {
            if (__args == null || __args.Length < 2)
                return;

            float damage = Convert.ToSingle(__args[0]);
            if (damage <= 0f)
                return;

            string tags = __args[1]?.ToString() ?? string.Empty;
            string category = tags.IndexOf("Explosive", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Explosive player hit"
                : "Player damage";

            HapticMixer.ScheduleImpact(
                category,
                HapticTuning.PlayerDamageIntensity,
                360,
                0f,
                0.90f,
                1.0f,
                0.85f,
                0.70f);
        }
    }

    [HarmonyPatch(typeof(PlayerDriver), "SendFlyingStun")]
    internal static class FlyingStunHaptics
    {
        [HarmonyPrefix]
        private static void Prefix(Vector3 __0)
        {
            HapticMixer.ScheduleImpact("Flying stun", HapticTuning.FlyingStunIntensity, 500,
                ImpactHaptics.GetSideFromDirection(__0),
                0.85f, 1.0f, 0.85f, 0.70f);
        }
    }

    [HarmonyPatch(typeof(PlayerDriver), "StunKnockup")]
    internal static class StunKnockupHaptics
    {
        [HarmonyPrefix]
        private static void Prefix(Vector3 __0)
        {
            HapticMixer.ScheduleImpact(
                "Stun knockback",
                HapticTuning.StunKnockupIntensity,
                420,
                ImpactHaptics.GetSideFromDirection(__0),
                0.85f,
                1.0f,
                0.85f,
                0.70f);
        }
    }

    [HarmonyPatch(typeof(PlayerDriver), "Knockup")]
    internal static class KnockupHaptics
    {
        [HarmonyPrefix]
        private static void Prefix(Vector3 __0)
        {
            HapticMixer.ScheduleImpact(
                "Knockup",
                HapticTuning.KnockupIntensity,
                400,
                ImpactHaptics.GetSideFromDirection(__0),
                0.85f,
                1.0f,
                0.85f,
                0.70f);
        }
    }

    internal static class ImpactHaptics
    {
        internal static float GetSideFromDirection(Vector3 direction)
        {
            Vector3 horizontalDirection = new Vector3(direction.x, 0f, direction.z);
            Vector3 forward = PlayerDriver.GetCameraForwardDirection();
            forward.y = 0f;
            if (horizontalDirection.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
                return 0f;

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            return Mathf.Clamp(Vector3.Dot(horizontalDirection.normalized, right), -1f, 1f);
        }
    }
}
