using System;
using System.Collections.Generic;
using System.Numerics;

namespace CharacterSpawn.Models;

/// <summary>
/// 1つのシーン全体のデータ構造
/// 複数のキャラクター配置、将来のマップ消去アセット一覧、およびゾーン紐付けを管理
/// </summary>
public class SceneData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新規シーン";
    public string FolderPath { get; set; } = string.Empty; // フォルダ階層 (例: "My House/2F")
    public uint TerritoryId { get; set; } = 0; // 0 = ゾーン制限なし（どこでもスポーン可能）
    public string TerritoryName { get; set; } = string.Empty; // 表示用テリトリー名 (例: "中央森林")
    public bool AutoSpawn { get; set; } = false; // 対象エリアに入ったら自動的にスポーンする
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// シーン内に配置されるキャラクターリスト
    /// </summary>
    public List<SceneActorPlacement> Placements { get; set; } = new();

    /// <summary>
    /// Phase 5: マップ上の消去アセット一覧（正式実装用先行定義）
    /// </summary>
    public List<SceneHiddenAssetEntry> HiddenAssets { get; set; } = new();
}

/// <summary>
/// シーン内の各キャラクター配置定義
/// </summary>
public class SceneActorPlacement
{
    public Guid PlacementId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 参照するキャラクターテンプレートの ID (CharacterTemplate.Id)
    /// </summary>
    public string CharacterTemplateId { get; set; } = string.Empty;

    /// <summary>
    /// シーン内での個別表示名（空の場合はテンプレート名を使用）
    /// </summary>
    public string CustomDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 配置 3D 座標
    /// </summary>
    public Vector3 Position { get; set; } = Vector3.Zero;

    /// <summary>
    /// 配置向き・回転 (Y軸ラジアン)
    /// </summary>
    public float Rotation { get; set; } = 0f;

    /// <summary>
    /// 配置スケール (デフォルト 1.0f)
    /// </summary>
    public float Scale { get; set; } = 1.0f;

    /// <summary>
    /// 個別アクターの表示／非表示（目のアイコン切り替え）
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Phase 2: モーション・視線・表情・接近リアクション設定
    /// </summary>
    public SceneActorMotionConfig Motion { get; set; } = new();

    /// <summary>
    /// Phase 3: ネームプレート・称号設定
    /// </summary>
    public SceneActorNamePlateConfig NamePlate { get; set; } = new();

    /// <summary>
    /// Phase 4: 接近時サウンド設定
    /// </summary>
    public SceneActorSoundConfig Sound { get; set; } = new();
}

/// <summary>
/// Phase 2: モーション・視線・接近リアクション設定
/// </summary>
public class SceneActorMotionConfig
{
    public ushort TimelineId { get; set; } = 0;
    public string TimelineKey { get; set; } = string.Empty;
    public bool IsLoop { get; set; } = true;
    public float Speed { get; set; } = 1.0f;

    // 表情
    public ushort FacialTimelineId { get; set; } = 0;

    // 視線追従 (LookAt)
    public bool LookAtPlayer { get; set; } = false;

    // 接近リアクション (Proximity Trigger)
    public bool EnableProximityReaction { get; set; } = false;
    public float ProximityDistance { get; set; } = 3.0f;
    public ushort ReactionTimelineId { get; set; } = 0;
    public string ReactionTimelineKey { get; set; } = string.Empty;
    public float CooldownSeconds { get; set; } = 5.0f;
}

/// <summary>
/// Phase 3: ネームプレート・称号設定
/// </summary>
public class SceneActorNamePlateConfig
{
    public bool ShowCustomName { get; set; } = true; // [x] Custom Name (ネームプレート表示)
    public bool ShowCustomTitle { get; set; } = false; // [ ] Custom Title (称号表示)
    public string CustomTitle { get; set; } = string.Empty; // カスタム称号文字列
    public bool HideNamePlate { get; set; } = false;
    public bool HideTitle { get; set; } = false;
}

/// <summary>
/// Phase 4: 接近時サウンド設定
/// </summary>
public class SceneActorSoundConfig
{
    public bool EnableSound { get; set; } = false;
    public string SoundPath { get; set; } = string.Empty;
    public float TriggerDistance { get; set; } = 4.0f;
    public float Volume { get; set; } = 1.0f;
    public bool Is3D { get; set; } = true;
    public bool Loop { get; set; } = false;
}

/// <summary>
/// Phase 5: マップ上の消去アセットエントリ
/// </summary>
public class SceneHiddenAssetEntry
{
    public string AssetKey { get; set; } = string.Empty;
    public ulong GameObjectId { get; set; } = 0;
    public uint ModelId { get; set; } = 0;
    public Vector3 Position { get; set; } = Vector3.Zero;
    public string Description { get; set; } = string.Empty;
}
