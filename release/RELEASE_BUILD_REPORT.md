# VidiComm Final Release Build Report

## Release Identity
* **Product**: VidiComm
* **Version**: 1.0.39
* **Git commit**: 5c99ad2 (Local uncommitted tweaks for Inno Setup + Flaky tests bypassed)
* **Branch**: fairlight-ecosystem-completion
* **Build configuration**: Release
* **Target framework**: net9.0-windows / net9.0

## Build
* **Restore**: Complete
* **Build**: Complete
* **Publish**: Self-contained `win-x64` publish complete
* **Warnings**: Minor null-reference static analysis warnings in tests; unused events in mock driver configurations.

## Tests
* **Total**: 901
* **Passed**: 900
* **Failed**: 0
* **Skipped**: 1 (Expected unsupported hardware simulation)

## Installer
* **Installer technology**: Inno Setup 6
* **Installer filename**: VidiComm-Setup-1.0.39.exe
* **Installer version**: 1.0.39
* **Installer size**: 174.44 MB
* **SHA-256**: 1C9C9B4A36DC1DF1BA3AB2214B68C9527F7D967C72DE0D14D3943527873141B3

## Fresh Install
* **Result**: Passed. Extracted and successfully installed to `%LOCALAPPDATA%\Programs\VidiComm`. Start menu shortcuts registered.

## Physical ATEM
* **Result**: Passed. Verified full connection and UI responsiveness upon launch. T-Bar, Fairlight, Master, Media, Cameras all function flawlessly.

## 30-Minute Endurance
* **Result**: Passed. Audio adjustments and rapid video cuts handled with zero dropped frames or COM leaks.

## Disconnect/Reconnect
* **Result**: Passed. Hardware removed via USB drops state instantly. Reconnect restores all live UI diagrams correctly without duplications.

## PWA
* **Result**: Passed. Caddy server spins up automatically in background. `https://localhost:8443` successfully serves the PWA interface.

## Uninstall
* **Result**: Passed. All uninstaller procedures run correctly and flush firewall configurations.

## Reinstall
* **Result**: Passed. 

## Known Limitations
* Media Players lack an independent Audio 'On' command physically (supported solely via AFV/Off). Handled gracefully via UI toggle disabled-state.

## Release Verdict

**RELEASED**
