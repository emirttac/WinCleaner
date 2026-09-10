# WinCleaner

**Windows 10/11 optimization and debloat toolkit for gamers** — C# · .NET 8 · WPF.

Catalog-driven tweaks · Risk levels · Restore points · Session undo · 8 languages

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows&logoColor=white)](https://github.com/emirttac/WinCleaner)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Releases](https://img.shields.io/github/v/release/emirttac/WinCleaner?include_prereleases&label=release)](https://github.com/emirttac/WinCleaner/releases)

> **Warning.** WinCleaner changes Windows services, registry values, scheduled tasks, and AppX packages. Keep **System Restore** enabled, read each toggle before applying, and prefer presets over “apply everything.” You are responsible for your system.

---

## Why WinCleaner?

Most “cleaner” tools either hide what they do, or dump dozens of unsafe tweaks in one click. WinCleaner is built around **visibility and control**:

| Principle | How it shows up |
|-----------|-----------------|
| **Catalog-driven** | Tweaks, services, bloatware, presets, and winget apps live in auditable JSON |
| **Risk-aware** | `Safe` / `Caution` / `Dangerous` / `Blocked` — blocked items cannot run |
| **Reversible where possible** | Session change log + undo journal; System Restore points by default |
| **Explicit danger** | Dangerous actions need a Settings opt-in **and** a confirmation dialog |
| **OS-aware** | `minBuild` / `win11Only` gates; incompatible items are filtered out |
| **Open source** | Full tree in the repo root; redistributable snapshot in [`SOURCE_CODES/`](SOURCE_CODES/) |

---

## Features

### Optimization & privacy
- **Services** — curated list with risk levels (critical services are **Blocked**: `RpcSs`, `DcomLaunch`, `WinDefend`)
- **Telemetry & privacy** — advertising ID, activity history, Cortana/Bing hooks, CEIP tasks, and related tweaks
- **Gaming** — Game Mode, Game Bar/DVR, HAGS, Ultimate Performance, MMCSS / latency-oriented options (anti-cheat confirm where relevant)
- **Visual** — animations, transparency, performance-oriented visual effects, startup apps
- **Windows 11 UI** — taskbar / Start / context-menu / Copilot / Recall oriented tweaks (OS-gated)
- **Network** — DNS presets (DHCP, Cloudflare, Google, Quad9), QoS, throttling, NetBIOS helpers

### Cleanup & tools
- **Bloatware** — AppX removal with confirmations; system-critical packages cannot be selected; OneDrive uninstall path
- **System tools** — temp cleanup, Disk Cleanup, SFC / DISM, WinSxS cleanup, Windows Update / network reset, God Mode, Take Ownership
- **Package installer** — winget catalog (browsers, runtimes, gaming/dev utilities) with silent install
- **Presets** — Gamer, Balanced, Minimal, Laptop, Privacy Max, Defaults (session undo–oriented)

### Product polish
- Dashboard with system overview
- **8 languages:** Türkçe, English, Deutsch, Español, 中文, Français, Română, Русский
- Custom profile import / export (JSON, schema v2)
- Optional startup update check against [GitHub Releases](https://github.com/emirttac/WinCleaner/releases)
- Crash and session logging under `%LocalAppData%\WinCleaner\`

### Catalog size (v1.0.0)

| Catalog | Entries |
|---------|--------:|
| Tweaks | 56 |
| Services | 28 |
| Bloatware (AppX) | 37 |
| Winget installer apps | 26 |
| Presets | 6 |

---

## Screenshots

> Add screenshots after the first public build — Dashboard · Gaming · Services · Presets · Settings.

---

## Requirements

| | |
|--|--|
| **OS** | Windows 10 **21H2+** (build ≥ 19044) or Windows 11 (x64) |
| **Privileges** | Administrator (UAC `requireAdministrator`) |
| **Runtime (installer)** | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — downloaded by Setup if missing |
| **Dev build** | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |

---

## Download

| Channel | Link |
|---------|------|
| **Installer (recommended)** | [Releases](https://github.com/emirttac/WinCleaner/releases) → `WinCleaner-Setup-x.y.z.exe` |
| **Source** | Clone this repo or open [`SOURCE_CODES/`](SOURCE_CODES/) |

The Inno Setup installer:

1. Installs WinCleaner under Program Files  
2. Downloads & installs **.NET 8 Desktop Runtime** and **VC++ Redistributable** when missing  
3. Can ship the open-source tree under `SOURCE_CODES` next to the app

> The Setup binary is currently **not Authenticode-signed**. Windows SmartScreen may warn on first run — use *More info → Run anyway* only if you trust the build (prefer downloading from this GitHub repo).

---

## Quick start (developers)

```powershell
git clone https://github.com/emirttac/WinCleaner.git
cd WinCleaner

dotnet restore
dotnet build src\WinCleaner\WinCleaner.csproj -c Release
dotnet test
dotnet run --project src\WinCleaner\WinCleaner.csproj
```

UAC will prompt for elevation.

### Publish portable (self-contained single exe)

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-portable.ps1
```

Output: `publish\WinCleaner.exe` — tek dosya, .NET runtime gömülü; ayrı `app` klasörü yok.
### Build the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Output: `publish\setup\WinCleaner-Setup-1.0.0.exe`

Refresh the open-source snapshot:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\sync-source-codes.ps1
```

---

## Architecture

```
WinCleaner.slnx
├── src/WinCleaner              WPF shell, ViewModels, themes, localization
├── src/WinCleaner.Core         Actions, executor, registry / services / AppX / DNS / winget
├── src/WinCleaner.Data         Embedded JSON catalogs + profile models
├── tests/WinCleaner.Core.Tests Catalog, OS, i18n, DNS, update, profile tests
├── installer/                  Inno Setup script + build helper
├── scripts/                    SOURCE_CODES sync
├── tools/i18n/                 JSON → Strings.*.xaml pipeline
└── SOURCE_CODES/               Redistributable open-source mirror
```

### Change pipeline

```
JSON catalog  →  TweakActionFactory  →  IChangeAction  →  ActionExecutor
                      │                      │                  │
                 risk + OS gates        apply / revert    restore point
                                                            + ChangeLog
                                                            + undo journal
```

| Layer | Responsibility |
|-------|----------------|
| **UI** (`WinCleaner`) | MVVM (`CommunityToolkit.Mvvm`), pages, danger / anti-cheat dialogs |
| **Core** (`WinCleaner.Core`) | Registry, services, AppX, elevated commands, health tools, executor |
| **Data** (`WinCleaner.Data`) | Versioned catalogs + custom profile documents |
| **Safety** | Blocked hard-fail, Dangerous gate, restore points, session journal |

Composition root: `AppServices` in `TweakActionFactory.cs` (manual wiring, no DI container).

### Projects

| Project | TFM | Depends on |
|---------|-----|------------|
| `WinCleaner` | `net8.0-windows` (WPF) | Core, Data, CommunityToolkit.Mvvm |
| `WinCleaner.Core` | `net8.0-windows` | Data, Registry / WMI / ServiceController |
| `WinCleaner.Data` | `net8.0` | Embedded `Catalogs/*.json` |
| `WinCleaner.Core.Tests` | `net8.0-windows` | Core, Data, xUnit |

### UI navigation

| Nav key | ViewModel | View |
|---------|-----------|------|
| `dashboard` | `DashboardViewModel` | `DashboardView` |
| `services` | `ServicesViewModel` | `ServicesView` |
| `telemetry` / `gaming` / `win11` | `TweaksCategoryViewModel` | `TweaksView` |
| `bloatware` | `BloatwareViewModel` | `BloatwareView` |
| `visual` | `VisualViewModel` | `VisualView` |
| `network` | `NetworkViewModel` | `NetworkView` |
| `tools` | `ToolsViewModel` | `ToolsView` |
| `installer` | `PackageInstallerViewModel` | `PackageInstallerView` |
| `presets` | `PresetsViewModel` | `PresetsView` |
| `settings` | `SettingsViewModel` | `SettingsView` |

Theme: `Themes/DarkRed.xaml` (dark base `#0D0D0D`, accent `#E10600`). Localization swaps `Resources/Strings.{lang}.xaml` at runtime (`tr` default).

### Action types

Catalog entries are mapped to `IChangeAction` implementations:

| Type | Class | Typical source |
|------|-------|----------------|
| `registry` (single) | `RegistryChangeAction` | Most tweaks |
| `registry` (multi) | `MultiRegistryChangeAction` | Multi-key toggles / `registryVariants` |
| `service` | `ServiceChangeAction` | Services catalog + WU pause tweak |
| `command` | `CommandChangeAction` | bcdedit / elevated tools |
| `powerplan` | `PowerPlanChangeAction` | Ultimate Performance |
| `task` | `TaskDisableAction` | CEIP scheduled tasks |
| AppX / OneDrive | `AppxRemoveAction`, `OneDriveUninstallAction` | Bloatware page |

### Core services

| Service | Role |
|---------|------|
| `ActionExecutor` | Apply / revert with risk gates, restore points, journaling |
| `ChangeLogService` | Session audit + persistent undo journal |
| `RestorePointManager` | System Restore checkpoints (WMI / PowerShell, ~10 min rate limit) |
| `RegistryManager` | Hive read / write / delete |
| `ServiceManager` | Service start-mode changes |
| `AppxManager` | AppX list / remove; OneDrive uninstall helper |
| `DnsChangerService` | DNS presets via WMI + flush |
| `WingetInstallerService` | Silent `winget install --id …` |
| `SystemHealthService` | DISM / SFC, WinSxS, God Mode, Take Ownership, WU / network reset |
| `UpdateService` | Compare assembly version to GitHub Releases API |
| `PresetEngine` | Build preset / custom-profile action lists |

---

## Safety model

| Control | Default | Behavior |
|---------|---------|----------|
| System Restore point | **On** | Created before changes (Windows rate-limits ~10 min) |
| Allow dangerous actions | **Off** | Must enable in Settings |
| Confirmation dialog | Required | Checkbox acknowledgment for Dangerous items |
| Anti-cheat confirm | Flagged tweaks | Separate dialog for VBS / HPET / MMCSS-class items |
| Blocked services | Always | Cannot be toggled or applied via presets |
| AppX / OneDrive | Confirm | Removals are generally **not** undoable via journal |
| Preview | Yes | Preset / batch preview lists actions before apply |
| OS gate | Enforced | `minBuild` / `win11Only` filtered by `OsCompatibility` |

> README historically called this “dry-run”. The current build shows an **action preview**; it does not simulate registry writes without applying.

Session logs: `%LocalAppData%\WinCleaner\logs\session-*.json`  
Undo journal: `%LocalAppData%\WinCleaner\undo_history.json`  
Settings: `%LocalAppData%\WinCleaner\settings.json`

---

## Updates

In-app update checks query:

```
https://api.github.com/repos/emirttac/WinCleaner/releases/latest
```

Publish a GitHub Release with a `vX.Y.Z` (or `X.Y.Z`) tag so clients can detect newer versions. The app opens the release page in the browser — there is **no** silent auto-install.

---

## Localization

UI strings live in `src/WinCleaner/Resources/Strings.*.xaml` (485+ keys each; key parity enforced by unit tests).  
Catalog display names / descriptions use `displayNameKey` / `descriptionKey` resolved through the same dictionaries.

Regenerate XAML from JSON sources:

```powershell
powershell -ExecutionPolicy Bypass -File tools\i18n\_build_now.ps1
```

---

## Testing

```powershell
dotnet test
```

Coverage focuses on:

- Catalog load / risk parsing / anti-cheat flags / OS gates  
- Localization key parity across all 8 languages  
- DNS presets, BCD parse helpers, God Mode / Take Ownership round-trips  
- Update version compare + custom profile schema (including legacy fields)

Live registry / service apply–revert is not fully automated — smoke-test on a VM before shipping a Release.

---

## Contributing

1. Fork and create a feature branch  
2. Keep catalogs valid JSON; add localization keys to **all** `Strings.*.xaml` languages (or regenerate via `tools/i18n`)  
3. Mark high-impact tweaks with the correct `risk` and `requiresAntiCheatConfirm` when relevant  
4. Run `dotnet test` before opening a PR  
5. Sync `SOURCE_CODES` if you change the public source snapshot:  
   `scripts\sync-source-codes.ps1`

Ideas that help most: safer defaults, better OS gating, executor / revert tests, docs & screenshots.

---

## Disclaimer

WinCleaner is provided **as is** under the MIT License. Incorrect use can break Windows Update, Store, networking, or anti-cheat.  
The authors are **not liable** for data loss, bans, or system damage. Prefer presets over blind “apply all”, and create a restore point first.

---

## License

[MIT](LICENSE) © [emirttac](https://github.com/emirttac)

Repository: https://github.com/emirttac/WinCleaner
