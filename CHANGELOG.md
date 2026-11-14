# Changelog

## 1.1.0

Updated for **LethalFantasy beta 1.0.2**.

- Fixed: build/runtime compatibility with beta 1.0.2 – stomp hits now read the
  giantess position through the new `IArmature` interface.
- Fixed: heartbeat never fired – the game renamed `heartbeatLupDup` to
  `_heartbeatLupDup` (both names are now supported).
- New: heartbeat for the **Animation** hug (no dialogue). It starts when you are
  close to the giantess' heartbeat, not while you are still on the ground.
- Changed: heartbeat start delay is 4 s for the Interaction hug and 1 s for the
  Animation hug; it stops about 1 s after release (was 10 s).
- New: support for several giantesses at once (Twins) – growth uses the
  strongest current growth and only the nearest active heartbeat drives beats.
- Build: game path is configured via `GamePath.props`; no game binaries are
  referenced by absolute path or copied to the output.

## 1.0.0

Initial release for LethalFantasy Alpha v9.1.0: footsteps, growth, heartbeat
(Interaction hug), stomp, trap, damage and knockback haptics for an 8-motor
layout plus a bass shaker channel.
