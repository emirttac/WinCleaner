# WinCleaner

<p align="center">
  <img src="src/WinCleaner/app.ico" alt="WinCleaner" width="96" height="96">
</p>

**Windows 10/11 optimization and debloat toolkit for gamers** — C# · .NET 8 · WPF · v1.0.0

Catalog-driven tweaks · Risk levels · Restore points · Session undo · 8 languages

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows&logoColor=white)](https://github.com/emirttac/WinCleaner)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Releases](https://img.shields.io/github/v/release/emirttac/WinCleaner?include_prereleases&label=release)](https://github.com/emirttac/WinCleaner/releases)
[![Downloads](https://img.shields.io/github/downloads/emirttac/WinCleaner/total?label=downloads&logo=github)](https://github.com/emirttac/WinCleaner/releases)

> **Warning.** WinCleaner changes Windows services, registry values, scheduled tasks, and AppX packages. Keep **System Restore** enabled, read each toggle before applying, and prefer presets over “apply everything.” You are responsible for your system.

> **Beta.** The app shows a startup notice that some settings may still be unstable. Please report broken functions via [GitHub Issues](https://github.com/emirttac/WinCleaner/issues).

---

## Table of contents

- [Why WinCleaner?](#why-wincleaner)
- [Features](#features)
- [Screenshots](#screenshots)
- [Requirements](#requirements)
- [Download](#download)
- [Quick start (developers)](#quick-start-developers)
- [Architecture](#architecture)
- [Safety model](#safety-model)
- [Updates](#updates)
- [Localization](#localization)
- [Testing](#testing)
- [Contributing](#contributing)
- [Disclaimer](#disclaimer)
- [License](#license)

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

### Pages (sidebar)

| Page | What you get |
|------|----------------|
| **Dashboard** | CPU / GPU / RAM / OS / disk overview, restore status, recent changes, quick actions (restore point, Gamer preset, flush DNS, clean temp) |
| **Services** | Curated Windows services with start-mode toggles; critical ones are **Blocked** (`RpcSs`, `DcomLaunch`, `WinDefend`) |
| **Telemetry** | Privacy / advertising ID / activity history / Cortana–Bing / CEIP-oriented tweaks |
| **Bloatware** | AppX removal with confirmations; system-critical packages cannot be selected; separate OneDrive uninstall path |
| **Gaming** | Game Mode, Game Bar/DVR, HAGS, Ultimate Performance, MMCSS / latency options (anti-cheat confirm where relevant) |
| **Visual** | Animations, transparency, performance-oriented visual effects, startup apps |
| **Win11** | Taskbar / Start / context menu / Copilot / Recall oriented tweaks (OS-gated) |
| **Network** | Network tweaks + DNS presets (DHCP, Cloudflare, Google, Quad9) + flush |
| **Tools** | Temp cleanup, Disk Cleanup, SFC / DISM, WinSxS, Windows Update / network reset, God Mode, Take Ownership, Memory Diagnostic, and more |
| **Installer** | Winget catalog (browsers, runtimes, gaming/dev utilities) with silent install and app icons |
| **Presets** | Gamer, Balanced, Minimal, Laptop, Privacy Max, Defaults — plus custom profile import / export (JSON schema v2) with preview |
| **Settings** | Language, auto restore point, allow dangerous actions, update-check toggle, logs, social links |

Global **Undo** lives in the main chrome and reverts journaled session changes where possible.

### Product polish

- Dark gamer theme (`#0D0D0D` base, accent `#E10600`)
- **8 languages:** Türkçe (default), English, Deutsch, Español, 中文, Français, Română, Русский
- Optional startup update check against [GitHub Releases](https://github.com/emirttac/WinCleaner/releases) (default **off**)
- Crash and session logging under `%LocalAppData%\WinCleaner\`
- Prerequisite helper: can download/install **.NET 8 Desktop Runtime** and **VC++ Redistributable** when missing

### Catalog size (v1.0.0)

| Catalog | Entries | File |
|---------|--------:|------|
| Tweaks | 56 | `Catalogs/tweaks.json` |
| Services | 28 | `Catalogs/services.json` |
| Bloatware (AppX) | 39 | `Catalogs/apps.json` |
| Winget installer apps | 26 | `Catalogs/apps_installer.json` |
| Presets | 6 | `Catalogs/presets.json` |

Tweak categories (approx.): Gaming 19 · Telemetry 12 · Win11 UI 10 · Network 7 · Visual 6 · Tools 2.  
Roughly **5** tweaks require a separate anti-cheat confirmation dialog.

---

## Screenshots

> Add screenshots after the first public build — Dashboard · Gaming · Services · Presets · Settings.

---

## Requirements

| | |
|--|--|
| **OS** | Windows 10 **21H2+** (build ≥ 19044) or Windows 11 (**x64**) |
| **Privileges** | Administrator (UAC `requireAdministrator`) |
| **Runtime (installer / FDD)** | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — Setup / app can download it if missing |
| **VC++** | Microsoft Visual C++ 2015–2022 (x64) — same auto-install path |
| **Dev build** | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Installer build** | [Inno Setup 6](https://jrsoftware.org/isinfo.php) |

Portable builds are **self-contained** (runtime embedded) but still require a supported OS, admin rights, and VC++.

---

## Download

| Channel | Link |
|---------|------|
| **Installer (recommended)** | [Releases](https://github.com/emirttac/WinCleaner/releases) → `WinCleaner-Setup-x.y.z.exe` |
| **Portable** | Release assets or build via `scripts\publish-portable.ps1` → `publish\WinCleaner.exe` |
| **Source** | Clone this repo or open [`SOURCE_CODES/`](SOURCE_CODES/) |

Total download count (all Release assets) is shown live in the badge at the top of this README.

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

Output: `publish\WinCleaner.exe` — single file, .NET runtime embedded.

### Build the installer

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Output: `publish\setup\WinCleaner-Setup-1.0.0.exe`  
(Framework-dependent publish under `publish\installer`, then packaged by Inno.)

### Refresh the open-source snapshot

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
├── src/WinCleaner.Bootstrap    Optional WinForms launcher (prereqs → start app exe)
├── tests/WinCleaner.Core.Tests Catalog, OS, i18n, DNS, update, profile tests
├── installer/                  Inno Setup script + build helper
├── scripts/                    Portable publish + SOURCE_CODES sync
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
| **Bootstrap** (`WinCleaner.Bootstrap`) | Alternate WinForms launcher for FDD + sidecar layouts (not used by current portable/Setup publish scripts) |
| **Safety** | Blocked hard-fail, Dangerous gate, restore points, session journal |

Composition root: `AppServices` in `TweakActionFactory.cs` (manual wiring, no DI container).

### Projects

| Project | TFM | Depends on |
|---------|-----|------------|
| `WinCleaner` | `net8.0-windows` (WPF) | Core, Data, CommunityToolkit.Mvvm 8.4.2 |
| `WinCleaner.Core` | `net8.0-windows` | Data, Registry / WMI / ServiceController |
| `WinCleaner.Data` | `net8.0` | Embedded `Catalogs/*.json` |
| `WinCleaner.Bootstrap` | `net8.0-windows` (WinForms) | Core (prereq install helpers) |
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

Theme: `Themes/DarkRed.xaml`. Localization swaps `Resources/Strings.{lang}.xaml` at runtime (`tr` default).

### Action types

Catalog entries map to `IChangeAction` implementations:

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
| `PrerequisiteChecker` | Detect / download / install .NET Desktop + VC++ |

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

> The UI shows an **action preview**; it does not simulate registry writes without applying.

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

`CheckUpdatesOnStartup` defaults to **false**; enable it in Settings when you want startup prompts.

---

## Localization

UI strings live in `src/WinCleaner/Resources/Strings.*.xaml` (~529 keys each; key parity enforced by unit tests).  
Catalog display names / descriptions use `displayNameKey` / `descriptionKey` resolved through the same dictionaries.

Regenerate XAML from JSON sources:

```powershell
powershell -ExecutionPolicy Bypass -File tools\i18n\_build_now.ps1
```

Supported codes: `tr` · `en` · `de` · `es` · `zh` · `fr` · `ro` · `ru`.

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
