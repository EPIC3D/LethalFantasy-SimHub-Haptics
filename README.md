# LethalFantasy SimHub Haptics

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/E6Z42AUOBQ)

A BepInEx plugin for **LethalFantasy** that turns in-game events into haptic
feedback through **SimHub**. It is built for an 8-motor haptic vest/seat layout
(e.g. HF8) plus one optional bass shaker channel.

The plugin listens to game events with Harmony patches, mixes them into nine
normalized `0..1` channels and streams them to SimHub over UDP
(`127.0.0.1:5000`, 91-byte packet, every 15 ms). SimHub's ShakeIt Motors then
drive the physical motors.

| Event | What you feel |
| --- | --- |
| Footsteps | A pulse that travels from the seat up the back, biased toward the side the step came from, scaled by size and distance |
| Growth | A rising wave that follows the giantess' actual growth rate and size |
| Heartbeat | Alternating left/right beats while you are held in a hug, following the game's own normal / flustered rhythm |
| Stomp hit, trap explosion, damage, knockback | Directional impact pulses |
| Pet (head stroking) | Upper-back motors following the stroking hand between the shoulders |
| Lap | A gentle top-to-bottom caress wave, and a soft breathing swell while resting |
| Foot massage | Whole-body pressure swells; step and crush hits |
| Menu pickup (Akipo, Twins) | A squeeze when picked up and pokes while held |
| Lift / set-down | A G-force feel on the seat when you are lifted or lowered |

Supports **LethalFantasy beta 1.0.2-1.0.3**

**LethalFantasy** is made by **LadyAlice** – support the original game on
[Patreon](https://www.patreon.com/cw/LadyAliceR18). This is an unofficial
fan-made mod and is not affiliated with or endorsed by the game's creator.

> **Disclaimer:** This software is provided "as is", without warranty of any
> kind. Use it at your own risk. The author accepts no responsibility for any
> damage, data loss, game issues or hardware problems (including haptic
> devices) resulting from its use. See [LICENSE](LICENSE) for details.

## Download

Get the ready-to-use package from the
[Releases](../../releases) page. It contains:

- `SimHubTelemetry.dll` – the BepInEx plugin
- `LethalFantasy.simdef` – the SimHub game definition
- `LethalFantasy-HF8.siprofile` – example ShakeIt Motors profile (8-motor effects)
- `com.epicopy.copilot.simhubtelemetry.cfg.example` – optional tuning template
- `INSTALL.md` – the installation guide

## Installation

See **[docs/INSTALL.md](docs/INSTALL.md)** for the full step-by-step guide,
including the SimHub ShakeIt setup.

Short version:

1. Install **BepInEx 5 (x64)** into the LethalFantasy folder and start the game once.
2. Copy `SimHubTelemetry.dll` into `<LethalFantasy>\BepInEx\plugins\`.
3. Import `LethalFantasy.simdef` in SimHub.
4. Import `LethalFantasy-HF8.siprofile` in ShakeIt Motors (or create the two
   Custom Effects by hand) and route the outputs to your motors.

## Configuration

All strengths, zone gains and timings can be tuned in
`<LethalFantasy>\BepInEx\config\com.epicopy.copilot.simhubtelemetry.cfg`.
See **[docs/CONFIGURATION.md](docs/CONFIGURATION.md)** for every setting and
for a detailed description of how each event is calculated.

## Building from source

Requirements: .NET SDK (any version that can build `netstandard2.1`) and a local
LethalFantasy install **with BepInEx 5** – the build references the game's own
`Assembly-CSharp.dll`, Unity and BepInEx assemblies, which are not part of this
repository.

```powershell
# 1. Tell the build where your game is
Copy-Item GamePath.props.example GamePath.props
#    then edit GamePath.props -> <LethalFantasyDir>

# 2. Build
dotnet build src\SimHubTelemetry\SimHubTelemetry.csproj -c Release

# 3. (optional) Build the release zip into dist\
.\scripts\package.ps1
```

You can also pass the path directly:
`dotnet build src\SimHubTelemetry\SimHubTelemetry.csproj -c Release -p:LethalFantasyDir="D:\Games\LethalFantasy"`

### Repository layout

```
src/SimHubTelemetry/     Plugin source (Plugin.cs, Haptics/*.cs)
simhub/                  SimHub game definition + example ShakeIt profile
config/                  Example tuning config
docs/                    Installation and configuration guides
scripts/package.ps1      Builds the release zip
```

## Notes

- This is an unofficial fan mod and is not affiliated with the LethalFantasy developers.
- No game, Unity, BepInEx or Harmony binaries are included or redistributed;
  they come from your own installation.
- The plugin and the SimHub definition must use the same telemetry signature
  (`0x1AD4446F`). If you change packet fields, update both.

## License

[MIT](LICENSE)
