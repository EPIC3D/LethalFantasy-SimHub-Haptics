# Installation guide

This guide sets up the plugin in LethalFantasy and configures SimHub to drive
an 8-motor haptic layout (e.g. HF8) from it.

**Requirements**

- LethalFantasy (beta 1.0.2 or newer) on Windows
- [SimHub](https://www.simhubdash.com/) with ShakeIt Motors and your haptic device already working
- The game and SimHub running on the **same PC** (the plugin sends to `127.0.0.1:5000`)

---

## 1. Install BepInEx 5

The plugin is a BepInEx 5 plugin. LethalFantasy is a 64-bit Unity (Mono) game,
so you need the **BepInEx 5.4.x, Windows x64** build.

1. Download `BepInEx_win_x64_5.4.x.zip` from the
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) (tested with 5.4.23).
2. Extract the zip **into the LethalFantasy folder**, next to `LethalFantasy.exe`.
   You should now have `BepInEx\`, `winhttp.dll` and `doorstop_config.ini` there.
3. Start the game once and quit. BepInEx creates its `config`, `plugins` and
   `LogOutput.log` on the first run.

## 2. Install the plugin

1. Close the game.
2. Copy `SimHubTelemetry.dll` into:
   ```
   <LethalFantasy>\BepInEx\plugins\
   ```
3. *(Optional)* To start from the default tuning, copy
   `com.epicopy.copilot.simhubtelemetry.cfg.example` to
   `<LethalFantasy>\BepInEx\config\com.epicopy.copilot.simhubtelemetry.cfg`
   (remove `.example`). If you skip this, the plugin creates the file with
   defaults on first start.

**Check:** start the game and open `<LethalFantasy>\BepInEx\LogOutput.log`.
You should see:

```
Loading [Epicopy + Copilot SimHub Telemetry 1.2.0]
First SimHub UDP telemetry packet sent (91 bytes).
Haptic event patches active: Armature.PlayFootstepScreenShake, Giantess.UpdateScale, ...
SimHub UDP telemetry sender initialized for 127.0.0.1:5000.
```

The line `BepInEx plugin component was destroyed; sender and mixer continue...`
is expected and harmless.

## 3. Register the game in SimHub

1. Open SimHub and import `LethalFantasy.simdef` in the **Game Definition
   Editor** (custom / external sim game definitions). If an older LethalFantasy
   definition is registered, update or replace it.
2. Make sure the definition uses:
   - UDP port: `5000`
   - Telemetry signature: `0x1AD4446F`
   - Process name: `LethalFantasy`
3. Start the game. SimHub should detect `LethalFantasy` and show it as running.

The definition exposes these properties:

| Property | Meaning |
| --- | --- |
| `GameRawData.Custom_HapticUpperBackLeft` / `...Right` | Upper back / shoulder blades |
| `GameRawData.Custom_HapticMidBackLeft` / `...Right` | Mid back / waist |
| `GameRawData.Custom_HapticLowerBackLeft` / `...Right` | Lower back / thighs |
| `GameRawData.Custom_HapticSeatLeft` / `...Right` | Seat / glutes |
| `GameRawData.Custom_BassShaker` | Optional bass shaker / Buttkicker |

All values are `0..1`.

## 4. Set up ShakeIt Motors

### Option A – import the example profile (quickest)

1. In SimHub open **ShakeIt Motors** and use the profile **Import** function.
2. Select `LethalFantasy-HF8.siprofile`. It appears as
   *LethalFantasy - HF8 (example)* and is linked to the LethalFantasy game
   definition.
3. The profile contains the two Custom Effects with all formulas, but **no
   device output settings** (those are specific to each PC). Open each effect
   and route its outputs to your physical motors as described in Option B below.

### Option B – create the effects manually

Create **two Custom Effects** in ShakeIt Motors. Each Custom Effect has four
outputs (Front Left, Front Right, Rear Left, Rear Right). Enter the formulas
below – they convert the plugin's `0..1` values to ShakeIt's `0..100` range.

**Custom Effect 1 – upper and mid back**

| Effect output | Formula |
| --- | --- |
| Front Left | `100 * [GameRawData.Custom_HapticUpperBackLeft]` |
| Front Right | `100 * [GameRawData.Custom_HapticUpperBackRight]` |
| Rear Left | `100 * [GameRawData.Custom_HapticMidBackLeft]` |
| Rear Right | `100 * [GameRawData.Custom_HapticMidBackRight]` |

**Custom Effect 2 – lower back and seat**

| Effect output | Formula |
| --- | --- |
| Front Left | `100 * [GameRawData.Custom_HapticLowerBackLeft]` |
| Front Right | `100 * [GameRawData.Custom_HapticLowerBackRight]` |
| Rear Left | `100 * [GameRawData.Custom_HapticSeatLeft]` |
| Rear Right | `100 * [GameRawData.Custom_HapticSeatRight]` |

Then, in each effect's output channel settings, route every output to the
matching **physical** motor. "Front/Rear" are only the effect's output names –
what matters is which physical motor each one is routed to.

**Optional bass shaker:** add an effect on your bass shaker output with the
formula `100 * [GameRawData.Custom_BassShaker]`.

## 5. Test

1. Start SimHub, then the game.
2. Walk near a giantess – footsteps should travel from seat to shoulders and
   come from the correct side.
3. Hugs:
   - **Interaction hug** (dialogue): the heartbeat starts about 4 s after the hug begins.
   - **Animation hug** (no dialogue): the heartbeat starts about 1 s after you are lifted close to her.
   - After you are put down, the heartbeat stops within about 1 s.

## Troubleshooting

| Problem | Check |
| --- | --- |
| No `BepInEx\LogOutput.log` | BepInEx is not installed in the game folder, or it is the wrong (x86 / BepInEx 6) build. |
| Log has no `SimHub Telemetry` lines | `SimHubTelemetry.dll` is not in `BepInEx\plugins`. |
| `Harmony loaded, but no game event patches were installed` | Game version not supported by this plugin version. |
| SimHub does not detect the game | Definition not imported, or process name / port differ. |
| Game detected but no vibration | Custom Effect formulas or output routing in ShakeIt. Use SimHub's telemetry viewer to confirm the `Custom_Haptic...` values change. |
| Too weak / too strong | Tune the `.cfg` – see [CONFIGURATION.md](CONFIGURATION.md). |

For detailed diagnostics set `EnableDebugLogging = true` in the `[Logging]`
section of the plugin config and restart the game; events and channel values
are then written to `LogOutput.log`.
