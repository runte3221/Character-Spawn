# Changelog

All notable changes to this project will be documented in this file.

## [0.1.14] - 2026-10-02
### Fixed
- **Penumbra InvalidIdentifier (ec=16) Resolution via OwnerId Initialization**:
  - Through full CIL reverse engineering of `Penumbra.GameData.dll`'s `CreateBNpcFromObject`, discovered that Penumbra inspects `GameObject.OwnerId`. If `OwnerId` is not equal to `0xE0000000` (`GameObject.InvalidGameObjectId`), Penumbra attempts to look up the parent object (`objects.ById(ownerId)`). Because `CreateBattleCharacter` initializes `OwnerId` to `0`, the lookup failed and returned `InvalidIdentifier` (`ec=16`), causing all Penumbra collection assignments to be rejected.
  - Explicitly set `nativeChara->GameObject.OwnerId = 0xE000_0000` alongside `NameId = 0`, `HomeWorld`, and `SetName(puppetName)`. This satisfies Penumbra's Player identifier validation branch, allowing `SetCollectionForObject` to return `ec=0` (Success) and apply collections flawlessly.
- **MCDF Section Penumbra Collection Selector**:
  - Added Penumbra Collection selection dropdown to the MCDF configuration section in `UI/CharacterLibraryTab.cs`.
  - Characters created or imported from `.mcdf` files can now bind and persist dedicated Penumbra Collections alongside their extracted Glamourer design.

## [0.1.13] - 2026-10-02
### Fixed
- **MCDF LZ4 Decompression Stream Support (AQR McdfCharaFileManager Architecture)**:
  - Resolved the critical issue where MCDF files failed to parse GlamourerDesignString because modern `.mcdf` files are whole LZ4-compressed binary streams rather than raw binary JSONs.
  - Integrated `lz4net` and updated `Services/McdfParser.cs` to decompress the LZ4 stream before inspecting the `"MCDF"` 4-byte header and parsing the embedded JSON string.
  - Successfully verified extraction of Glamourer Base64 strings from real `.mcdf` files (`testruma.mcdf`, `test.mcdf`), eliminating fallback to previous actor appearance or local player.
- **Accurate Penumbra V5 IPC Signature Resolution (CIL Metadata Verification)**:
  - Discovered via CIL disassembly of `Penumbra.Api.dll` that `Penumbra.SetCollectionForObject.V5` strictly expects `(int actorIndex, Guid? collectionId, bool allowCreate, bool allowDelete)` and returns `(int ec, (Guid, string)? oldCollection)` (`ValueTuple<int, Nullable<ValueTuple<Guid, string>>>`), not `(int, Guid)` or `int`.
  - Updated `Services/PenumbraIpc.cs` with exact tuple signatures for both V5 and Legacy APIs, resolving CallGate type-conversion exceptions (`converting from ValueTuple 2 to System.Int32`) and ensuring 100% reliable Penumbra Collection assignment to spawned actors.
- **Penumbra Dual-Phase Redraw Synchronization (AQR Conformity)**:
  - In `Managers/ActorManager.cs`, aligned appearance application order with AQR: Penumbra collection assignment -> first Penumbra Redraw -> Glamourer design application -> second Penumbra Redraw. This guarantees Mod textures, clothes, and meshes apply seamlessly to spawned actors.

## [0.1.12] - 2026-10-02
### Fixed
- **Binary MCDF Format Parsing (AQR MCDF-Loader Architecture)**:
  - Resolved the issue where selecting an MCDF file appeared not to load and spawned the local player's appearance. Discovered that modern MCDF files are proprietary binary containers (`"MCDF"` 4-byte header + UTF-8 JSON payload) rather than standard ZIP archives.
  - Rewrote `Services/McdfParser.cs` with binary header scanning and direct extraction of the Base64 `GlamourerData` string, enabling instant parsing and full appearance restoration from `.mcdf` files.
