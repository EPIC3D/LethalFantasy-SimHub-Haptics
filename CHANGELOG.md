# Changelog

## 1.2.0

For **LethalFantasy beta 1.0.2**. No SimHub profile or `.simdef` change is
needed; existing config files keep working (new keys are added with defaults).

- New: **Pet** – head stroking on the two upper-back motors, following the
  stroking hand and gliding between the left and right shoulder.
- New: **Lap** – the caressing hand becomes a gentle top-to-bottom wave over the
  whole body; while resting in the lap (e.g. during dialogue) a soft breathing
  swell keeps the presence noticeable.
- New: **Foot massage** – whole-body pressure that swells and fades with the
  foot's motion; foot-massage steps hit harder with the requested level, and the
  crush gives a strong hit.
- New: **Menu pickup** (Akipo and the Twins) – a short squeeze when picked up,
  and pokes from the other hand while held.
- New: **Lift / set-down** – a G-force feel, mostly on the seat, when an
  interaction lifts or lowers you (lap, hug, menu pickup, foot massage).
  Scene-fade jumps and menu camera moves are filtered out.
- New: all motors are zeroed immediately on game exit and on every scene change
  (restart, return to lobby, main menu).
- New: `[Haptics - Interactions]` settings section.

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
