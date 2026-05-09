# Jumpzys Vortex Feature Additions

- Installer scaffold: `installer.iss` for Inno Setup.
- Release pipeline: `release.ps1` publishes, zips, hashes, and writes an update manifest.
- Auto-update scaffold: manifest-based check in Control Center.
- Plugin discovery: scans `%APPDATA%\JumpzysVortex\Plugins` for `plugin.json`.
- PresentMon adapter: detects `PresentMon.exe` and can launch a 30-second capture.
- Restore/Safety Center: tracks reversible system changes.
- Toast-style notifications: tray balloon notices for boost/restore events.
- Theme editor: saved accent and density preferences.
- Mini Mode: compact always-on-top dashboard mode.
- Process details: quick high-memory process inspection.
