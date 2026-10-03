using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using CharacterCopyFlags = FFXIVClientStructs.FFXIV.Client.Game.Character.CharacterSetupContainer.CopyFlags;
using ClientObjectManager = FFXIVClientStructs.FFXIV.Client.Game.Object.ClientObjectManager;
using CharacterSpawn.Models;
using CharacterSpawn.Services;

namespace CharacterSpawn.Managers;

public unsafe class ActorManager : IDisposable
{
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly LogManager? logManager;
    private readonly TimelineManager timelineManager;
    private readonly HeadTrackingManager headTrackingManager;
    private readonly GlamourerIpc glamourerIpc;
    private readonly PenumbraIpc penumbraIpc;
    private readonly McdfParser? mcdfParser;
    private readonly IDalamudPluginInterface? pluginInterface;
    private readonly CustomizePlusIpc? customizePlusIpc;
    private readonly GameDataService? gameDataService;
    private readonly IFramework? framework;

    private readonly List<SpawnedActorData> activeActors = new();
    private readonly List<ushort> createdIndexes = new();

    // HDM GuiseService.cs 準拠: モンスター/再描画待機キュー (Phase: WaitEnable)
    private sealed class MonsterRedrawJob
    {
        public SpawnedActorData Spawned = null!;
        public ushort GlobalIndex;
        public int Ticks;
    }
    private readonly List<MonsterRedrawJob> monsterRedrawJobs = new();

    private const int MaxReadyTicks = 200;


    // 単一プレビューアクター（Characterタブ専用）
    public SpawnedActorData? CurrentPreviewActor { get; private set; }

    public IReadOnlyList<SpawnedActorData> ActiveActors => activeActors;

    public ActorManager(
        IClientState clientState,
        IObjectTable objectTable,
        ISigScanner? sigScanner,
        IPluginLog log,
        TimelineManager timelineManager,
        HeadTrackingManager headTrackingManager,
        GlamourerIpc glamourerIpc,
        PenumbraIpc penumbraIpc,
        LogManager? logManager = null,
        McdfParser? mcdfParser = null,
        IDalamudPluginInterface? pluginInterface = null,
        CustomizePlusIpc? customizePlusIpc = null,
        GameDataService? gameDataService = null,
        IFramework? framework = null)
    {
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.log = log;
        this.logManager = logManager;
        this.timelineManager = timelineManager;
        this.headTrackingManager = headTrackingManager;
        this.glamourerIpc = glamourerIpc;
        this.penumbraIpc = penumbraIpc;
        this.mcdfParser = mcdfParser;
        this.pluginInterface = pluginInterface;
        this.customizePlusIpc = customizePlusIpc;
        this.gameDataService = gameDataService;
        this.framework = framework;

        this.clientState.TerritoryChanged += OnTerritoryChanged;
    }

    private void OnTerritoryChanged(uint territoryType)
    {
        logManager?.Info("Territory changed. Clearing spawned actors tracking.");
        foreach (var actor in activeActors)
        {
            if (actor.TemporaryCollectionGuid.HasValue)
            {
                penumbraIpc.DeleteTemporaryCollection(actor.TemporaryCollectionGuid.Value);
            }
            if (actor.TemporaryCustomizePlusGuid.HasValue && customizePlusIpc != null)
            {
                customizePlusIpc.DeleteTemporaryProfile(actor.TemporaryCustomizePlusGuid.Value);
            }
        }
        monsterRedrawJobs.Clear();
        activeActors.Clear();
        createdIndexes.Clear();
        CurrentPreviewActor = null;
    }

    private string GetPuppetName(CharacterTemplate template)
    {
        // AQR & Brio 黄金パターン:
        // テンプレートの名前（先頭単語）から英字を抽出し " Cnpc" を付加（例: "Kimo Cnpc"）
        // FF14 の PlayerIdentifier / VerifyPlayerName 規則 (Forename + " " + Surname, 各15文字以内, 合計20文字以内) を完全充足
        string baseName = "Actor";
        if (!string.IsNullOrWhiteSpace(template.Name))
        {
            var letters = new string(template.Name.TakeWhile(c => char.IsLetter(c)).ToArray());
            if (letters.Length >= 2)
            {
                baseName = char.ToUpper(letters[0]) + letters.Substring(1).ToLower();
            }
        }

        if (baseName.Length > 14) baseName = baseName.Substring(0, 14);
        string candidate = $"{baseName} Cnpc";
        if (candidate.Length <= 20) return candidate;

        return "Cutscene Player";
    }

    /// <summary>
    /// Characterタブ専用のプレビュー用スポーン
    /// 既存のプレビューアクターがあれば自動的にデスポーンしてから新しく生成する
    /// </summary>
    public SpawnedActorData? SpawnPreviewCharacter(CharacterTemplate template)
    {
        DespawnPreviewCharacter();

        var spawned = SpawnCharacter(template);
        if (spawned != null)
        {
            CurrentPreviewActor = spawned;
            logManager?.Info($"Set CurrentPreviewActor to '{spawned.DisplayName}' (Instance: {spawned.InstanceId}).");
        }
        return spawned;
    }

