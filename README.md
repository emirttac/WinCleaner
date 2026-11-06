# WinCleaner

<p align="center">
  <img src="src/WinCleaner/app.ico" alt="WinCleaner" width="96" height="96">
</p>

**Windows 10/11 optimization and debloat toolkit for gamers** — C# · .NET 8 · WPF · **v2.5.0**

Catalog-driven tweaks · Risk levels · Restore points · Session undo · Headless config · 8 languages

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?logo=windows&logoColor=white)](https://github.com/emirttac/WinCleaner)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Releases](https://img.shields.io/github/v/release/emirttac/WinCleaner?include_prereleases&label=release)](https://github.com/emirttac/WinCleaner/releases)
[![Downloads](https://img.shields.io/github/downloads/emirttac/WinCleaner/total?label=downloads&logo=github)](https://github.com/emirttac/WinCleaner/releases)
[![Website](https://img.shields.io/badge/website-GitHub%20Pages-0D0D0D?logo=githubpages&logoColor=white)](https://emirttac.github.io/WinCleaner/)

**Site:** [emirttac.github.io/WinCleaner](https://emirttac.github.io/WinCleaner/) · [Developers](https://emirttac.github.io/WinCleaner/developers.html)

> **Warning.** WinCleaner changes Windows services, registry values, scheduled tasks, and AppX packages. Keep **System Restore** enabled, read each toggle before applying, and prefer presets over “apply everything.” You are responsible for your system.

> **Beta.** The app shows a startup notice that some settings may still be unstable. Please report broken functions via [GitHub Issues](https://github.com/emirttac/WinCleaner/issues).

---

## Table of contents

- [Why WinCleaner?](#why-wincleaner)
- [Features](#features)
- [Requirements](#requirements)
- [Download](#download)
- [Quick start (developers)](#quick-start-developers)
- [Headless / config mode](#headless--config-mode)
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
| **Dashboard** | CPU / GPU / RAM / OS / disk overview, restore status, recent changes, quick actions (restore point, Gamer Mode preset, flush DNS, clean temp) |
| **Presets** | Gamer Mode, Balanced Mode, Minimal / Server Mode, Laptop / Battery Mode, Maximum Privacy, Restore Defaults — plus custom profile import / export (JSON schema v3, v2 still reads) with preview |
| **Gaming** | 20 catalog tweaks: Game Mode, Game Bar/DVR, HAGS, Ultimate Performance, FSO, MMCSS / latency, mouse accel, VBS/HPET (anti-cheat confirm where flagged), and related gaming options |
| **Telemetry** | 15 privacy tweaks: advertising ID, activity history, Cortana–Bing, CEIP tasks, location, speech, Store suggestions, and related telemetry |
| **Bloatware** | 46 AppX packages (31 with Store ID reinstall via winget); `systemCritical` rows (Microsoft Store, Calculator) cannot be removed; separate OneDrive uninstall path |
| **Visual** | 20 visual tweaks (animations, transparency, dark mode, Explorer polish) plus startup-app toggles |
| **Win11** | 12 OS-gated UI tweaks: taskbar / Start / classic context menu / Copilot / Recall / widgets / Chat |
| **Services** | 28 curated Windows services with start-mode toggles; **Blocked** and never changeable: `RpcSs`, `DcomLaunch`, `WinDefend` |
| **Network** | 9 network tweaks + DNS presets (DHCP, Cloudflare, Google, Quad9) + flush |
| **Tools** | 16 Tools-category tweaks plus maintenance grid (restore point, temp cleanup, Disk Cleanup, SFC/DISM, WinSxS, WU/network reset, memory diagnostic, disk analyze, God Mode), Take Ownership, NTP/autologon/winget repair, registry import/export, legacy Control Panel launchers |
| **Installer** | 158 winget apps with silent install **and uninstall**, grouped Browsers / Communication / Developer / Gaming / Media / Tools, plus app icons |
| **Settings** | 8 languages, appearance (dark, light, or match Windows), auto restore point, allow dangerous actions, update-check toggle, logs, social links |

Global **Undo** lives in the main chrome and reverts journaled session changes where possible. The top search box opens a **global search** across pages.

### Product polish

- Dark gamer theme (`#0D0D0D` base, accent `#E10600`), plus light and “match Windows”. Missing `settings.json` theme stays dark. Text on panel surfaces targets at least 4.5:1; borders, focus, and status colors at least 3:1.
- Applied-state checks run off the UI thread, are cached, and the page shows “Loading…” until they finish.
- Startup dialogs and gate messages come from the language resources. Catalog text that is not a product name is translated.
- **8 languages:** Türkçe (default), English, Deutsch, Español, 中文, Français, Română, Русский
- Optional startup update check against [GitHub Releases](https://github.com/emirttac/WinCleaner/releases) (default **off**). A downloaded installer runs only after its SHA256 matches `SHA256SUMS.txt`.
- Releases are **unsigned**. The tag workflow publishes win-x64 only, plus `SHA256SUMS.txt` and an SPDX 2.3 file. An ARM64 PC still asks before a system write.
- **Installer auto-prereqs:** Setup downloads and quietly installs **.NET 8 Desktop Runtime (x64)** and **VC++ 2015–2022 (x64)** when missing. The app also re-checks at launch for framework-dependent builds.
- Crash and session logging under `%LocalAppData%\WinCleaner\`
- Headless profile apply via `--config` (see below)

### Catalog size (v2.5.0)

| Catalog | Entries | File |
|---------|--------:|------|
| Tweaks | 92 | `Catalogs/tweaks.json` |
| Services | 28 | `Catalogs/services.json` |
| Bloatware (AppX) | 46 | `Catalogs/apps.json` |
| Winget installer apps | 158 | `Catalogs/apps_installer.json` |
| Presets | 6 | `Catalogs/presets.json` |
| Languages | 8 | `Resources/Strings.*.xaml` (`tr` default) |

**Tweak categories** (92 total — Safe 53 · Caution 37 · Dangerous 2)

| Category | Count |
|----------|------:|
| Gaming | 20 |
| Visual | 20 |
| Tools | 16 |
| Telemetry | 15 |
| Windows11UI | 12 |
| Network | 9 |

**Services risk** (28 total): Safe 6 · Caution 17 · Dangerous 2 · Blocked 3 (`RpcSs`, `DcomLaunch`, `WinDefend`).

**Winget categories**

| Category | Count |
|----------|------:|
| Tools | 61 |
| Developer | 48 |
| Media | 23 |
| Browsers | 9 |
| Gaming | 9 |
| Communication | 8 |
| **Total** | **158** |

**Built-in presets** (UI names → catalog id)

| Preset | Tweaks | Services |
|--------|-------:|---------:|
| Gamer Mode (`preset-gamer`) | 16 | 3 |
| Balanced Mode (`preset-balanced`) | 5 | 2 |
| Minimal / Server Mode (`preset-minimal`) | 12 | 5 |
| Laptop / Battery Mode (`preset-laptop`) | 7 | 3 |
| Maximum Privacy (`preset-privacy-max`) | 13 | 5 |
| Restore Defaults (`preset-defaults`) | 0 (session undo) | 0 |

**Languages:** Türkçe (default), English, Deutsch, Español, 中文, Français, Română, Русский — 758 string keys each, parity enforced by tests.

Exactly **5** tweaks require a separate anti-cheat confirmation dialog (`tweak-mmcss-gpu`, `tweak-mmcss-priority`, `tweak-mmcss-scheduling`, `tweak-disable-vbs`, `tweak-disable-hpet`). **31 / 46** AppX rows have a `storeId` and support **Reinstall** via winget.

---

## Requirements

| | |
|--|--|
| **OS** | Windows 10 **21H2+** (build ≥ 19044) or Windows 11 (**x64**) |
| **Privileges** | Administrator (UAC `requireAdministrator`) |
| **Runtime (installer / FDD)** | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — Setup downloads it if missing |
| **VC++** | Microsoft Visual C++ 2015–2022 (x64) — Setup downloads it if missing |
| **Dev build** | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Installer build** | [Inno Setup 6](https://jrsoftware.org/isinfo.php) |

Portable builds are **self-contained** (runtime embedded) but still require a supported OS and admin rights. VC++ is still checked at startup and can be auto-installed when missing.

---

## Download

| Channel | Link |
|---------|------|
| **Installer (recommended)** | [Releases](https://github.com/emirttac/WinCleaner/releases) → `WinCleaner-Setup-2.5.0.exe` |
| **Portable** | Release assets or build via `scripts\publish-portable.ps1` → `publish\WinCleaner.exe` |
| **Source** | Clone this repo or open [`SOURCE_CODES/`](SOURCE_CODES/) |

The Inno Setup installer (`WinCleaner-Setup-2.5.0.exe`):

1. Installs WinCleaner under Program Files  
2. On the Ready page, detects missing **.NET 8 Desktop Runtime** and **VC++ Redistributable**, downloads them from Microsoft, and installs quietly  
3. Ships the open-source tree under `SOURCE_CODES` next to the app  

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

Output: `publish\setup\WinCleaner-Setup-2.5.0.exe`  
(Framework-dependent publish under `publish\installer`, then packaged by Inno. Missing runtimes are fetched at install time.)

### Refresh the open-source snapshot

```powershell
powershell -ExecutionPolicy Bypass -File scripts\sync-source-codes.ps1
```

---

## Headless / config mode

Apply a custom profile JSON without the full UI:

```powershell
WinCleaner.exe --config path\to\profile.json --apply --silent
WinCleaner.exe --config path\to\profile.json --whatif
WinCleaner.exe --config path\to\profile.json --apply --allow-unknown --log path\to\log.txt
```

| Flag | Meaning |
|------|---------|
| `--config <path>` | Profile JSON (schema v2 or v3) |
| `--apply` | Apply accepted tweak IDs |
| `--silent` | No summary MessageBox |
| `--whatif` | Plan only |
| `--allow-unknown` | Skip unknown IDs instead of rejecting the run |
| `--log <path>` | Optional log path |

Unknown IDs reject the run unless `--allow-unknown` is set.

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
                 risk + OS gates        apply / revert    session checkpoint
                                                            + ChangeLog
                                                            + undo journal
```

| Layer | Responsibility |
|-------|----------------|
| **UI** (`WinCleaner`) | MVVM (`CommunityToolkit.Mvvm` + WPF-UI), pages, danger / anti-cheat dialogs |
| **Core** (`WinCleaner.Core`) | Registry, services, AppX, elevated commands, health tools, executor |
| **Data** (`WinCleaner.Data`) | Versioned catalogs + custom profile documents |
| **Bootstrap** (`WinCleaner.Bootstrap`) | Alternate WinForms launcher for FDD + sidecar layouts |
| **Safety** | Blocked hard-fail, Dangerous gate, restore points, session journal |

Composition root: `AppServices` in `TweakActionFactory.cs` (manual wiring, no DI container).

### Projects

| Project | TFM | Depends on |
|---------|-----|------------|
| `WinCleaner` | `net8.0-windows` (WPF) | Core, Data, CommunityToolkit.Mvvm 8.4.2, WPF-UI 4.3.0 |
| `WinCleaner.Core` | `net8.0-windows` | Data, Registry / WMI / ServiceController |
| `WinCleaner.Data` | `net8.0` | Embedded `Catalogs/*.json` |
| `WinCleaner.Bootstrap` | `net8.0-windows` (WinForms) | Core (prereq install helpers) |
| `WinCleaner.Core.Tests` | `net8.0-windows` | Core, Data, xUnit |

### UI navigation

| Nav key | ViewModel | View |
|---------|-----------|------|
| `dashboard` | `DashboardViewModel` | `DashboardView` |
| `presets` | `PresetsViewModel` | `PresetsView` |
| `gaming` / `telemetry` / `win11` | `TweaksCategoryViewModel` | `TweaksView` |
| `bloatware` | `BloatwareViewModel` | `BloatwareView` |
| `visual` | `VisualViewModel` | `VisualView` |
| `services` | `ServicesViewModel` | `ServicesView` |
| `network` | `NetworkViewModel` | `NetworkView` |
| `tools` | `ToolsViewModel` | `ToolsView` |
| `installer` | `PackageInstallerViewModel` | `PackageInstallerView` |
| `settings` | `SettingsViewModel` | `SettingsView` |

Theme: `Themes/DarkRed.xaml` + `Themes/Controls.xaml`. Localization swaps `Resources/Strings.{lang}.xaml` at runtime (`tr` default).

### Action types

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
| `RestorePointManager` | One session checkpoint at startup (in-process gap ~10 min). Windows may still refuse a second point for 24 hours. |
| `RegistryManager` | Hive read / write / delete |
| `ServiceManager` | Service start-mode changes |
| `AppxManager` | AppX list / remove; OneDrive uninstall helper |
| `DnsChangerService` | DNS presets via WMI + flush |
| `WingetInstallerService` | Silent `winget install` / `uninstall` / `upgrade` |
| `SystemHealthService` | DISM / SFC, WinSxS, God Mode, Take Ownership, WU / network reset |
| `UpdateService` | Compare assembly version to GitHub Releases API |
| `PresetEngine` | Build preset / custom-profile action lists |
| `PrerequisiteChecker` | Detect / download / install .NET Desktop + VC++ |
| `ConfigSession` | Headless profile planning and apply |

---

## Safety model

| Control | Default | Behavior |
|---------|---------|----------|
| System Restore point | **On** | One checkpoint per app session, created at startup. Later tweaks do not create another point. Windows can skip a new point when one already exists in the last 24 hours. |
| Allow dangerous actions | **Off** | Must enable in Settings |
| Confirmation dialog | Required | Checkbox acknowledgment for Dangerous items |
| Anti-cheat confirm | Flagged tweaks | Separate dialog for VBS / HPET / MMCSS-class items |
| Blocked services | Always | Cannot be toggled or applied via presets |
| AppX / OneDrive | Confirm | Removals are generally **not** undoable via journal |
| Preview | Yes | Preset / batch preview lists actions before apply |
| OS gate | Enforced | `minBuild` / `win11Only` filtered by `OsCompatibility` |

> The UI shows an **action preview**; it does not simulate registry writes without applying.

### Undo matrix

| Kind | What revert writes | Applies to |
|------|--------------------|------------|
| **Exact** | The value read before apply | Single and multi registry, service start type, scheduled task, startup Run entry. Visual-effect mask when the prior bytes were stored. Active power plan when its GUID was stored. |
| **Scripted** | A fixed catalog script, not the previous machine value | Command tweaks (e.g. core parking, IPv6, folder discovery) |
| **Defaulted** | A built-in Windows default, not the previous value | Visual-effect registry keys on best-performance revert; power plan falls back to High Performance when no GUID was stored |
| **Scripted-enum** | The last detected preset name | DNS (adapter server lists are not restored) |
| **None** | No journal undo | AppX remove/reinstall, OneDrive uninstall, DISM `/ResetBase`, Winsock reset, Windows Update folder rename |

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

UI strings live in `src/WinCleaner/Resources/Strings.*.xaml` (key parity enforced by unit tests).  
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
