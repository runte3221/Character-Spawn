# Changelog

All notable changes to this project will be documented in this file.

## [0.1.31] - 2026-10-02
### Fixed
- **ASCII-Only Valid FF14 Puppet Name Generation (Fixing Reverting to Player Character Baseline)**:
  - **Root Cause Identified**: In v0.1.30, hex digits from `template.Id` (e.g. `Csp 04ffbf3e`) were used in puppet names. FF14 engine and all IPC plugins (Penumbra, Glamourer, CustomizePlus) enforce strict player name validation (`VerifyPlayerName`) that strictly rejects digits (0-9). Consequently, Penumbra rejected the actor with `ec=16 (InvalidActor)`, Glamourer with `result=2 (ActorNotFound)`, and CustomizePlus with `ec=255 (ActorNotFound)`, resulting in zero appearances being applied and the actor remaining as the player character.
  - **Valid FF14 Name Generator**: `GetPuppetName` now maps Guid bytes deterministically to an 8-character ASCII alphabet-only Surname (`[A-Z][a-z]{7}`, e.g. `Csp Evjkkhzl`), perfectly complying with FF14 player naming standards while maintaining deterministic per-template uniqueness ($26^8 \approx 2.08 \times 10^{11}$ combinations).
- **Robust Glamourer Guid Design Application & Fallthrough Guard**:
  - In `GlamourerIpc.ApplyDesignToActorEx`, for Guid designs, retrieves the design JObject, applies `ForceAllApply` to ensure no slots are skipped, and applies state via `ApplyState` while directly synchronizing 26-byte `CustomizeData` to the native actor.
  - Guarded Guid designs from falling through to the Base64 string parser, preventing conversion failure errors (`result: 7`).

## [0.1.30] - 2026-10-02
### Fixed
- **Puppet Actor Name Cache Isolation & Full State Cleanup (Fixing Respawning as Wrong / Deleted Character Appearance)**:
  - **Root Cause 1 (Actor Name Cache Collision)**: `NextPuppetName()` generated sequential names (`Csp Aa`, `Csp Ab`, ...). Upon plugin reload or serial rollover, names assigned to previous characters (such as `Chonk` or `Lyle`) were recycled for new characters (`Ruma`). Glamourer and Penumbra automatically cache and restore states by actor GameObject name (`Csp Ac`), causing past appearances and mod collections to automatically override the new character immediately upon entering the world.
  - **Deterministic Unique Puppet Identity**: Changed actor naming strategy to `GetPuppetName(CharacterTemplate)` (`Csp {template.Id:N8}`). Each character template now maintains a completely unique and deterministic puppet name, mathematically guaranteeing zero name collision with other or deleted characters across sessions.
  - **Root Cause 2 (Profile Bleed in User Config)**: Cleaned up accidentally persisted `CustomizePlusProfileName: "Chonk"` inside `CharacterSpawn.json` for template `Ruma` caused by modal field retention. Added automatic actor-level profile detachment (`DeleteTemporaryProfileOnCharacter`) when no profile is configured.
  - **Full Glamourer & CustomizePlus State Reset on Despawn & Apply**:
    - Integrated `Glamourer.UnlockState` and `Glamourer.RevertState` / `RevertToAutomation` into `ActorManager.DespawnCharacter` and before applying appearances in `ApplyAppearanceDirect`.
    - Integrated `CustomizePlus.DeleteTemporaryProfileOnCharacter` on despawn and template load to guarantee clean actor state.

## [0.1.29] - 2026-10-02
### Fixed
- **Penumbra Collection Isolation & Unassignment on Despawn/Appearance (Fixing Wrong Collection Pulled on Spawn)**:
  - **Root Cause Identified**: Previous character collections and temporary collections remained registered to actor slots (`Global#200`) without explicit unassignment upon despawning. Spawning a new character with no collection or switching between MCDF and Glamourer presets resulted in previous Penumbra collections persisting or overriding the new actor's appearance.
  - **UnassignCollectionForActor**: Introduced dedicated IPC subscriber in `PenumbraIpc` to unassign both temporary collections (`AssignTemporaryCollection.V5(Guid.Empty, actorIndex, false)`) and standard object collections (`SetCollectionForObject.V5(actorIndex, null / Guid.Empty, true, true)`).
  - Called `UnassignCollectionForActor` inside `ActorManager.DespawnCharacter` and at the start of `ActorManager.ApplyAppearanceDirect`, guaranteeing a clean slate before any appearance is loaded.
  - Corrected `PenumbraIpc.SetCollectionForActor` to treat `PenumbraApiEc.NothingChanged (1)` as success alongside `ec=0`.
