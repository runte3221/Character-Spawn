using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;

namespace CharacterSpawn.Models;

public enum CharacterSourceType
{
    Glamourer,
    Penumbra,
    Monster,
    Npc,
    Mcdf,
    PlayerClone,
    MountMinion
}

public class CharacterTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Character";
    public string FolderPath { get; set; } = string.Empty;
    public CharacterSourceType SourceType { get; set; } = CharacterSourceType.PlayerClone;

    // NPC / Monster / Minion / Mount IDs
    public uint DataId { get; set; } = 0; // ENpcBase, BNpcBase, Companion, or Mount ID
    public uint ModelCharaId { get; set; } = 0;
    public uint IconId { get; set; } = 0;
    public bool IsMount { get; set; } = false;

    // Appearance / Customization
    public int McType { get; set; } = 1; // 1=Human, 2=Demihuman, 3=Monster
    public float Scale { get; set; } = 1.0f;
    public bool WeaponVisible { get; set; } = true;
    public byte[]? CustomizeData { get; set; }
    public ulong[]? NpcEquipmentModelIds { get; set; } // Head, Body, Hands, Legs, Feet, Ears, Neck, Wrists, RingR, RingL
    public ulong NpcMainHandModelId { get; set; } = 0;
    public ulong NpcOffHandModelId { get; set; } = 0;
    public Dictionary<EquipSlot, EquipmentItem>? Equipment { get; set; }

    // External integration data
    public string? GlamourerDesignString { get; set; }
    public string? PenumbraCollectionName { get; set; }
    public string? McdfFilePath { get; set; }
    public string? CustomizePlusProfileGuid { get; set; }
    public string? CustomizePlusProfileName { get; set; }

    public CharacterTemplate Clone()
    {
        return (CharacterTemplate)MemberwiseClone();
    }
}

public enum EquipSlot
{
    MainHand = 0,
    OffHand = 1,
    Head = 2,
    Body = 3,
    Hands = 4,
    Legs = 5,
    Feet = 6,
    Ears = 7,
    Neck = 8,
    Wrists = 9,
    RightRing = 10,
    LeftRing = 11
}

public class EquipmentItem
{
    public uint ItemId { get; set; }
    public byte StainId { get; set; }
}

public class TransformData
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public float Rotation { get; set; } = 0.0f; // Radians
    public float Scale { get; set; } = 1.0f;
}

public class AnimationSettings
{
    public ushort TimelineId { get; set; } = 0;
    public string TimelineName { get; set; } = "None";
    public bool IsLoop { get; set; } = false;
    public ushort FacialExpressionId { get; set; } = 0;
    public string FacialExpressionName { get; set; } = "None";
    public bool LookAtPlayer { get; set; } = false;
}

public class NamePlateSettings
{
    public bool Show { get; set; } = true;
    public string CustomName { get; set; } = string.Empty;
}

public class SpawnedActorData
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Character";
    public string PuppetName { get; set; } = string.Empty;

    public TransformData Transform { get; set; } = new();
    public AnimationSettings Animation { get; set; } = new();
    public NamePlateSettings NamePlate { get; set; } = new();
    public bool IsTargetable { get; set; } = true;

    [JsonIgnore]
    public nint NativeAddress { get; set; } = 0;

    [JsonIgnore]
    public ushort GlobalIndex { get; set; } = 0;

    [JsonIgnore]
    public ushort ComIndex { get; set; } = 0;

    [JsonIgnore]
    public uint GameObjectId { get; set; } = 0;

    [JsonIgnore]
    public bool IsSpawned => NativeAddress != 0;

    [JsonIgnore]
    public bool IsReady { get; set; } = false;

    [JsonIgnore]
    public Guid? TemporaryCollectionGuid { get; set; }

    [JsonIgnore]
    public Guid? TemporaryCustomizePlusGuid { get; set; }

    [JsonIgnore]
    public Guid? AssignedCustomizePlusGuid { get; set; }
}

public class ScenePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Scene";
    public uint TerritoryTypeId { get; set; } = 0;
    public string TerritoryName { get; set; } = "Unknown";
    public bool AutoSpawnOnZone { get; set; } = false;
    public List<SpawnedActorData> Actors { get; set; } = new();
}
