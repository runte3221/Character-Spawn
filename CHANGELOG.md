# Changelog

All notable changes to this project will be documented in this file.

## [0.1.10] - 2026-10-02
### Fixed
- **AQR-Conforming Monster & Non-Humanoid Spawning**: Adopted A Quest Reborn (AQR) architectural pattern for monster and non-humanoid NPC spawning. By setting `ModelContainer.ModelCharaId`, disabling weapons, and triggering Penumbra's `RedrawObject`, models (such as Ruin Runner, Antelope Doe, Gnat, Letter Moogle) now properly instantiate and render instead of showing only a gizmo.
- **Glamourer & Penumbra Appearance Application**: 
  - Fixed Glamourer IPC calls by applying proper flags (`flags = 7`: Customization | Equipment | Accessories) instead of `0`.
  - Added automatic resolution from design names to GUIDs in `ApplyDesignToActor`.
  - Ensured Penumbra collection assignment (`allowCreate = true, allowDelete = true`) executes before Glamourer design application, followed by `RedrawObject`.
  - Resolved the bug where spawning Glamourer / Penumbra presets spawned the local player's appearance.
- **MCDF Appearance Application**: Ensured MCDF parsed Glamourer designs are dispatched with `flags = 7` and synchronized with Penumbra Redraw, eliminating player clone fallbacks.
- **NPC Weapon Residuals Fix**: Set default `WeaponVisible = false` on NPC character templates to eliminate unintended local player weapon rendering (e.g. Ru Sushimo, Miounne).
- **Weapon Visibility Toggle (ON/OFF)**: Linked `SetWeaponVisibility` with Penumbra `RedrawObject`, guaranteeing immediate model re-render when toggling weapon visibility back ON or OFF.
- **Sorted Dropdown Lists**: Alphabetically sorted Glamourer designs and Penumbra collections in dropdown combo boxes for fast navigation.
- **UI Layout Separator Bleed Fix**: Encapsulated the character detail right pane inside `ImGui.BeginChild("RightDetailPane")`, preventing horizontal `ImGui.Separator()` lines from bleeding across into the left tree pane.
- **Removed Extraneous Guide Text**: Removed helper text annotations (`<= 武器表示ON/OFF`, `<= ギズモ表示ON/OFF`) and the preview explanation box per user feedback.

## [0.1.9] - 2026-10-02
### Added
- **Log Tab & LogManager**: Added a dedicated "Log" tab next to "Settings" in the main window with real-time log monitoring (info/warning/error color-coding, search filter, auto-scroll, and one-click "Copy All" to clipboard) to easily diagnose appearance source detection and actor spawning.
- **Weapon Visibility Control**: Added a `[x] Weapon Visible` checkbox in the Character tab details pane, enabling instant hiding/displaying of equipped weapons for spawned actors.
- **BNpcLink Mapping for Monsters**: Embedded comprehensive `BNpcLink` mapping (13,312 entries) linking `BNpcName` to `BNpcBase`, resolving model resolution failures for monsters.
- **Full Search for NPCs and Monsters**: Completely removed artificial search caps (previously 500), allowing smooth full-library search across all game NPCs and monsters.

### Changed
- **Character Tab Preview Mode**: Separated character spawning in the Character tab into a dedicated preview mode conforming to user UI mockups:
  - Toggles between `[ Spawn ]` and red highlighted `[ Despawn ]`.
  - Displays green `[Name] Spawning...` status and `[x] Gizmo` toggle while active.
  - Explanatory banner explaining the temporary preview feature.
- **New Chara 2x2 Button Grid**: Redesigned the "Select Appearance Source" selector from a dropdown combo to a responsive 2x2 button grid (`[ Glamourer&Penumbra ] [ MCDF ]` / `[ NPC(ENpc) ] [ Monster/Mob ]`) with red accent highlighting on the active source.

### Fixed
- **Glamourer & Penumbra IPC Connectivity**: Upgraded IPC key subscribers to the latest versions (`Glamourer.ApiVersion.V2`, `Glamourer.GetDesignList.V2`, `Glamourer.ApplyState`, `Penumbra.ApiVersion.V5`, `Penumbra.GetCollections.V5`, `Penumbra.SetCollectionForObject.V5`, `Penumbra.RedrawObject.V5`) with backward-compatibility fallbacks and dynamic re-checking polling, fixing the persistent "IPC Not Detected" issue.
- **Non-Humanoid Model Spawning (Moogles, Monsters)**: Fixed an issue where non-humanoid actors (e.g. Letter Moogle, Gegeruju, monsters) failed to render or only displayed a gizmo. Decoupled humanoid player cloning from non-humanoid model initialization, preventing bone/resource container corruption.
- **Monster Player-Clone Bug**: Fixed bug where monsters (such as Matanga/Matagai) spawned with player appearance by correctly obtaining `ModelCharaId` via `BNpcLink`.
- **NPC Weapon Residuals**: Fixed bug where humanoid NPCs (such as Ru Sushimo) displayed local player weapons by strictly managing weapon hiding and DrawData equipment containers.
- **MCDF Spawning**: Fixed MCDF spawn behavior by ensuring parsed Glamourer design strings are applied directly to the spawned preview actor via updated Glamourer IPC.
- **3D Gizmo Dragging & Picking**: Substantially increased gizmo handle picking radii and added full axis-line segment hit detection, allowing smooth and intuitive dragging along any axis.

