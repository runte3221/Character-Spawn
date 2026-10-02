# Changelog

All notable changes to this project will be documented in this file.

## [0.1.2] - 2026-10-02
### Fixed
- Fixed compilation error by switching from deprecated `ImGuiNET` to Dalamud API 15 standard `Dalamud.Bindings.ImGui`.

## [0.1.1] - 2026-10-02
### Fixed
- Fixed native actor management using SigScanner delegates for FFXIV 7.x compatibility.
- Fixed `NamePlateController` event handling to match `INamePlateUpdateHandler` API.
- Fixed `TimelineManager` animation trigger via `PlayTimeline`.
- Added `repo.json` manifest for Dalamud custom plugin repository installer.
- Added `IGameInteropProvider` service injection.

## [0.1.0] - 2026-10-02
### Added
- Initial project structure and build configuration for Character Spawn plugin.
- Character Library system (supports Glamourer, Penumbra, MCDF, Monsters, and Player clone).
- Stage & Scene placement system with 3D gizmo and UI transform controls.
- Animation control with seamless loop toggle and facial expression settings.
- Player look-at (head tracking) capability.
- Customizable nameplates and targetability toggle.
- Stagehand-like scene preset management with zone-based auto-spawn.