    /// <summary>
    /// Characterタブ専用のプレビューアクターを破棄
    /// </summary>
    public void DespawnPreviewCharacter()
    {
        if (CurrentPreviewActor != null)
        {
            logManager?.Info($"Despawning current preview character: {CurrentPreviewActor.DisplayName}");
            DespawnCharacter(CurrentPreviewActor);
            CurrentPreviewActor = null;
        }
    }

    /// <summary>
    /// テンプレートをもとに新しいキャラクターをスポーンする (HDM & AQR アーキテクチャ準拠)
    /// </summary>
    public SpawnedActorData? SpawnCharacter(CharacterTemplate template, Vector3? spawnPosition = null, float? spawnRotation = null)
    {
        try
        {
            var localPlayer = objectTable.Length > 0 ? objectTable[0] as ICharacter : null;
            if (localPlayer == null || localPlayer.Address == 0)
            {
                logManager?.Error("Spawn failed: Local player not found. Cannot seed character clone.");
                return null;
            }

            var com = ClientObjectManager.Instance();
            if (com == null)
            {
                logManager?.Error("Spawn failed: ClientObjectManager instance is null.");
                return null;
            }

            // COMの空きスロットを作成（自動割り当て）
            uint comId = com->CreateBattleCharacter();
            if (comId == 0xFFFFFFFF)
            {
                logManager?.Error("ClientObjectManager.CreateBattleCharacter returned 0xFFFFFFFF (slot limit reached).");
                return null;
            }

            ushort comIdx = (ushort)comId;
            var newObject = com->GetObjectByIndex(comIdx);
            if (newObject == null)
            {
                logManager?.Error($"Failed to retrieve spawned GameObject at COM index {comIdx}. Reclaiming slot.");
                com->DeleteObjectByIndex(comIdx, 0);
                return null;
            }

            var nativeChara = (Character*)newObject;
            var meNative = (Character*)localPlayer.Address;
            var pos = spawnPosition ?? GetDefaultSpawnPosition();
            var rot = spawnRotation ?? localPlayer.Rotation;

            // モンスターモデルIDの補完 (未設定の場合のみ補完)
            if (template.SourceType == CharacterSourceType.Monster && template.ModelCharaId == 0 && template.DataId > 0 && gameDataService != null)
            {
                var resolvedId = gameDataService.GetMonsterModelCharaId(template.DataId);
                if (resolvedId > 0)
                {
                    logManager?.Info($"Resolved Monster ModelCharaId for '{template.Name}' to {resolvedId} (DataId: {template.DataId}).");
                    template.ModelCharaId = resolvedId;
                }
            }

            // NPCデータの補完 (CustomizeData または NpcEquipmentModelIds の補完)
            if (template.SourceType == CharacterSourceType.Npc && gameDataService != null)
            {
                if (template.CustomizeData == null || template.NpcEquipmentModelIds == null)
                {
                    uint npcId = template.DataId;
                    if (npcId == 0 && !string.IsNullOrWhiteSpace(template.Name))
                    {
                        var searchRes = gameDataService.SearchNpcs(template.Name, 1);
                        if (searchRes.Count > 0)
                        {
                            npcId = searchRes[0].Id;
                            template.DataId = npcId;
                            logManager?.Info($"Resolved ENpcId {npcId} by name '{template.Name}'.");
                        }
                    }

                    if (npcId > 0)
                    {
                        var app = gameDataService.GetNpcAppearanceData(npcId);
                        if (app != null)
                        {
                            template.ModelCharaId = app.ModelCharaId;
                            template.CustomizeData = app.CustomizeData;
                            template.NpcEquipmentModelIds = app.EquipmentModelIds;
                            template.McType = app.McType;
                            logManager?.Info($"Auto-resolved NPC appearance data for '{template.Name}' from ENpcId {npcId} (ModelChara: {template.ModelCharaId}, McType: {template.McType}).");
                        }
                    }
                }
            }

            logManager?.Info($"Spawning '{template.Name}' (Source: {template.SourceType}, ModelChara: {template.ModelCharaId}, Weapon: {template.WeaponVisible}) at COM#{comIdx}...");

            // 1. 自キャラからベースラインをコピーして drawable 骨格を確立 (AQR / Brio 準拠)
            nativeChara->CharacterSetup.CopyFromCharacter(meNative, CharacterCopyFlags.WeaponHiding);
            nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);

            // 2. ベースラインのリセット
            nativeChara->ModelContainer.ModelCharaId = 0;
            nativeChara->GameObject.Scale = 1.0f;
            nativeChara->DrawData.HideWeapons(!template.WeaponVisible);
            nativeChara->DrawData.IsWeaponHidden = !template.WeaponVisible;

            // AQR 黄金律:
            // ObjectKind, BattleNpcSubKind, OwnerId, NameId, HomeWorld の改変は一切行わない（素の BattleCharacter を維持）！
            nativeChara->GameObject.TargetableStatus = 0;
            nativeChara->GameObject.EventId = 0;

            string puppetName = GetPuppetName(template);
            nativeChara->GameObject.SetName(puppetName);

            // 位置・回転・透明度の設定
            nativeChara->GameObject.SetPosition(pos.X, pos.Y, pos.Z);
            nativeChara->GameObject.SetRotation(rot);
            nativeChara->GameObject.DefaultPosition = pos;
            nativeChara->GameObject.DefaultRotation = rot;
            nativeChara->Alpha = 1.0f;

            // 描画開始 (Brio / AQR 黄金律: EnableDraw)
            nativeChara->GameObject.EnableDraw();

            // グローバルインデックスの解決 (Two Index Spaces Trap 対策)
            var objRef = objectTable.CreateObjectReference((nint)nativeChara) as ICharacter;
            if (objRef == null)
            {
                logManager?.Error($"COM#{comIdx} created but no ICharacter reference resolved. Reclaiming slot.");
                com->DeleteObjectByIndex(comIdx, 0);
                return null;
            }

            ushort globalIdx = (ushort)objRef.ObjectIndex;
            if (globalIdx == localPlayer.ObjectIndex)
            {
                logManager?.Error($"Spawn: Puppet resolved to local player's global index ({globalIdx})! Reclaiming COM#{comIdx}.");
                com->DeleteObjectByIndex(comIdx, 0);
                return null;
            }

            var spawned = new SpawnedActorData
            {
                TemplateId = template.Id,
                DisplayName = string.IsNullOrWhiteSpace(template.Name) ? puppetName : template.Name,
                PuppetName = puppetName,
                NativeAddress = (nint)nativeChara,
                GlobalIndex = globalIdx,
                ComIndex = comIdx,
                GameObjectId = nativeChara->EntityId,
                Transform = new TransformData
                {
                    Position = pos,
                    Rotation = rot,
                    Scale = 1.0f
                },
                NamePlate = new NamePlateSettings
                {
                    Show = true,
                    CustomName = template.Name
                },
                IsTargetable = false
            };

            // =========================================================================
            // パイプライン D: Monster / MOB (ModelCharaId > 0) [HDM GuiseService 準拠]
            // =========================================================================
            if (template.ModelCharaId > 0)
            {
                nativeChara->GameObject.DisableDraw();
                nativeChara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;
                nativeChara->GameObject.Scale = template.Scale > 0 ? template.Scale : 1.0f;
                nativeChara->DrawData.HideWeapons(true);
                nativeChara->DrawData.IsWeaponHidden = true;

                if (template.NpcEquipmentModelIds != null && template.NpcEquipmentModelIds.Length > 0)
                {
                    var equipSpan = nativeChara->DrawData.EquipmentModelIds;
                    for (int idx = 0; idx < template.NpcEquipmentModelIds.Length && idx < equipSpan.Length; idx++)
                    {
                        equipSpan[idx] = new EquipmentModelId { Value = template.NpcEquipmentModelIds[idx] };
                    }
                    nativeChara->DrawData.IsHatHidden = false;
                }

                nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);

                monsterRedrawJobs.Add(new MonsterRedrawJob
                {
                    Spawned = spawned,
                    GlobalIndex = globalIdx,
                    Ticks = 0
                });

                activeActors.Add(spawned);
                createdIndexes.Add(globalIdx);
                logManager?.Info($"[Pipeline D: Monster] Spawned '{spawned.DisplayName}' on Global#{globalIdx}. Enqueued to MonsterRedrawJob.");
                return spawned;
            }

