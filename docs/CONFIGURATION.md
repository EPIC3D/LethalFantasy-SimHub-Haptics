# Configuration and event behaviour

The plugin creates or updates
`<LethalFantasy>\BepInEx\config\com.epicopy.copilot.simhubtelemetry.cfg` on
start. Edit it while the game is closed, then restart the game.

## Settings

```ini
[Logging]
EnableDebugLogging = false

[Haptics - Footsteps]
IntensityMultiplier = 2      # 0..5
SeatGain = 1                 # 0..2
LowerBackGain = 1.35         # 0..2
MidBackGain = 1              # 0..2
UpperBackGain = 0.9          # 0..2
BassGain = 0.85              # 0..2

[Haptics - Growth]
ScaleRateForFullResponse = 0.05
IntensityAt30Meters = 0.01   # 0..1
IntensityAt90Meters = 0.05   # 0..1
IntensityAt1440Meters = 0.5  # 0..1
IntensityAt23040Meters = 1   # 0..1
BassGain = 0.18              # 0..1

[Haptics - Heartbeat]
MotorIntensity = 0.28        # 0..1, normal heartbeat
ElevatedMotorIntensity = 0.3 # 0..1, flustered heartbeat
PulseDurationMs = 180        # 30..300

[Haptics - Impacts]
TrapExplosionIntensity = 0.4
StompHitIntensity = 0.55
PlayerDamageIntensity = 0.3
FlyingStunIntensity = 0.42
StunKnockupIntensity = 0.36
KnockupIntensity = 0.3

[Haptics - Propagation]
DelayBetweenZonesSeconds = 0.06  # 0..0.2
```

All outputs are clamped to `0..1` before they are sent. Growth reference points
are kept monotonic, so intensity never decreases as height increases.

## Channels and packet

The packet (91 bytes, little-endian, packed) carries the SimHub header followed
by nine `float` channels in this order: `BassShaker`, `HapticUpperBackLeft`,
`HapticUpperBackRight`, `HapticMidBackLeft`, `HapticMidBackRight`,
`HapticLowerBackLeft`, `HapticLowerBackRight`, `HapticSeatLeft`,
`HapticSeatRight`.

- Definition unique ID: `530205f6-c531-4c7a-b079-4e73679e2910`
- Game signature: `0x5B503624`
- Telemetry signature: `0x1AD4446F`

When several events overlap, each channel takes the **strongest** value rather
than the sum, so nothing clips by accident.

## How each event is calculated

### Footsteps

Patch: `Armature.PlayFootstepScreenShake`. Uses the game's shake power and
radius, falloff curve, player distance, giantess scale and shockwave range to
compute intensity and pulse duration. The step position relative to the camera
direction biases left vs. right. The pulse starts at the seat and travels up
through the four zones, `DelayBetweenZonesSeconds` apart. Every giantess' steps
are handled independently, so with two giantesses you feel steps from both
directions.

### Growth

Patch: `Giantess.UpdateScale`. Follows the actual positive scale change per
frame: intensity = growth-rate response × size target (interpolated between the
30 m / 90 m / 1440 m / 23040 m reference points). Growth bursts and pauses come
through as separate waves travelling from the seat upward; the bass channel
gets a subtle rumble. With several giantesses growing at once, the strongest
current growth is used.

### Heartbeat

Follows the game's own heartbeat (`GiantessExpressionManager`: Normal = 60 BPM,
Flustered = 100 BPM and its alternating "lup-dup" beats). Beats alternate
left/right on the upper and mid back motors.

The heartbeat runs only while you are actually held:

| Situation | Starts | Stops |
| --- | --- | --- |
| Interaction hug (with dialogue) | 4 s after the hug reaches `HowHug1` | ~1 s after release / leaving range |
| Animation hug (no dialogue) | 1 s after you are within 2.5 m of her heartbeat source | ~1 s after the game heartbeat stops or you are more than 3.5 m away |

The game already turns its heartbeat on at the start of the animation, while you
are still on the ground and cannot hear it – the distance check prevents the
haptics from starting there. During the Interaction hug intro (`Intro1-3`) the
distance trigger is ignored so the 4 s delay is respected. If several
giantesses have an active heartbeat, only the nearest one drives the beats.

### Impacts

Patches in `ImpactHaptics.cs`:

- Trap explosion – moderate pulse toward the trap's side.
- Successful stomp hit – stronger pulse toward the giantess' foot.
- Player damage – directionless pulse.
- Flying stun, stun knockup, knockup – pulse in the direction of the launch vector.

## Debug logging

With `EnableDebugLogging = true` the log contains detected events, hug and
heartbeat state transitions, the distance to the heartbeat source, and every
5 s a status line with packet count, patch call counters and channel values.
Errors are always logged.