## [0.1.8] - 2026-10-02
### Added
- **Character Library UI Redesign**: Redesigned the Character Library layout into a two-pane hierarchical structure matching the user mockups:
  - Left Pane: Folder & character tree view with expandable folder categories, plus bottom action buttons (`[New Chara]`, `[New Folder]`, `[Delete]`).
  - Right Pane: Inline character name display & editing, with action buttons (`[Spawn]`, `[edit]`, `[delete]`) and a detailed summary of appearance attributes.
  - Character Modal: Dedicated popup window for creating and editing characters with clear appearance source selection and settings.
  - New Folder Modal: Popup dialog to create new organization folders.
- **Glamourer & Penumbra Integration UI**: Added AQR-style searchable dropdown combos for selecting Glamourer designs and Penumbra collections directly from IPC, with optional manual string input.
- **Explorer File Picker for MCDF**: Integrated Win32 `GetOpenFileNameW` dialog via an asynchronous STA worker thread to open Windows File Explorer and browse `.mcdf` files directly, automatically parsing and populating Glamourer appearance data upon selection.
- **Humanoid & Non-Humanoid NPC Appearance Extraction**: Added `GetNpcAppearanceData` to extract 26-byte `CustomizeData` and 10-slot equipment model IDs from `ENpcBase` and `NpcEquip` Lumina sheets.

### Fixed
- **NPC Spawning as Player Character**: Fixed a critical bug where spawning NPCs (such as Letter Moogle or Miounne) resulted in the local player's appearance. Implemented proper `ModelContainer.ModelCharaId` assignment for non-humanoid NPCs and direct `DrawData.CustomizeData` & `EquipmentModelIds` buffer population for humanoid NPCs.
- **Search Result Limits**: Expanded search result caps for Monsters and NPCs from 10 to 500 items, with smooth scrollable list boxes.

## [0.1.7] - 2026-10-02
### Fixed
- **3D Model Rendering & Visibility**: Resolved the issue where only the 3D gizmo appeared without the character model. Implemented continuous per-frame draw enforcement (`UpdateFrame`), clearing DrawObject hidden flags (0x10) and ensuring `EnableDraw()` is triggered once `IsReadyToDraw()` becomes satisfied.
- **Penumbra & Glamourer IPC Synchronization**: Added `Penumbra.RedrawObject` and `Glamourer.ReapplyState` triggers upon spawning to immediately build and render custom character models in modded environments.
- **Template Auto-Population**: Added automatic capture of the local player's current Glamourer design when saving player clone or glamourer templates with empty design strings.

## [0.1.6] - 2026-10-02
### Fixed
- **UI Responsiveness**: Replaced `Selectable` with an `ImGui.Table` layout in `CharacterLibraryTab.cs`, fixing an issue where "Spawn onto Map" and "Delete" buttons could not be clicked.
- **Actor Spawning & Despawning**: Refactored `ActorManager.cs` to utilize `ClientObjectManager.Instance()->CreateBattleCharacter` and `DeleteObjectByIndex` (FFXIVClientStructs / Brio / AQR standard architecture) instead of failing SigScanner delegates, resolving the issue where spawned actors did not appear on the map.
- **Transform & Drawing Synchronization**: Implemented proper `CharacterSetup.CopyFromCharacter` initialization, `GameObject.EnableDraw()`, and direct position/rotation updates for spawned characters.

## [0.1.5] - 2026-10-02
### Fixed
- Fixed `SeString.TextValue` usage for Player and Target clone name extraction.

## [0.1.4] - 2026-10-02
### Fixed
- Fixed LocalPlayer access using `IObjectTable[0]` conforming to Dalamud API 15 standards.
- Fixed `ISigScanner` integration for native delegate resolution.
- Fixed `ObjectTargetableFlags.IsTargetable` type conversion.
- Fixed `ActionTimeline` collection index access in `GameDataService`.

## [0.1.3] - 2026-10-02
### Fixed
- Fixed `OnTerritoryChanged` signature to `uint` parameter.
- Fixed `PlayTimeline` and `StopTimeline` calls to supply required slot parameter.
- Fixed `OnNamePlateUpdate` signature to `(INamePlateUpdateContext, IReadOnlyList<INamePlateUpdateHandler>)`.
- Removed unused `activeTab` field in `MainWindow`.

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
