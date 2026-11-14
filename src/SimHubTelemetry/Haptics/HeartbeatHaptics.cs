using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MyFirstPlugin
{
    [HarmonyPatch(typeof(GiantessExpressionManager), "LateUpdate")]
    internal static class HeartbeatHaptics
    {
        // Interaction hug (HugAkipo states) vs Animation-only hug (proximity) start delays.
        private const double HugStateStartDelaySeconds = 4.0;
        private const double ProximityStartDelaySeconds = 1.0;
        // Release now follows proximity / the game's own heartbeat, so a long grace period is not needed.
        private const double HeartbeatStopDelaySeconds = 1.0;
        private static readonly FieldInfo HeartbeatStateField =
            AccessTools.Field(typeof(GiantessExpressionManager), "heartbeatState");
        private static readonly FieldInfo HeartbeatPulseStateField =
            // Beta 1.0.2 renamed heartbeatLupDup to _heartbeatLupDup; keep the old name as fallback.
            AccessTools.Field(typeof(GiantessExpressionManager), "_heartbeatLupDup") ??
            AccessTools.Field(typeof(GiantessExpressionManager), "heartbeatLupDup");
        private static readonly Dictionary<int, bool> LastPulseStates = new Dictionary<int, bool>();
        private static bool _missingFieldsWarningLogged;
        private static bool _playerHeldInHug;
        private static bool _nextPulseOnLeft = true;
        private static double _lastGamePulseAt = -1.0;
        private static double _fallbackPulseIntervalSeconds = 0.5;
        private static bool _lastHeartbeatWasElevated;
        private static double _nextDistanceLogAt;
        private static readonly Dictionary<int, InstanceHeartbeat> Instances = new Dictionary<int, InstanceHeartbeat>();
        private static readonly List<int> StaleInstances = new List<int>();
        private static int _primaryInstanceId = -1;

        private sealed class InstanceHeartbeat
        {
            internal string State;
            internal float Distance = -1f;
            internal int Frame;
        }

        // Tracks each GiantessExpressionManager separately and derives one shared decision
        // per frame: proximity is active if the player is near ANY giantess with an active
        // heartbeat, and the nearest such giantess becomes the pulse source.
        private static void UpdateInstanceAndProximity(GiantessExpressionManager manager)
        {
            int frame = Time.frameCount;
            int id = manager.GetInstanceID();
            if (!Instances.TryGetValue(id, out InstanceHeartbeat info))
            {
                info = new InstanceHeartbeat();
                Instances.Add(id, info);
            }

            string state = HeartbeatStateField.GetValue(manager)?.ToString();
            if (state != info.State)
            {
                Plugin.LogDebug("Game heartbeat state [" + manager.name + "]: " + (info.State ?? "?") +
                    " -> " + state + " (held in hug: " + _playerHeldInHug + ").");
                info.State = state;
            }

            bool active = state == "Normal" || state == "Flustered";
            info.Distance = active ? GetHeartbeatSourceDistance(manager) : -1f;
            info.Frame = frame;

            // Aggregate over managers seen this or the previous frame; drop destroyed ones.
            int nearestId = -1;
            float nearestDistance = float.MaxValue;
            StaleInstances.Clear();
            foreach (KeyValuePair<int, InstanceHeartbeat> entry in Instances)
            {
                if (frame - entry.Value.Frame > 120)
                {
                    StaleInstances.Add(entry.Key);
                    continue;
                }

                if (frame - entry.Value.Frame > 1 || entry.Value.Distance < 0f)
                    continue;

                if (entry.Value.Distance < nearestDistance)
                {
                    nearestDistance = entry.Value.Distance;
                    nearestId = entry.Key;
                }
            }

            foreach (int staleId in StaleInstances)
            {
                Instances.Remove(staleId);
                LastPulseStates.Remove(staleId);
            }

            _primaryInstanceId = nearestId;

            // Animation-only hugs skip the HugAkipo states, so also gate on the game's own
            // heartbeat plus closeness to its audio source. During the Interaction hug intro
            // the hug state (4 s delay) decides instead of proximity.
            bool proximityActive = false;
            if (!_hugIntroActive && nearestId != -1)
            {
                proximityActive = _proximityRequested
                    ? nearestDistance <= ProximityExitDistance
                    : nearestDistance <= ProximityEnterDistance;
            }

            double now = Plugin.GetSessionTimeSeconds();
            if (nearestId != -1 && now >= _nextDistanceLogAt)
            {
                _nextDistanceLogAt = now + 5.0;
                Plugin.LogDebug("Heartbeat source distance: " + nearestDistance.ToString("0.0") +
                    " m (giantesses tracked: " + Instances.Count + ", proximity active: " +
                    proximityActive + ").");
            }

            SetProximityRequested(proximityActive);
        }
        private const float ProximityEnterDistance = 2.5f;
        private const float ProximityExitDistance = 3.5f;
        private static bool _hugRequested;
        private static volatile bool _hugIntroActive;

        internal static void SetHugIntroActive(bool isIntro)
        {
            if (_hugIntroActive == isIntro)
                return;

            _hugIntroActive = isIntro;
            Plugin.LogDebug(isIntro
                ? "Interaction hug intro: proximity trigger suppressed."
                : "Interaction hug intro ended: proximity trigger allowed.");
        }
        private static bool _proximityRequested;

        private static float GetHeartbeatSourceDistance(GiantessExpressionManager manager)
        {
            if (PlayerDriver.LocalPlayer == null)
                return -1f;

            Component source = HeartbeatAudioSourceField?.GetValue(manager) as Component;
            Vector3 sourcePos = source != null ? source.transform.position : manager.transform.position;
            return Vector3.Distance(PlayerDriver.GetPlayerPosition(), sourcePos);
        }

        private static void SetProximityRequested(bool isNear)
        {
            if (_proximityRequested == isNear)
                return;

            _proximityRequested = isNear;
            Plugin.LogDebug(isNear
                ? "Player is close to the audible heartbeat source."
                : "Player left the heartbeat source range.");
            UpdateRequestedHeld();
        }
        private static readonly FieldInfo HeartbeatAudioSourceField =
            AccessTools.Field(typeof(GiantessExpressionManager), "heartbeatAudioSource1");

        [HarmonyPostfix]
        private static void Postfix(GiantessExpressionManager __instance)
        {
            ProcessPendingHugState();

            if (__instance != null && HeartbeatStateField != null)
                UpdateInstanceAndProximity(__instance);

            if (__instance == null || !_playerHeldInHug)
                return;

            int instanceId = __instance.GetInstanceID();

            // Several giantesses (e.g. Twins) each run LateUpdate; only the nearest one with an
            // active heartbeat may drive pulses, otherwise two hearts would double the rhythm.
            if (_primaryInstanceId != -1 && instanceId != _primaryInstanceId)
                return;
            if (HeartbeatStateField == null || HeartbeatPulseStateField == null)
            {
                if (!_missingFieldsWarningLogged)
                {
                    Plugin.Log?.LogError("Heartbeat telemetry fields were not found; heartbeat haptics are unavailable.");
                    _missingFieldsWarningLogged = true;
                }
                return;
            }

            string heartbeatState = HeartbeatStateField.GetValue(__instance)?.ToString();
            if (heartbeatState != "Normal" && heartbeatState != "Flustered")
            {
                LastPulseStates.Remove(instanceId);
                TryScheduleReleaseGracePulse();
                return;
            }

            bool currentPulseState = (bool)HeartbeatPulseStateField.GetValue(__instance);
            if (!LastPulseStates.TryGetValue(instanceId, out bool previousPulseState))
            {
                LastPulseStates.Add(instanceId, currentPulseState);
                _lastGamePulseAt = Plugin.GetSessionTimeSeconds();
                _lastHeartbeatWasElevated = heartbeatState == "Flustered";
                return;
            }

            if (currentPulseState == previousPulseState)
                return;

            double now = Plugin.GetSessionTimeSeconds();
            if (_lastGamePulseAt >= 0.0)
            {
                double observedInterval = now - _lastGamePulseAt;
                if (observedInterval >= 0.1 && observedInterval <= 2.0)
                {
                    _fallbackPulseIntervalSeconds =
                        (_fallbackPulseIntervalSeconds + observedInterval) * 0.5;
                }
            }

            _lastGamePulseAt = now;
            _lastHeartbeatWasElevated = heartbeatState == "Flustered";
            LastPulseStates[instanceId] = currentPulseState;
            HapticMixer.ScheduleHeartbeatPulse(
                _nextPulseOnLeft,
                _lastHeartbeatWasElevated);
            _nextPulseOnLeft = !_nextPulseOnLeft;
        }

        private static bool _requestedHeldInHug;
        private static double _hugStateChangeRequestedAt;

        internal static void RequestPlayerHeldInHug(bool isHeld)
        {
            if (_hugRequested == isHeld)
                return;

            _hugRequested = isHeld;
            UpdateRequestedHeld();
        }

        // Heartbeat runs while either the Interaction hug holds the player or the player is
        // close to the audible heartbeat (Animation-only hug).
        private static void UpdateRequestedHeld()
        {
            bool isHeld = _hugRequested || _proximityRequested;
            if (_requestedHeldInHug == isHeld)
                return;

            _requestedHeldInHug = isHeld;
            _hugStateChangeRequestedAt = Plugin.GetSessionTimeSeconds();
            if (isHeld)
                _lastGamePulseAt = -1.0;
            Plugin.LogDebug(isHeld
                ? "Hug entered the held interaction state (" + (_hugRequested ? "interaction" : "proximity") +
                    "); heartbeat start is pending for " + GetStartDelaySeconds() + " s."
                : "Hug entered a release state; heartbeat stop is pending for " +
                    HeartbeatStopDelaySeconds + " s.");
        }

        private static void TryScheduleReleaseGracePulse()
        {
            if (_requestedHeldInHug || !_playerHeldInHug || _lastGamePulseAt < 0.0)
                return;

            double now = Plugin.GetSessionTimeSeconds();
            double stopAt = _hugStateChangeRequestedAt + HeartbeatStopDelaySeconds;
            double nextPulseAt = _lastGamePulseAt + _fallbackPulseIntervalSeconds;
            if (now < nextPulseAt || now >= stopAt)
                return;

            HapticMixer.ScheduleHeartbeatPulse(_nextPulseOnLeft, _lastHeartbeatWasElevated);
            _nextPulseOnLeft = !_nextPulseOnLeft;
            _lastGamePulseAt = now;
        }

        private static double GetStartDelaySeconds()
        {
            return _hugRequested ? HugStateStartDelaySeconds : ProximityStartDelaySeconds;
        }

        private static void ProcessPendingHugState()
        {
            double delay = _requestedHeldInHug
                ? GetStartDelaySeconds()
                : HeartbeatStopDelaySeconds;
            if (_playerHeldInHug == _requestedHeldInHug ||
                Plugin.GetSessionTimeSeconds() - _hugStateChangeRequestedAt <
                delay)
                return;

            _playerHeldInHug = _requestedHeldInHug;
            if (!_playerHeldInHug)
            {
                LastPulseStates.Clear();
                _nextPulseOnLeft = true;
                _lastGamePulseAt = -1.0;
            }
            else
            {
                _nextPulseOnLeft = true;
            }

            HapticMixer.SetHeartbeatEnabled(_playerHeldInHug);
            Plugin.LogDebug(_playerHeldInHug
                ? "Heartbeat haptics enabled while the player is held in the Hug loop."
                : "Heartbeat haptics disabled after the release grace period.");
        }
    }

    [HarmonyPatch]
    internal static class HugHeartbeatStateTransitionHaptics
    {
        private const int FirstHeldState = 3;
        private const int FirstReleaseState = 20;

        private static MethodBase TargetMethod()
        {
            Type stateBaseType = typeof(GiantessBehaviourEventHugAkipo)
                .BaseType.BaseType.BaseType;
            return AccessTools.Method(stateBaseType, "State");
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, object newState)
        {
            if (!(__instance is GiantessBehaviourEventHugAkipo) || newState == null)
                return;

            int state = Convert.ToInt32(newState);
            bool isInHeldInteraction = state >= FirstHeldState && state < FirstReleaseState;
            HeartbeatHaptics.SetHugIntroActive(state < FirstHeldState);
            HeartbeatHaptics.RequestPlayerHeldInHug(isInHeldInteraction);
        }
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventHugAkipo), "BehaviourUpdate")]
    internal static class HugHeartbeatHaptics
    {
        private const int FirstHeldState = 3;
        private const int FirstReleaseState = 20;
        private static readonly FieldInfo HugStateField =
            AccessTools.Field(typeof(GiantessBehaviourEventHugAkipo), "state");
        private static bool _missingStateWarningLogged;
        private static int _lastLoggedState = -1;

        [HarmonyPostfix]
        private static void Postfix(GiantessBehaviourEventHugAkipo __instance)
        {
            if (__instance == null || HugStateField == null)
            {
                if (!_missingStateWarningLogged)
                {
                    Plugin.Log?.LogError("Hug behavior state field was not found; heartbeat state gating is unavailable.");
                    _missingStateWarningLogged = true;
                }
                return;
            }

            object stateValue = HugStateField.GetValue(__instance);
            if (stateValue == null)
                return;

            int state = Convert.ToInt32(stateValue);
            if (state != _lastLoggedState)
            {
                Plugin.LogDebug("Hug state: " + stateValue + " (" + state + ").");
                _lastLoggedState = state;
            }
            bool isInHeldInteraction = state >= FirstHeldState && state < FirstReleaseState;
            HeartbeatHaptics.SetHugIntroActive(state < FirstHeldState);
            HeartbeatHaptics.RequestPlayerHeldInHug(isInHeldInteraction);
        }
    }

    [HarmonyPatch(typeof(GiantessBehaviourEventHugAkipo), "BehaviourEnd")]
    internal static class HugHeartbeatEndHaptics
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            HeartbeatHaptics.SetHugIntroActive(false);
            HeartbeatHaptics.RequestPlayerHeldInHug(false);
        }
    }
}
