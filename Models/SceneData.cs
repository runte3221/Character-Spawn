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

    /// <summary>
    /// Step 2.2: 自律移動・パトロール・追従設定
    /// </summary>
    public SceneActorMovementConfig Movement { get; set; } = new();
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

    // 視線追従 (LookAt Player / LookAt Custom Spawn)
    public bool LookAtPlayer { get; set; } = false;
    public bool LookAtCustomSpawn { get; set; } = false;
    public Guid LookAtTargetPlacementId { get; set; } = Guid.Empty;
    public float BodyTurnAngleLimit { get; set; } = 0.0f; // 体の回転許容角度(度)。0=首・視線のみ追従, 45=左右45度まで体も追従, 180=全方位追従
    public float LookAtMaxDistance { get; set; } = 15.0f; // 視線追従の最大有効距離(m)。範囲外に出ると追従解除 (デフォルト: 15.0m)

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
    public bool ShowCustomName { get; set; } = false; // [ ] Custom Name (デフォルトは非表示、チェック時のみ通常表示)
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

/// <summary>
/// 自律移動モード
/// </summary>
public enum MovementMode
{
    None = 0,               // 静止
    Patrol = 1,             // ウェイポイント巡回
    FollowPlayer = 2,       // プレイヤー追従
    PatrolAndFollow = 3     // 巡回＋プレイヤー接近時一時追従
}

/// <summary>
/// 巡回ループ方式
/// </summary>
public enum PatrolLoopType
{
    Loop = 0,               // 循環 (A -> B -> C -> A ...)
    PingPong = 1,           // 往復 (A -> B -> C -> B -> A ...)
    Once = 2                // 片道 (A -> B -> C で停止)
}

/// <summary>
/// 巡回ウェイポイント
/// </summary>
public class SceneActorWaypoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Vector3 Position { get; set; } = Vector3.Zero;
    public float WaitSeconds { get; set; } = 0.0f; // 到着時の待機秒数
    public ushort ActionTimelineId { get; set; } = 0; // 到着時のモーション (0=待機維持)
    public string ActionTimelineKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Step 2.2: 自律移動・パトロール・追従設定
/// </summary>
public class SceneActorMovementConfig
{
    public MovementMode Mode { get; set; } = MovementMode.None;
    public PatrolLoopType LoopType { get; set; } = PatrolLoopType.Loop;
    public float Speed { get; set; } = 2.5f; // 移動速度 (m/s) [歩き=2.0, 駆け足=4.0, 走り=6.0]
    public float TurnSpeed { get; set; } = 360.0f; // 旋回速度 (度/秒)
    
    // ウェイポイントリスト
    public List<SceneActorWaypoint> Waypoints { get; set; } = new();

    // 移動中モーション設定 (0=自動/未指定)
    public ushort WalkTimelineId { get; set; } = 0;
    public string WalkTimelineKey { get; set; } = string.Empty;

    // プレイヤー接近追従 & テリトリー帰還設定
    public float FollowTriggerDistance { get; set; } = 4.0f; // 接近検知距離
    public float FollowStopDistance { get; set; } = 1.8f;    // 停止距離
    public float MaxTerritoryDistance { get; set; } = 15.0f; // テリトリー限界距離 (ホームからの最大距離)
    public bool ReturnToHome { get; set; } = true;          // 追従解除時にホーム/直前地点へ歩いて戻る
}

