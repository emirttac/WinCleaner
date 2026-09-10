# SOURCE_CODES

This folder contains the **open-source snapshot** of WinCleaner for audit and redistribution.

It mirrors the buildable project tree (without `bin/` / `obj/`):

```
SOURCE_CODES/
├── src/
│   ├── WinCleaner/           # WPF UI (MVVM)
│   ├── WinCleaner.Core/      # Services, actions, safety executor
│   └── WinCleaner.Data/      # Embedded JSON catalogs
├── tests/
│   └── WinCleaner.Core.Tests/
├── installer/                # Inno Setup script + build helper
├── scripts/                  # sync-source-codes.ps1
├── assets/
├── WinCleaner.slnx
├── LICENSE
└── .gitignore
```

## Build from this folder

```powershell
cd SOURCE_CODES
dotnet restore WinCleaner.slnx
dotnet build src\WinCleaner\WinCleaner.csproj -c Release
dotnet test
```

The **canonical** working tree for development remains the repository root (`src/`, `tests/`).  
Refresh this snapshot with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\sync-source-codes.ps1
```

## License

See [`LICENSE`](LICENSE) (MIT). The same file lives at the repository root.
