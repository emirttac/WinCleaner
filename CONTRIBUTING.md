# Contributing

## Catalog

A tweak in `tweaks.json` must include `reversibility`: `Exact`, `Defaulted`, `Scripted`, or `None`. `ReversibilityRules.Validate` rejects a missing or unknown value. `risk` is `Safe`, `Caution`, `Dangerous`, or `Blocked`.

`Blocked` cannot be applied. `Dangerous` stays off until the user allows it. Do not loosen those gates.

`skus` and `archs` are optional. An empty list means no restriction. An unknown SKU stays visible. Home must not gain a Hyper-V row: that tweak keeps `skus` of Pro, Enterprise, Education, LTSC, and Server.

If a feature was already enabled, revert leaves it enabled.

Product names that stay identical to English belong in `tools/i18n/catalog-copy-allowlist.txt`. Every other catalog string of 12 characters or more must differ from the English value in the other seven languages. New string keys go in all eight `Strings.*.xaml` files.

Winget ids are unique and match `Publisher.Package` or a 12-character Store id. The catalog test does not call `winget show`.

Do not copy WinUtil text. If a row is adapted from WinUtil, name the WinUtil key in a note. Do not add presets named Standard, Minimal, or Advanced as copies of WinUtil. The existing six presets stay.

## Build

```powershell
dotnet test WinCleaner.slnx -c Release
dotnet format WinCleaner.slnx --verify-no-changes
```

`win-arm64` publish is refused. The release script is `scripts/publish-release.ps1`. It writes SHA256 and SPDX 2.3. The release note says the build is unsigned.