            // =========================================================================
            // パイプライン C: NPC (人型 ENpc: SourceType == Npc かつ ModelCharaId == 0) [HDM HumanGuise 準拠]
            // =========================================================================
            if (template.SourceType == CharacterSourceType.Npc)
            {
                ApplyNpcAppearance(nativeChara, globalIdx, template, spawned);
                nativeChara->GameObject.EnableDraw();
                spawned.IsReady = true;

                activeActors.Add(spawned);
                createdIndexes.Add(globalIdx);
                logManager?.Info($"[Pipeline C: NPC] Spawned '{spawned.DisplayName}' on Global#{globalIdx}. Appearance finalized immediately.");
                return spawned;
            }

            // =========================================================================
            // パイプライン A & B: AQR系統 (PlayerClone / Glamourer / MCDF) [AQR 準拠]
            // =========================================================================
            // スポーン完了直後に同一フレーム・同一コンテキストで即時直列実行！
            ApplyAppearanceDirect(nativeChara, globalIdx, template, spawned);
            nativeChara->GameObject.EnableDraw();
            spawned.IsReady = true;

            activeActors.Add(spawned);
            createdIndexes.Add(globalIdx);
            logManager?.Info($"[Pipeline A/B: AQR] Spawned '{spawned.DisplayName}' on Global#{globalIdx}. Penumbra & Glamourer finalized immediately.");
            return spawned;
        }
        catch (Exception ex)
        {
            logManager?.Error($"Exception during SpawnCharacter: {ex}");
            return null;
        }
    }

    /// <summary>
    /// スポーン中アクターの位置・回転を更新
    /// </summary>
    public void UpdateActorTransform(SpawnedActorData actor, Vector3 newPosition, float newRotation)
    {
        actor.Transform.Position = newPosition;
        actor.Transform.Rotation = newRotation;

        if (actor.NativeAddress == 0 || !actor.IsReady) return;

        try
        {
            if (actor.GlobalIndex < objectTable.Length)
            {
                var obj = objectTable[actor.GlobalIndex];
                if (obj is ICharacter charaObj && charaObj.Address != nint.Zero)
                {
                    var chara = (Character*)charaObj.Address;
                    chara->GameObject.SetPosition(newPosition.X, newPosition.Y, newPosition.Z);
                    chara->GameObject.SetRotation(newRotation);
                    chara->GameObject.DefaultPosition = newPosition;
                    chara->GameObject.DefaultRotation = newRotation;
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Warning($"UpdateActorTransform failed: {ex.Message}");
        }
    }

    /// <summary>
    /// アニメーション・表情の適用
    /// </summary>
    public void ApplyActorAnimation(SpawnedActorData actor)
    {
        if (actor.NativeAddress == 0) return;
        try
        {
            if (actor.GlobalIndex < objectTable.Length)
            {
                var obj = objectTable[actor.GlobalIndex];
                if (obj is ICharacter charaObj && charaObj.Address != nint.Zero)
                {
                    var chara = (Character*)charaObj.Address;
                    timelineManager.ApplyTimeline(chara, actor.Animation);
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Warning($"ApplyActorAnimation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// ターゲット可否設定を適用
    /// </summary>
    public void ApplyTargetable(SpawnedActorData actor)
    {
        if (actor.NativeAddress == 0) return;
        try
        {
            if (actor.GlobalIndex < objectTable.Length)
            {
                var obj = objectTable[actor.GlobalIndex];
                if (obj is ICharacter charaObj && charaObj.Address != nint.Zero)
                {
                    var go = (GameObject*)charaObj.Address;
                    if (actor.IsTargetable)
                        go->TargetableStatus |= ObjectTargetableFlags.IsTargetable;
                    else
                        go->TargetableStatus &= ~ObjectTargetableFlags.IsTargetable;
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Warning($"ApplyTargetable failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定アクターをデスポーン（破棄）: HDM & Brio DestroyObject パターン
    /// </summary>
    public void DespawnCharacter(SpawnedActorData actor)
    {
        try
        {
            actor.IsReady = false;
            monsterRedrawJobs.RemoveAll(j => j.Spawned == actor || j.GlobalIndex == actor.GlobalIndex);

            // Penumbra 一時コレクションのクリーンアップ (AQR / Mare 準拠)
            if (actor.TemporaryCollectionGuid.HasValue)
            {
                penumbraIpc.DeleteTemporaryCollection(actor.TemporaryCollectionGuid.Value);
                actor.TemporaryCollectionGuid = null;
            }

            // Penumbra コレクション割り当ての完全解除 (通常 & 一時)
            if (penumbraIpc.IsAvailable)
            {
                penumbraIpc.UnassignCollectionForActor(actor.GlobalIndex);
            }

            // Glamourer ロック解除 (削除するアクターに対して RevertState は非同期再描画を誘発して武器残存の原因になるため呼ばない)
            if (glamourerIpc != null && glamourerIpc.IsAvailable)
            {
                glamourerIpc.UnlockState(actor.GlobalIndex, actor.PuppetName);
                if (!string.IsNullOrEmpty(actor.DisplayName) && actor.DisplayName != actor.PuppetName)
                {
                    glamourerIpc.UnlockState(actor.GlobalIndex, actor.DisplayName);
                }
            }

            // CustomizePlus プロファイル紐付けおよび一時プロファイルの完全クリーンアップ (AQR / Caraxi 準拠)
            if (customizePlusIpc != null && customizePlusIpc.IsAvailable)
            {
                if (actor.AssignedCustomizePlusGuid.HasValue && !string.IsNullOrEmpty(actor.PuppetName))
                {
                    ushort worldId = 0;
                    if (actor.NativeAddress != 0)
                    {
                        var c = (Character*)actor.NativeAddress;
                        worldId = (ushort)c->HomeWorld;
                    }
                    customizePlusIpc.RemovePlayerCharacter(actor.AssignedCustomizePlusGuid.Value, actor.PuppetName, worldId);
                    actor.AssignedCustomizePlusGuid = null;
                }

                if (actor.TemporaryCustomizePlusGuid.HasValue)
                {
                    customizePlusIpc.DeleteTemporaryProfile(actor.TemporaryCustomizePlusGuid.Value);
                    actor.TemporaryCustomizePlusGuid = null;
                }
            }

            if (actor.NativeAddress != 0)
            {
                var chara = (Character*)actor.NativeAddress;
                try
                {
                    // 武器のグラフィックフラグを非表示にし、描画パイプラインから完全アンロード (孤立武器残留防止)
                    chara->DrawData.HideWeapons(true);
                    chara->DrawData.IsWeaponHidden = true;
                    chara->GameObject.DisableDraw();
                }
                catch { }

                var com = ClientObjectManager.Instance();
                if (com != null)
                {
                    // ライブオブジェクトから最新の COM インデックスを解決して正規破棄
                    var comIdx = com->GetIndexByObject((GameObject*)actor.NativeAddress);
                    if (comIdx != 0xFFFFFFFF)
                    {
                        com->DeleteObjectByIndex((ushort)comIdx, 0);
                        logManager?.Info($"Despawned actor '{actor.DisplayName}' (COM#{comIdx}, Global#{actor.GlobalIndex}).");
                    }
                    else
                    {
                        logManager?.Warning($"Despawn: actor '{actor.DisplayName}' not COM-managed (GetIndexByObject returned 0xFFFFFFFF).");
                    }
                }
                actor.NativeAddress = 0;
            }
        }
        catch (Exception ex)
        {
            logManager?.Error($"Failed to despawn actor {actor.DisplayName}: {ex.Message}");
        }
        finally
        {
            createdIndexes.Remove(actor.GlobalIndex);
            activeActors.Remove(actor);
            if (CurrentPreviewActor == actor)
            {
                CurrentPreviewActor = null;
            }
        }
    }

    /// <summary>
    /// 全アクターを一括デスポーン
    /// </summary>
    public void DespawnAll()
    {
        monsterRedrawJobs.Clear();
        var list = activeActors.ToList();
        foreach (var actor in list)
        {
            DespawnCharacter(actor);
        }
        activeActors.Clear();
        createdIndexes.Clear();
        CurrentPreviewActor = null;
        logManager?.Info("Despawned all active actors.");
    }

    /// <summary>
    /// 毎フレームの更新処理: 視線追従 & モンスターRedraw (HDM GuiseService 準拠)
    /// </summary>
    public void UpdateFrame()
    {
        try
        {
            headTrackingManager.UpdateTracking(activeActors);

            // モンスター / 再描画待機キュー (HDM GuiseService RedrawPhase.WaitEnable 方式)
            if (monsterRedrawJobs.Count > 0)
            {
                for (int i = monsterRedrawJobs.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var job = monsterRedrawJobs[i];
                        job.Ticks++;

                        if (!activeActors.Contains(job.Spawned))
                        {
                            monsterRedrawJobs.RemoveAt(i);
                            continue;
                        }

                        // 最低 2 フレーム待機 (DisableDraw がゲームエンジンに反映されるのを待つ)
                        if (job.Ticks < 2) continue;

                        if (job.GlobalIndex >= objectTable.Length)
                        {
                            if (job.Ticks >= MaxReadyTicks) monsterRedrawJobs.RemoveAt(i);
                            continue;
                        }

                        var obj = objectTable[job.GlobalIndex];
                        if (obj is not ICharacter charaObj || charaObj.Address == nint.Zero)
                        {
                            if (job.Ticks >= MaxReadyTicks) monsterRedrawJobs.RemoveAt(i);
                            continue;
                        }

                        job.Spawned.NativeAddress = charaObj.Address;
                        var chara = (Character*)charaObj.Address;

                        bool ready = false;
                        try { ready = chara->GameObject.IsReadyToDraw(); } catch { }
                        if (!ready && job.Ticks < MaxReadyTicks) continue;

                        try { chara->GameObject.EnableDraw(); } catch { }
                        job.Spawned.IsReady = true;
                        monsterRedrawJobs.RemoveAt(i);
                        logManager?.Info($"Monster/Actor redraw complete for '{job.Spawned.DisplayName}' on Global#{job.GlobalIndex} after {job.Ticks} ticks.");
                    }
                    catch (Exception ex)
                    {
                        logManager?.Error($"MonsterRedrawJob exception: {ex}");
                        if (i < monsterRedrawJobs.Count) monsterRedrawJobs.RemoveAt(i);
                    }
                }
            }

            // 全アクティブアクターの描画可視化保証 (Brio DrawWhenReady & AQR 準拠)
            foreach (var actor in activeActors)
            {
                if (actor.NativeAddress == 0) continue;
                if (actor.GlobalIndex >= objectTable.Length) continue;

                var obj = objectTable[actor.GlobalIndex];
                if (obj is not ICharacter charaObj || charaObj.Address == nint.Zero) continue;
                var chara = (Character*)charaObj.Address;

                // モンスターの再描画待機中（DisableDraw中）は干渉しない
                if (monsterRedrawJobs.Any(j => j.Spawned == actor || j.GlobalIndex == actor.GlobalIndex)) continue;

                // 1. DrawObject が存在する場合、非表示フラグ(0x10)があれば解除
                if (chara->GameObject.DrawObject != null)
                {
                    if ((chara->GameObject.DrawObject->Flags & 0x10) != 0)
                    {
                        chara->GameObject.DrawObject->Flags &= unchecked((byte)~0x10);
                        chara->GameObject.EnableDraw();
                    }
                }
                else
                {
                    // DrawObject 未生成なら EnableDraw を試行
                    chara->GameObject.EnableDraw();
                }

                // 2. 描画準備完了状態なら確実に EnableDraw を実行
                if (chara->GameObject.IsReadyToDraw())
                {
                    chara->GameObject.EnableDraw();
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Error($"ActorManager.UpdateFrame outer exception: {ex}");
        }
    }

    private void ApplyNpcAppearanceDirectFallback(Character* chara, CharacterTemplate template)
    {
        if (template.CustomizeData != null && template.CustomizeData.Length >= 26)
        {
            fixed (byte* pCust = template.CustomizeData)
            {
                Buffer.MemoryCopy(pCust, &chara->DrawData.CustomizeData, 26, 26);
            }
        }

        if (template.NpcEquipmentModelIds != null && template.NpcEquipmentModelIds.Length > 0)
        {
            var equipSpan = chara->DrawData.EquipmentModelIds;
            for (int idx = 0; idx < template.NpcEquipmentModelIds.Length && idx < equipSpan.Length; idx++)
            {
                equipSpan[idx] = new EquipmentModelId { Value = template.NpcEquipmentModelIds[idx] };
            }
        }

        chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None);
    }

    /// <summary>
    /// パイプライン C: NPC (人型 ENpc) 外見適用 (HDM HumanGuise 方式)
    /// </summary>
    public void ApplyNpcAppearance(Character* chara, ushort globalIndex, CharacterTemplate template, SpawnedActorData? spawned = null)
    {
        if (chara == null) return;
        int actorIndex = (int)globalIndex;

        // 自キャラ誤爆ガード
        if (globalIndex <= 0 || (objectTable.Length > 0 && objectTable[0]?.Address == (nint)chara))
        {
            logManager?.Warning($"ApplyNpcAppearance: Aborted attempt to apply appearance to LocalPlayer (GlobalIndex: {globalIndex}).");
            return;
        }

        logManager?.Info($"[Pipeline C: NPC] ApplyNpcAppearance: '{template.Name}' on Global#{globalIndex}...");

        bool glamSuccess = false;
        if (glamourerIpc != null && glamourerIpc.IsAvailable)
        {
            var player = objectTable.Length > 0 ? objectTable[0] as ICharacter : null;
            string? localPlayerName = player?.Name.TextValue;
            glamSuccess = glamourerIpc.ApplyNpcAppearance(actorIndex, template.CustomizeData, template.NpcEquipmentModelIds, showHeadgear: true, spawned?.PuppetName, localPlayerName);
            logManager?.Info($"[Pipeline C: NPC] Glamourer ApplyNpcAppearance result on Global#{actorIndex}: {glamSuccess}");
        }

        if (!glamSuccess)
        {
            logManager?.Warning($"[Pipeline C: NPC] Glamourer NPC appearance failed or unavailable. Applying direct memory fallback...");
            ApplyNpcAppearanceDirectFallback(chara, template);
        }

        chara->DrawData.HideWeapons(!template.WeaponVisible);
        chara->DrawData.IsWeaponHidden = !template.WeaponVisible;

        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.Redraw(actorIndex);
        }
        logManager?.Info($"[Pipeline C: NPC] Applied Humanoid NPC appearance to Global#{actorIndex} (Glamourer: {glamSuccess}).");
    }

    /// <summary>
    /// パイプライン A & B: 外見（Glamourer / Penumbra / MCDF / Customize+）の即時直接適用
    /// AQuestReborn (AQR) アーキテクチャ完全準拠
    /// </summary>
    public void ApplyAppearanceDirect(Character* chara, ushort globalIndex, CharacterTemplate template, SpawnedActorData? spawned = null, bool applyGlamourer = true)
    {
        if (chara == null) return;
        int actorIndex = (int)globalIndex;

        // 自キャラ誤爆の物理遮断ガード
        if (globalIndex <= 0 || (objectTable.Length > 0 && objectTable[0]?.Address == (nint)chara))
        {
            logManager?.Warning($"ApplyAppearanceDirect: Aborted attempt to apply appearance to LocalPlayer (GlobalIndex: {globalIndex}).");
            return;
        }

        logManager?.Info($"[Pipeline A/B] ApplyAppearanceDirect: '{template.Name}' (GlobalIndex: {actorIndex}, Source: {template.SourceType})...");

        // 前のステートや割り当てをクリーンアップ
        if (glamourerIpc != null && glamourerIpc.IsAvailable)
        {
            glamourerIpc.UnlockState(actorIndex, spawned?.PuppetName);
            if (!string.IsNullOrEmpty(spawned?.DisplayName) && spawned.DisplayName != spawned.PuppetName)
            {
                glamourerIpc.UnlockState(actorIndex, spawned.DisplayName);
            }
        }

        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.UnassignCollectionForActor(actorIndex);
        }

        // =========================================================================
        // パイプライン B: MCDF の場合 (AQR McdfCharaFileManager 準拠)
        // =========================================================================
        if (template.SourceType == CharacterSourceType.Mcdf)
        {
            if (!string.IsNullOrWhiteSpace(template.McdfFilePath) && mcdfParser != null)
            {
                try
                {
                    string configDir = pluginInterface?.ConfigDirectory.FullName ?? Path.GetTempPath();
                    string cacheDir = Path.Combine(configDir, "mcdf_cache");
                    Directory.CreateDirectory(cacheDir);

                    var bundle = mcdfParser.ExtractMcdfBundle(template.McdfFilePath, cacheDir);
                    if (bundle != null)
                    {
                        if (spawned?.TemporaryCollectionGuid.HasValue == true)
                        {
                            penumbraIpc.DeleteTemporaryCollection(spawned.TemporaryCollectionGuid.Value);
                            spawned.TemporaryCollectionGuid = null;
                        }

                        if (penumbraIpc.IsAvailable)
                        {
                            var tempGuid = penumbraIpc.CreateTemporaryCollection(template.Name);
                            if (tempGuid != Guid.Empty)
                            {
                                if (bundle.ModPaths.Count > 0 || !string.IsNullOrEmpty(bundle.ManipulationData))
                                {
                                    penumbraIpc.AddTemporaryMod(tempGuid, bundle.ModPaths, bundle.ManipulationData ?? string.Empty);
                                }
                                bool assignOk = penumbraIpc.AssignTemporaryCollection(tempGuid, actorIndex);
                                if (spawned != null)
                                {
                                    spawned.TemporaryCollectionGuid = tempGuid;
                                }
                                logManager?.Info($"MCDF: Assigned Penumbra temporary collection {tempGuid} to actor #{actorIndex} (Success: {assignOk}) with {bundle.ModPaths.Count} mod files.");
                            }
                        }

                        // AQR 準拠: MCDF 内包の Base64 データをそのまま無加工で適用
                        string? designString = bundle.GlamourerDesign;
                        if (string.IsNullOrWhiteSpace(designString)) designString = template.GlamourerDesignString;

                        if (glamourerIpc.IsAvailable && !string.IsNullOrWhiteSpace(designString))
                        {
                            bool glamSuccess = glamourerIpc.ApplyDesignToActor(designString, actorIndex, spawned?.PuppetName);
                            logManager?.Info($"MCDF Glamourer ApplyDesign result on Global#{actorIndex} ('{spawned?.PuppetName}'): {glamSuccess}");
                        }

                        // 武器の表示・非表示
                        chara->DrawData.HideWeapons(!template.WeaponVisible);
                        chara->DrawData.IsWeaponHidden = !template.WeaponVisible;

                        // Penumbra Redraw (AQR 方式: 最後に必ず Redraw)
                        if (penumbraIpc.IsAvailable)
                        {
                            penumbraIpc.Redraw(actorIndex);
                            logManager?.Info($"MCDF Penumbra Redraw for Global#{actorIndex}.");
                        }

                        // Customize+ Profile の適用
                        ApplyCustomizePlusProfile(chara, actorIndex, template, spawned, bundle.CustomizePlusData);

                        if (spawned != null) spawned.IsReady = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logManager?.Error($"Error applying MCDF bundle: {ex.Message}");
                }
            }
            return;
        }

        // =========================================================================
        // パイプライン A: 通常の Glamourer & Penumbra & Customize+ (AQR 準拠)
        // =========================================================================

        // 1. Penumbra コレクションの適用 (Guid渡し & 直後 RedrawObject)
        if (penumbraIpc.IsAvailable && !string.IsNullOrWhiteSpace(template.PenumbraCollectionName))
        {
            bool penSuccess = penumbraIpc.SetCollectionForActor(template.PenumbraCollectionName, actorIndex);
            logManager?.Info($"Penumbra SetCollection '{template.PenumbraCollectionName}' on Global#{actorIndex}: {penSuccess}");
            if (penSuccess)
            {
                penumbraIpc.Redraw(actorIndex);
            }
        }

        // 2. Glamourer デザインの適用 (Guid 指定または PlayerClone)
        if (glamourerIpc.IsAvailable)
        {
            string? designString = template.GlamourerDesignString;

            if (!string.IsNullOrWhiteSpace(designString))
            {
                bool glamSuccess = glamourerIpc.ApplyDesignToActor(designString, actorIndex, spawned?.PuppetName);
                logManager?.Info($"Glamourer ApplyDesign result on Global#{actorIndex} ('{spawned?.PuppetName}'): {glamSuccess}");
            }
            else if (template.SourceType == CharacterSourceType.PlayerClone)
            {
                var playerDesign = glamourerIpc.GetCustomization(0);
                if (!string.IsNullOrWhiteSpace(playerDesign))
                {
                    bool glamSuccess = glamourerIpc.ApplyDesignToActor(playerDesign, actorIndex, spawned?.PuppetName);
                    logManager?.Info($"Applied player customization clone via Glamourer to Global#{actorIndex} ('{spawned?.PuppetName}'): {glamSuccess}");
                }
            }
        }

        // 3. 武器の表示・非表示
        chara->DrawData.HideWeapons(!template.WeaponVisible);
        chara->DrawData.IsWeaponHidden = !template.WeaponVisible;

        // 4. Customize+ Profile の適用
        ApplyCustomizePlusProfile(chara, actorIndex, template, spawned);

        if (spawned != null) spawned.IsReady = true;
    }

    /// <summary>
    /// Customize+ Profile (テンプレート指定 または MCDF内包) をパペット名とワールドIDで紐付け (Caraxi / AQR 準拠)
    /// インデックス指定を行わないため、自キャラ(LocalPlayer Index 0)への誤爆は物理的に完全不可能
    /// </summary>
    private void ApplyCustomizePlusProfile(Character* chara, int actorIndex, CharacterTemplate template, SpawnedActorData? spawned, string? fallbackMcdfCPlusData = null)
    {
        if (customizePlusIpc == null || !customizePlusIpc.IsAvailable) return;

        try
        {
            ushort worldId = chara != null ? (ushort)chara->HomeWorld : (ushort)0;
            string puppetName = spawned?.PuppetName ?? string.Empty;

            // 既存の紐付け解除
            if (spawned?.AssignedCustomizePlusGuid.HasValue == true && !string.IsNullOrEmpty(puppetName))
            {
                customizePlusIpc.RemovePlayerCharacter(spawned.AssignedCustomizePlusGuid.Value, puppetName, worldId);
                spawned.AssignedCustomizePlusGuid = null;
            }

            // 1. テンプレートで明示指定された CustomizePlus プロファイル
            if (!string.IsNullOrWhiteSpace(template.CustomizePlusProfileGuid) &&
                Guid.TryParse(template.CustomizePlusProfileGuid, out var profileGuid))
            {
                if (!string.IsNullOrEmpty(puppetName))
                {
                    bool ok = customizePlusIpc.AddPlayerCharacter(profileGuid, puppetName, worldId);
                    if (ok && spawned != null)
                    {
                        spawned.AssignedCustomizePlusGuid = profileGuid;
                        logManager?.Info($"CustomizePlus: Mapped profile '{template.CustomizePlusProfileName ?? profileGuid.ToString()}' ({profileGuid}) to puppet '{puppetName}' (World: {worldId}).");
                    }
                    else
                    {
                        logManager?.Warning($"CustomizePlus: Failed to map profile '{profileGuid}' to puppet '{puppetName}'.");
                    }
                }
            }
            // 2. MCDF に内包された CustomizePlus データ
            else if (!string.IsNullOrWhiteSpace(fallbackMcdfCPlusData))
            {
                string cPlusJson = fallbackMcdfCPlusData.Trim();
                if (!cPlusJson.StartsWith("{") && !cPlusJson.StartsWith("["))
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(cPlusJson);
                        cPlusJson = Encoding.UTF8.GetString(bytes);
                    }
                    catch (Exception ex)
                    {
                        logManager?.Warning($"Failed to base64-decode MCDF CustomizePlus data: {ex.Message}");
                    }
                }

                // MCDF のプロファイル JSON から Guid を取得して紐付け試行
                try
                {
                    var parsed = Newtonsoft.Json.Linq.JObject.Parse(cPlusJson);
                    if (parsed["UniqueId"] != null && Guid.TryParse(parsed["UniqueId"]!.ToString(), out var mcdfProfileGuid))
                    {
                        if (!string.IsNullOrEmpty(puppetName))
                        {
                            bool ok = customizePlusIpc.AddPlayerCharacter(mcdfProfileGuid, puppetName, worldId);
                            if (ok && spawned != null)
                            {
                                spawned.AssignedCustomizePlusGuid = mcdfProfileGuid;
                                logManager?.Info($"CustomizePlus: Mapped MCDF profile {mcdfProfileGuid} to puppet '{puppetName}'.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logManager?.Warning($"Could not map MCDF CustomizePlus profile: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Error($"Error applying CustomizePlus profile: {ex.Message}");
        }
    }

    /// <summary>
    /// 互換用ラッパー
    /// </summary>
    private void ApplyExternalAppearance(SpawnedActorData spawned, CharacterTemplate template)
    {
        if (spawned.NativeAddress == 0) return;
        ApplyAppearanceDirect((Character*)spawned.NativeAddress, spawned.GlobalIndex, template, spawned);
    }

    /// <summary>
    /// プレビュー中アクターの武器表示状態を切り替えて即座に再描画する
    /// </summary>
    public void SetWeaponVisibility(SpawnedActorData actor, bool visible)
    {
        if (actor.NativeAddress == 0) return;
        var nativeChara = (Character*)actor.NativeAddress;
        int actorIndex = (int)actor.GlobalIndex;

        nativeChara->DrawData.HideWeapons(!visible);
        nativeChara->DrawData.IsWeaponHidden = !visible;
        nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);

        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.Redraw(actorIndex);
        }
        logManager?.Info($"Updated weapon visibility for '{actor.DisplayName}' (Visible: {visible}, GlobalIndex: {actorIndex}).");
    }

    private Vector3 GetDefaultSpawnPosition()
    {
        var player = objectTable.Length > 0 ? objectTable[0] : null;
        if (player == null) return Vector3.Zero;

        // 自キャラの正面1.5mの位置を初期位置にする
        var rot = player.Rotation;
        var forward = new Vector3((float)Math.Sin(rot), 0, (float)Math.Cos(rot));
        return player.Position + (forward * 1.5f);
    }

    /// <summary>
    /// 自キャラ (LocalPlayer Index 0) の Glamourer ステートおよび Penumbra コレクションをリバートして本来の姿に復元する
    /// </summary>
    public void RevertLocalPlayer()
    {
        void ExecuteRevert()
        {
            try
            {
                var player = objectTable.Length > 0 ? objectTable[0] as ICharacter : null;
                string? name = player?.Name.TextValue;
                glamourerIpc?.RevertLocalPlayer(name, player);
                if (penumbraIpc != null && penumbraIpc.IsAvailable)
                {
                    penumbraIpc.UnassignCollectionForActor(0);
                }
                logManager?.Info($"Reverted LocalPlayer state via Glamourer & Penumbra on main thread (Name: '{name}').");
            }
            catch (Exception ex)
            {
                logManager?.Warning($"RevertLocalPlayer failed: {ex.Message}");
            }
        }

        if (framework != null)
        {
            framework.RunOnFrameworkThread(ExecuteRevert);
        }
        else
        {
            ExecuteRevert();
        }
    }

    public void Dispose()
    {
        clientState.TerritoryChanged -= OnTerritoryChanged;
        DespawnAll();
    }
}
