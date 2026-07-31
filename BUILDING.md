# Building Tea Time Deck

Two components, built independently:

| Path | What | Toolchain |
| --- | --- | --- |
| `dalamud/` | In-game Dalamud plugin, hosts the local API | .NET 10 SDK (portable) |
| `streamdeck/` | Stream Deck plugin, talks to the local API | Node 20 |

## .NET 10 SDK

Dalamud plugins need the .NET 10 **SDK** only to compile. The runtime that
actually executes the plugin inside FFXIV is bundled by Dalamud/XIVLauncher, so
no machine-wide runtime is needed.

If you already have the .NET 10 SDK installed and on `PATH`, skip to the build
step — `dotnet build` will just work.

### Building without a machine-wide install

Installing .NET 10 system-wide breaks some other software, so this project is
developed against a **portable** SDK: a plain folder install that is not on
`PATH` and not in the registry. `dev.ps1` opts a single shell into it.

```powershell
. .\dev.ps1                                   # opt this shell in
dotnet build dalamud/TeaTimeDeck.slnx -c Release
```

`dev.ps1` looks at `$env:DOTNET10_ROOT`, defaulting to `$env:USERPROFILE\dotnet10`.
Set that variable first if your portable SDK lives somewhere else.

Or without touching the environment at all:

```powershell
& "$env:USERPROFILE\dotnet10\dotnet.exe" build dalamud/TeaTimeDeck.slnx -c Release
```

To install the portable SDK:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile "$env:TEMP\dotnet-install.ps1"
& "$env:TEMP\dotnet-install.ps1" -Channel 10.0 -Quality GA -InstallDir "$env:USERPROFILE\dotnet10" -NoPath
```

`-NoPath` is the key flag — it keeps the install out of `PATH` and the registry.

### Gotchas

The solution is `.slnx`, the XML solution format that is the default in .NET 10.
Older `dotnet` versions cannot open it.

With a portable SDK, a bare `dotnet build` resolves whatever global SDK you have
and fails on `Dalamud.NET.Sdk` (a net10.0 project). Dot-source `dev.ps1` first.

`global.json` pins `10.0.301` so IDEs resolve the same SDK. Rider needs the SDK
path under Settings → Build → Toolset and Build.

## Loading the plugin in-game

The build drops `TeaTimeDeck.dll` under `dalamud/TeaTimeDeck/bin/Release/`.
In-game: `/xlsettings` → Experimental → Dev Plugin Locations → add that folder,
then `/xlplugins` → installed → enable Tea Time Deck. `/xlplugins` reload picks up
rebuilds without restarting the game.

## Notes

- The PowerShell `NativeCommandError` around the .NET first-run telemetry banner
  is harmless noise, not a build failure. Read the MSBuild summary lines.
- Dalamud dev libraries are referenced from
  `%AppData%\XIVLauncher\addon\Hooks\dev\` (currently Dalamud 15.0.3). They are
  supplied by XIVLauncher, not NuGet.