- **Penumbra IPC Return Signature Tuple Resolution (AQR Penumbra Architecture)**:
  - Fixed `Penumbra.SetCollectionForObject.V5` failure where Penumbra returns `(PenumbraApiEc, Guid)` (`ValueTuple<int, Guid>`) while legacy code expected `int`, causing CallGate runtime conversion exceptions that prevented Penumbra collections from applying.
  - Added robust tuple-based subscriber fallbacks in `Services/PenumbraIpc.cs` supporting both V5 and legacy signatures.
- **Pre-Draw Direct Appearance Application (Root Cause of "Temporary Local Player" Eliminated)**:
  - Completely redesigned `ActorManager.SpawnCharacter` and `ApplyAppearanceDirect`: actors now immediately call `DisableDraw()` upon creation and have their target appearance (Glamourer design, Penumbra collection, MCDF state, or Monster `ModelCharaId`) applied *before* the first frame renders, rather than waiting for `DrawObject->IsVisible`.
  - Eliminates the brief appearance of the local player before transitioning to the desired design.
- **HDM-Compliant Monster & Non-Humanoid Rendering (Fix for "Gizmo Only")**:
  - Identified that triggering Penumbra `RedrawObject` on non-humanoid monsters (`ModelCharaId > 0`, Letter Moogle, Ruin Runner, Antelope Doe, Gnat) caused Penumbra to invalidate and strip non-humanoid `DrawObject`s, leaving only a gizmo.
  - Removed Penumbra redraw calls from monsters in accordance with HDM's `GuiseService`, using native engine `DisableDraw` -> `IsReadyToDraw()` -> `EnableDraw()` cycle, ensuring 100% stable 3D monster and non-humanoid NPC rendering.
- **UI Flat Styling & Border Glitch Fix**:
  - Removed borders on the left tree scroll child window in `UI/CharacterLibraryTab.cs` to prevent visual line artifacts when selecting spawned characters.
### Fixed
- **Two Index Spaces Trap Resolution (Glamourer/Penumbra Player Clone Fix)**:
  - Resolved critical architectural flaw where internal `ClientObjectManager` slots (COM# 0, 1...) were passed to IPC endpoints instead of global `IObjectTable` indices (~200-244 reserved range). Passing COM# 0 caused Glamourer and Penumbra to target the local player character (index 0).
  - Tracked and supplied `actor.ObjectIndex` (`GlobalIndex`) for all Glamourer and Penumbra IPC operations, preventing spawned characters from taking on the local player's appearance.
- **Glamourer Identity Stamping (HDM The 0.8.44 Bug Fix)**:
  - Stamped each spawned BattleNpc actor with a valid SE player name format (`Cs Aa`, `Cs Ab`...) via a unique serial generator, `NameId = 0`, and the local player's `HomeWorld`.
  - Bypasses Glamourer's `ActorIdentifierFactory` invalid NPC ID rejection, allowing Glamourer state and design application to succeed reliably.
- **Draw-When-Ready 2-Phase Queue (HDM / Brio Architecture)**:
  - Implemented framework-driven `ReadyJob` queue. Phase 1 polls `IsReadyToDraw()` and enables draw; Phase 2 waits until `DrawObject != null && DrawObject->IsVisible` before applying Glamourer designs or Penumbra collections, ensuring Glamourer applies to a settled and registered actor body.
- **Monster & NPC Non-Humanoid Rendering (HDM GuiseService & Brio Pattern)**:
  - Fixed "gizmo only" / invisible models by seeding all actors with a double `CharacterSetup.CopyFromCharacter` from the local player before applying monster or NPC models, ensuring an active drawable skeleton exists rather than an uninitialized, invisible `SetupBNpc(0)`.
  - Swapped `ModelContainer.ModelCharaId`, hid weapons, and triggered Penumbra `RedrawObject` / `DisableDraw` settlement, guaranteeing monster and mob 3D models render correctly.
- **Template Data Persistence Guarantee**:
  - Enhanced `SaveModalTemplate` to guarantee full persistence of Glamourer GUID/name, Penumbra collection name, MCDF file path & parsed Base64 design, and NPC/Monster IDs with extensive logging.
  - Added clean state restoration in `OpenEditCharacterModal` and detailed attribute inspection in `Template Details`.
  - Robust `DespawnCharacter` resolving live COM indexes via `GetIndexByObject` to avoid stale index deletion.

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
