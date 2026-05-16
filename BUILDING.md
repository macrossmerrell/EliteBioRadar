# Building Elite Bio Radar

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows)
- Windows 10 or later

No Visual Studio required — all commands run from a terminal in the project folder.

---

## Standard Build (recommended)

```
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

- `dotnet restore` — downloads NuGet dependencies (only needed once, or after package changes)
- `dotnet build -c Release` — compiles and checks for errors
- `dotnet publish ...` — packages everything into a single self-contained exe

Output goes to `bin\Release\net8.0-windows\BioRadar-App\`

The following files are created alongside the exe on first run and persist between publishes as long as you do not delete the `BioRadar-App\` folder:
- `EliteBioRadar.cache.json` — scan location history per planet
- `EliteBioRadar.settings.json` — saved app settings
- `EliteBioRadar.log` — diagnostic log (overwritten on each launch)

---

## Quick Run (development/testing only)

Builds and launches without publishing. Cache and settings will be created in `bin\Debug\net8.0-windows\` instead of `BioRadar-App\`.

```
dotnet run
```

---

## Clean Build

Removes all build output. **Warning:** deletes the `bin\` folder including any cache or settings files stored there.

```
dotnet clean
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## Notes

- The `BioRadar-App\` folder is the only thing end users need — no .NET installation required on their machine
- Do not run `dotnet clean` if you have unsaved cache data in `bin\`
