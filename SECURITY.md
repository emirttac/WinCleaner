# Security

WinCleaner changes Windows settings, services, and optional features. Run it only on a machine you administer.

## Releases

Releases are **unsigned**. Before you run `WinCleaner.exe`, compare its SHA256 with `SHA256SUMS.txt` from the same GitHub Release. There is no Authenticode signature. A missing or mismatched hash means the file must not be run. The app’s update check does the same and stays off unless you turn it on.

`win-arm64` is not published. An ARM64 PC still asks before a system write.

## Reporting

Report a vulnerability through GitHub Security Advisories on this repository, or open a private report if that feature is available. Do not post exploit steps in a public issue.

## Scope

Blocked and Dangerous catalog gates are not optional bypasses. A tweak marked Blocked cannot be applied. Dangerous actions stay off until Settings allows them.