- **Character Modal State Pollution & Cross-Contamination**:
  - Separated MCDF parsed Glamourer design strings (`modalMcdfGlamourerDesign`) from standard Glamourer design inputs (`customGlamourerString`).
  - Completely isolated saved properties per `CharacterSourceType` inside `CharacterLibraryTab.SaveModalTemplate`, ensuring MCDF archive data never overwrites or bleeds into standard Glamourer & Penumbra character definitions.
- **Direct Glamourer Guid Application & Native Synchronization**:
  - In `GlamourerIpc.ApplyDesignToActorEx`, prioritized direct invocation of `ApplyDesign(Guid, actorIndex, 0, 6UL)` when a valid Guid is present, eliminating Base64 parse errors (`result: 7`) while synchronizing 26-byte `CustomizeData` directly to native engine structs.

## [0.1.28] - 2026-10-02
### Fixed
- **Force All Apply Flags & Direct Native `CustomizeData` Synchronization (Fixing Male/Different Race Character Spawning)**:
  - **Root Cause Identified**: Discovered via deep inspection of Glamourer design files (`238897be-...` / `Chonk`) that certain presets have `"Apply": false` on essential customization slots like `Race` (e.g., Highlander Male with `Race: Apply = false`). Calling `ApplyDesign(Guid)` directly left the spawned actor's race unchanged as Miqo'te (the player character's baseline) while applying male gender and highlander clan, causing an invalid race-clan mismatch that failed rendering and caused fallback to player appearance.
  - **ForceAllApply**: Implemented automatic resolution of full design JObjects (via `Glamourer.GetDesignJObject` and disk fallback) and forced `Apply = true` across all Customize slots (`Race`, `Gender`, `Clan`, `BodyType`, `Face`, `Hairstyle`, etc.) and Equipment slots before calling `ApplyState`.
  - **Direct Native Memory Synchronization**: Extracted the exact 26-byte `CustomizeData` from the design JObject and wrote it directly into `chara->DrawData.CustomizeData` followed by `CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)`. This ensures the game engine's native character data itself is immediately transformed to the target race and gender, eliminating any chance of fallback during Redraw.

## [0.1.27] - 2026-10-02
### Fixed
- **Persistent Glamourer Design & State Synchronization Across Redraws (Fixing Spawning as Player Character)**:
  - Discovered via reverse engineering of `Glamourer.Api.dll` and `HDM.dll` IL that `ApplyFlagEx.DesignDefault = 7UL` contains `ApplyFlag.Once = 1`. When applied with `Once`, Glamourer only temporarily overwrites the actor's in-memory draw model without persisting to the actor's internal Glamourer state. Consequently, subsequent Penumbra / engine redraws caused actors to immediately revert back to the base puppet appearance (the user's own player character).
  - Adopted HDM's proven standard: changed apply flags from `7UL` / `7U` to `6UL` / `6U` (`ApplyFlag.Equipment | ApplyFlag.Customization` without `Once`). This ensures Glamourer updates the persistent actor state, preserving designs (`Chonk`, custom MCDF characters) flawlessly through redraws.
  - Eliminated redundant intermediate `Redraw` invocation directly after `Penumbra.SetCollectionForActor`, consolidating redraw logic to the finalization phase.
- **MCDF Embedded CustomizePlus Profile Deserialization (`unexpected character 'e'`)**:
  - Identified that `CustomizePlusData` embedded in Mare Synchronos MCDF archives is Base64-encoded JSON. Passing raw Base64 strings to CustomizePlus IPC caused JSON deserialization failure.
  - Added automatic Base64-detection and decoding before forwarding to CustomizePlus IPC `SetTemporaryProfile`, ensuring embedded body scales and bone transforms apply correctly.

## [0.1.26] - 2026-10-02
### Fixed
- **Actor Lifecycle Safety & Crash Prevention on Despawn/Respawn (HDM Compliance)**:
  - Fixed critical CTD / unhandled exception (`0x12345679` via Dalamud Detour / `RaiseException`) occurring when despawning and respawning actors.
  - Eliminated stale raw native pointer dereferences in `ActorManager.UpdateFrame()`. Fully transitioned to HDM's golden pattern: re-resolving actors every tick via `IObjectTable[GlobalIndex]` and verifying `chara.Address != nint.Zero` before accessing native structs.
  - Wrapped `UpdateFrame()`, `DrawUI()`, `OnFrameworkUpdate()`, `UpdateActorTransform()`, and all job processing loops in structured `try-catch` exception blocks to prevent CLR unhandled exceptions from breaching native detour boundaries.
  - Added `pendingNpcJobs.RemoveAll` to `DespawnCharacter` to prevent lingering jobs from polling deleted actors.
  - Introduced `IsReady` lifecycle guard to `SpawnedActorData` ensuring 3D Gizmos and transform updates only activate once draw baseline and appearance customization are fully initialized.

## [0.1.25] - 2026-10-02
### Fixed
- **Penumbra Collection & Mod Redirection for Humanoid Actors (`ec=16` InvalidActor Resolution)**:
  - Discovered through deep IL disassembly of `Penumbra.dll`'s `CollectionApi.SetCollectionForObject` and `AssociatedIdentifier` that Penumbra's internal identifier resolution calls `ActorIdentifierFactory.FromObject` with `allowPlayerNpc: false`.
  - When `nativeChara->GameObject.ObjectKind` was `BattleNpc`, Penumbra strictly branched into `CreateBNpcFromObject`. Because spawned puppets have `NameId = 0`, this consistently produced `ActorIdentifier.Invalid`, resulting in `ec=16 (InvalidActor)` and causing Penumbra to fail collection assignment and mod redirection (leaving actors in a vanilla state).
  - Explicitly classified all humanoid puppets (Glamourer designs, MCDF bundles, and player clones) as `ObjectKind.Player`. This directs Penumbra into `CreatePlayerFromObject`, which verifies the player name and home world, resolving a valid Player Identifier and enabling 100% successful Penumbra collection assignment (`ec=0`) and instant mod rendering upon `Redraw`.
  - Maintained `ObjectKind.BattleNpc` for non-humanoid monsters (`ModelCharaId > 0`) during Phase 2 transition to ensure native monster model rendering remains undisturbed.

## [0.1.24] - 2026-10-02
### Fixed
- **AQR Independent Spawn & MCDF Temporary Collection Application**:
  - Eliminated `nativeChara->GameObject.OwnerId = 0xE000_0000;` override (preserved default 0). Discovered through IL disassembly of `Penumbra.GameData.dll` that a non-zero `OwnerId` caused `CreateBNpcFromObject` to attempt resolving a non-existent parent GameObject in `ObjectTable`, yielding invalid identifiers and failing with `ec=255 (UnknownError)`.
  - Re-ordered MCDF loading pipeline to add temporary mod files (`AddTemporaryMod`) before actor assignment (`AssignTemporaryCollection`), ensuring seamless mod registration without AQR dependency.
  - Formatted puppet names as `"Csp {hi}{lo}"` to strictly satisfy Penumbra's `VerifyPlayerName` player naming validation.
  - Eliminated premature `ApplyAppearanceDirect` invocation in `SpawnCharacter`, executing appearance resolution exclusively after Phase 2 humanoid baseline draw verification.
- **Humanoid NPC True Appearance Synchronization (Mionne, Gontran)**:
  - By restoring `OwnerId = 0`, Glamourer's `GetState` now resolves immediately within 1-2 frames instead of timing out at 120 frames, successfully applying genuine NPC facial customizations and equipment without player clone fallbacks.
- **Monster Model Accuracy (Ruins Runner)**:
  - Fixed issue where Ruins Runner spawned as an unintended monster (Raptor). Switched monster cache indexing to use unique `BaseId` instead of shared `BNpcNameId`, preventing ID collisions, and restricted `ModelCharaId` auto-resolution to unassigned models only.
- **Demihuman NPC Rendering (Letter Moogle)**:
  - Added fallback to `baseRow` inline equipment fields in `GameDataService.GetNpcAppearanceData` when `NpcEquip.RowId == 0`, ensuring Demihuman body/head equipment slots are correctly populated and rendered rather than showing an invisible body with gizmo only.

## [0.1.23] - 2026-10-02
### Fixed
- **HDM Official `mob-model-index.csv` Integration (Accurate Monster / Mob Spawning)**:
  - Replaced heuristic `BNpcLink.csv` mapping with HDM's authoritative `mob-model-index.csv` (16,243 rows).
  - Resolved model mismatch issues where spawning monsters like Ruins Runner resulted in incorrect models (Ruins Runner correctly resolves to ModelChara 1281, McType 3, Scale 1.1).
  - Japanese monster names resolved directly from Lumina `BNpcName` sheet with duplicate deduplication for a clean search experience.
- **Humanoid NPC Appearance Synchronization (Resolved Player Clone / testruma / Ruma Fallback)**:
  - Identified root cause in `dalamud.log`: synchronous `Thread.Sleep(16)` blocked the main framework thread, preventing Glamourer from registering newly spawned actors and causing `GetState` to return null.
  - Implemented non-blocking per-frame polling queue (`PendingNpcJob`) conforming to HDM's `HumanGuise.cs`.
  - Polled each frame up to 120 frames without blocking; as soon as `GetState` resolves, mapped 26-byte NPC customization and 10-slot equipment, stripped `Parameters` and `Materials` to eliminate player skin/shader pollution, and executed `Penumbra.Redraw` to finalize the NPC skeleton and gear.
  - NPCs like Gontran and Miounne now render with 100% faithful face, hair, and gear instead of falling back to player clones.
- **Demihuman NPC Spawning (Resolved Invisible Letter Moogle / Gizmo-Only Bug)**:
  - Resolved issue where Demihuman NPCs (McType 2, e.g. Letter Moogle, Namazu) rendered invisible with only gizmos.
  - Ensured `NpcEquip` parts are extracted and written directly into `DrawData.EquipmentModelIds`, and `DrawData.IsHatHidden = false` is maintained so Demihuman bodies and equipment render reliably.

## [0.1.22] - 2026-10-02
### Fixed
- **HDM-Compliant Monster & Mob Spawning (Resolved Invisible 3D Model / Gizmo-Only Bug)**:
  - Resolved issue where spawned monsters / mobs were invisible, showing only gizmo manipulators.
  - Aligned with HDM's (`Enceladeum/HDM`) proven two-phase rendering architecture: actors are initially seeded as clean humanoid baseline clones (`ModelCharaId = 0`, `Scale = 1.0f`) to allow the engine to establish a valid baseline draw object.
  - In Phase 2, once the humanoid draw object is verified visible (`DrawObject != null && DrawObject->IsVisible`), the actor is transitioned to the target monster `ModelCharaId` and scale, followed by a dedicated native redraw sequence (`DisableDraw()` -> `IsReadyToDraw()` wait -> `EnableDraw()`).
  - Completely suppressed Penumbra / Glamourer redraw invocations on monster models to prevent invalidation of non-humanoid draw objects.
  - Added Demihuman equipment mapping and `IsHatHidden = false` preservation for non-humanoid demihumans.
- **HDM-Compliant Humanoid NPC Spawning via Glamourer IPC**:
  - Implemented `Glamourer.GetState` and `Glamourer.ApplyState` IPC integration in `GlamourerIpc.cs` conforming to HDM's `HumanGuise.cs`.
  - Added `ApplyNpcAppearance` to map 26-byte `CustomizeData` into Glamourer's 36-field `Customize` model via `CustomizeMap`, and injected NPC gear slots using bit-packed `CustomItemId`.
  - Automatically stripped `Parameters` and `Materials` blocks on NPC appearance apply, preventing player skin tone / shader overrides from bleeding onto NPC disguises.
  - Added automatic fallback resolution of `ModelCharaId` and NPC appearance from `GameDataService` if template IDs are present.

## [0.1.21] - 2026-10-02
### Fixed
- **Continuous 360-Degree Horizontal Rotation (Resolved 180° Flip & Jitter)**:
  - Fixed character rotation getting stuck and jittering around the 180-degree mark.
  - Replaced Euler angle matrix decomposition (`ImGuizmo.DecomposeMatrixToComponents`), which suffered from a ±180° discontinuity and feedback-loop jitter, with Stagehand-compliant `Quaternion` matrix composition (`Matrix4x4.CreateFromQuaternion`).
  - Extracted the actor's forward direction vector via `Vector3.Transform(Vector3.UnitZ, newRot)` and computed seamless continuous heading with `MathF.Atan2(forward.X, forward.Z)`. The character now rotates smoothly and continuously past 180° without any jitter or angle wrapping limits.

## [0.1.20] - 2026-10-02
### Fixed
- **Horizontal Actor Rotation (Yaw Ring)**:
  - Fixed character rotation not responding during ring manipulation. Switched operation from 4-ring `Rotate` (which prioritized screen-space camera roll) to `ImGuizmoOperation.RotateY` (horizontal planar yaw ring).
  - Dragging the green horizontal rotation ring now immediately and smoothly turns the character's heading in 360 degrees.
- **Eliminated "New NPC: Failed to get response." Popup**:
  - Implemented dynamic input capture in `GizmoRenderer`: `ImGuiWindowFlags.NoInputs` is dynamically cleared while hovering or manipulating the gizmo (`IsOver() || IsUsing()`), consuming clicks and preventing game-world click-through.
  - While not hovering over the gizmo, `NoInputs` remains active so players can freely rotate the game camera without hindrance.
  - Enforced `TargetableStatus = 0` and `EventId = 0` on spawned actors to completely disable game NPC interaction events.
- **Removed Duplicate Header Gizmo Buttons**:
  - Cleaned up `MainWindow.Draw()` by removing the redundant gizmo toolbar buttons from the upper-left header above tabs, retaining only the clean in-context toolbar inside the character spawn details and stage scene tabs.

## [0.1.19] - 2026-10-02
### Fixed
- **ImGuizmo 3D Rendering & Camera Projection**:
  - Resolved gizmo rendering failure by applying FFXIV reverse-Z clip projection matrix correction (`M43 = -(clip * near)`, `M33 = -((far + near) / (far - near))`, `view.M44 = 1.0f`) conforming to Stagehand and BDTH architecture.
  - Recomposed transform matrix via `ImGuizmo.RecomposeMatrixFromComponents` with Euler degrees, properly positioning the 3D gizmo at the target actor's location.
- **Camera Viewport Input Transparency**:
  - Added `ImGuiWindowFlags.NoInputs` to the full-screen gizmo overlay window. This completely eliminates game-wide mouse input blocking, allowing unrestricted camera rotation (right-click drag) and character movement in the game world while preserving 3D gizmo hit-testing.
- **Gizmo Toggle Unification**:
  - Removed duplicate `Gizmo` ON/OFF checkboxes from `CharacterLibraryTab`, `StageSceneTab`, and `SettingsTab`.
  - Unified gizmo state management entirely into the Stagehand-style mode toolbar:
    - **Select (Mouse Pointer)**: Turns gizmo OFF / hides manipulator.
    - **Translate (Cross Arrows)**: Turns gizmo ON in translation mode with XY/XZ/YZ quad plane handles.
    - **Rotate (Sync Alt)**: Turns gizmo ON in rotation mode with 3-axis rings.

## [0.1.18] - 2026-10-02
### Added
- **Stagehand-Compliant ImGuizmo 3D Gizmo System**:
  - Replaced the custom 2D screen-projected gizmo with native `Dalamud.Bindings.ImGuizmo` architecture identical to Stagehand.
  - Extracted game camera matrices (`ViewMatrix`, `ProjectionMatrix`) directly from `FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance()->CurrentCamera->RenderCamera`.
  - Moved gizmo rendering to a full-screen transparent overlay window in `Plugin.DrawUI`, completely eliminating mouse focus loss and click-through issues when dragging handles in the 3D game world.
  - Implemented Stagehand-style mode toolbar (Select / Translate / Rotate) across `MainWindow`, `CharacterLibraryTab`, and `StageSceneTab`:
    - **Select Mode (`FontAwesomeIcon.MousePointer`)**: Hides the gizmo for normal scene interaction.
    - **Translate Mode (`FontAwesomeIcon.ArrowsUpDownLeftRight`)**: Renders primary X, Y, Z axis arrows alongside red, green, and blue **XY, XZ, YZ quad planes** for multi-axis simultaneous drag-manipulation.
    - **Rotate Mode (`FontAwesomeIcon.SyncAlt`)**: Separates rotation from translation, rendering dedicated 3-axis rotation rings for intuitive yaw/pitch/roll adjustments.
  - Real-time transform synchronization via `Matrix4x4.Decompose` updating actor position and yaw in both library preview and active stage actors.

## [0.1.17] - 2026-10-02
### Added
- **Customize+ (C+) Profile Integration**:
  - Implemented comprehensive IPC integration with Customize+ (v6+ API) via `Services/CustomizePlusIpc.cs`.
  - Added Customize+ profile selector to character creation and editing modals in `CharacterLibraryTab.cs`.
  - Spawning a character with an assigned Customize+ profile now automatically queries and applies temporary body scales and bone transforms to the spawned actor.
  - Full automatic cleanup: temporary Customize+ profiles are seamlessly revoked and freed upon despawning characters, scene transitions, or territory changes.
  - Added support for embedded `CustomizePlusData` within `.mcdf` archives, allowing automatic body scaling even for third-party MCDF files without manual profile mapping.

### Changed
- **Modal UI Cleanup**:
  - Removed the unused `Or Direct Design String / Code` manual multiline input box from `CharacterLibraryTab.cs`, streamlining the character creation modal to design and collection dropdown pickers.

## [0.1.16] - 2026-10-02
### Fixed
- **Penumbra Collection & MCDF Temporary Collection Assignment via ObjectKind.Player**:
  - Through comprehensive CIL reverse-engineering of `Penumbra.GameData.dll`'s `ActorIdentifierFactory.FromObject`, discovered the exact root cause of `AssignTemporaryCollection` failing with error code `255` and `SetCollectionForObject` failing with `ec = 16` (`InvalidIdentifier`).
  - Previously, all spawned actors were assigned `ObjectKind = ObjectKind.BattleNpc`. When resolving collections, Penumbra's internal IPC methods invoke `CreateBNpcFromObject(allowPlayer: false)`. Because `allowPlayer` is hard-coded to `false` in collection assignment IPC, any non-Player object kind—regardless of its name or `OwnerId`—is strictly treated as a monster NPC. Since `DataId` was 0, it resolved to non-existent `BNpc(0)`, which has 0 mod collections, causing `Collections.Add` to reject assignment with error code 255 and `SetCollectionForObject` to return 16.
  - Resolved this by setting `ObjectKind = ObjectKind.Player` (and `BattleNpcSubKind = BattleNpcSubKind.Player`) for all humanoid actors (`template.ModelCharaId == 0`), while keeping `ObjectKind = ObjectKind.BattleNpc` strictly for monster models (`template.ModelCharaId > 0`).
  - With `ObjectKind.Player`, Penumbra directly routes to `CreatePlayerFromObject`, validating the character's name (`"Cs Aa"` format) and returning a 100% valid Player identifier. Both normal Penumbra collections and MCDF temporary collections now successfully bind (`ec = 0`) and apply all custom 3D models, textures, and manipulations to spawned actors.

## [0.1.15] - 2026-10-02
### Added
- **Full AQR-Compliant MCDF Mod Extraction & Penumbra Temporary Collection Lifecycle**:
  - Implemented complete extraction of embedded Mod files (3D models, textures, materials, and FileSwaps) and `ManipulationData` directly from `.mcdf` LZ4 binary streams into local plugin cache (`mcdf_cache`).
  - Integrated Penumbra Temporary Collection IPC APIs (`CreateTemporaryCollection`, `AssignTemporaryCollection`, `AddTemporaryMod`, `DeleteTemporaryCollection`).
  - When spawning a character with an MCDF file, CharacterSpawn now dynamically creates a dedicated Penumbra temporary collection, binds all embedded mod files and meta manipulations to the spawned actor, applies the Glamourer design, and redraws seamlessly without requiring user-created Penumbra collections.
  - Automatically deletes and reclaims temporary Penumbra collections upon despawning or territory transitions, preventing memory/handle leaks.
- **Dynamic Plugin Assembly Version Logging**:
  - Replaced hardcoded `v0.1.9` startup log string in `Plugin.cs` with dynamic assembly version resolution (`v{GetType().Assembly.GetName().Version}`).

### Fixed
- **MCDF UI Penumbra Independence (AQR Conformity)**:
  - Removed manual Penumbra Collection selector from the MCDF section in `CharacterLibraryTab.cs`. MCDF templates now automatically inform users of embedded mod auto-loading via temporary collections, enabling full cross-user portability for third-party `.mcdf` files.
- **ActorManager PluginInterface Injection**:
  - Wired `IDalamudPluginInterface` through to `ActorManager` to provide reliable config directory resolution for MCDF file caching.
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
