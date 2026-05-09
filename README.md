# Jumpzys Vortex v2.2 — Full Rebuild

Razer Vortex-inspired WPF game optimiser.
Dark UI · Real metrics · ML bottleneck prediction · Global hotkeys · FPS + network overlays

---

## Requirements
- Windows 10/11 (x64)
- .NET 8 SDK → https://dotnet.microsoft.com/download
- Visual Studio 2022 (Community is free) **or** `dotnet` CLI

---

## Build & Run

### Visual Studio
1. Open `JumpzysVortex.sln`
2. Right-click `JumpzysVortex.App` → **Set as Startup Project**
3. Press **F5**

### CLI (quickest)
```bash
cd JumpzysVortex.App
dotnet run
```

### Publish single EXE
```bash
cd JumpzysVortex.App
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -o ../publish
```

> **Run as Administrator** so process-priority + power-plan changes take full effect.  
> The app.manifest requests elevation automatically — Windows will show a UAC prompt.

---

## Project layout

| Project                    | What it does                                              |
|----------------------------|-----------------------------------------------------------|
| `JumpzysVortex.App`        | WPF UI — 6 tabs, XAML + code-behind, app.manifest (UAC)  |
| `JumpzysVortex.Core`       | Game detection, process throttling, power-plan switching  |
| `JumpzysVortex.Services`   | Real CPU/RAM/GPU via PerformanceCounter + WMI, FPS, logs  |
| `JumpzysVortex.Network`    | Real ICMP ping, jitter, packet loss, Tcpip speed counters |
| `JumpzysVortex.AI`         | StateEngine — Green/Yellow/Red logic + tips               |
| `JumpzysVortex.ML`         | Microsoft.ML FastTree binary classifier                   |
| `JumpzysVortex.Config`     | JSON settings + Windows registry startup                  |
| `JumpzysVortex.Hotkeys`    | Win32 RegisterHotKey — works while in-game                |
| `JumpzysVortex.Overlay`    | Transparent FPS + network overlays (Topmost)              |
| `JumpzysVortex.Tray`       | System tray icon with right-click menu                    |

---

## Tabs

| Tab         | Contents                                                     |
|-------------|--------------------------------------------------------------|
| Dashboard   | Live metrics, ping, process list, boost button, quick settings, log |
| Performance | Detailed CPU/RAM/GPU/FPS cards, min/max/avg FPS, process list |
| Network     | Ping history, jitter, packet loss, download/upload, conn info |
| ML Engine   | Snapshot progress, bottleneck risk, train/clear, factor weights |
| Settings    | All toggles, thresholds, custom game exes, about             |
| Logs        | Full scrollable session log with clear + open folder         |

---

## Global Hotkeys (work in-game)

| Keys           | Action              |
|----------------|---------------------|
| Ctrl+Shift+B   | Apply boost         |
| Ctrl+Shift+R   | Restore normal      |
| Ctrl+Shift+O   | Toggle overlays     |
| Ctrl+Shift+D   | Show / hide window  |

---

## Data locations

| Data         | Path                                                    |
|--------------|---------------------------------------------------------|
| Settings     | `%APPDATA%\JumpzysVortex\settings.json`                 |
| ML model     | `%APPDATA%\JumpzysVortex\model.zip`                     |
| Session logs | `Documents\JumpzysVortex\Logs\`                         |

---

## GPU Temperature note

Windows does not expose CPU/GPU temperatures natively via PerformanceCounters.
The app tries these sources in order:
1. **MSAcpi_ThermalZoneTemperature** — always available, may read motherboard zone not CPU
2. **OpenHardwareMonitor WMI** — install [OHM](https://openhardwaremonitor.org/) and run it as admin
3. **LibreHardwareMonitor WMI** — install [LHM](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) and run as admin

For accurate temps, run either OHM or LHM alongside Vortex.
