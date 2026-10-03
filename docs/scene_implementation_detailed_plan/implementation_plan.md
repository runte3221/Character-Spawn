# 実装計画書: シーン・配置管理（Scene）詳細設計と品質保証（QA）計画

## 1. 全体アーキテクチャ方針と隔離設計

### 【設計の黄金律】
1. **第1工程コアの不変性**:
   - `ActorManager.cs` の `SpawnCharacterInternal`、`ApplyAppearanceDirect`、`CustomizePlusIpc.cs`、`GlamourerIpc.cs` は完成品として凍結。
   - `SceneManager` は、単に `ActorManager.SpawnCharacter(template, position, rotation)` を複数回安全に呼び出す上位コントローラーとして振る舞う。
2. **疎結合なモジュール分割**:
   - `PlacementService`: 座標・複数配置
   - `AnimationService`: モーション・表情・視線・接近検知
   - `NamePlateService`: ネームプレート描画フック
   - `SoundService`: オーディオ演出（将来）
   - `AssetHiderService`: マップオブジェクト消去（将来）
   各サービスは互いの内部実装を知らず、`SceneActorPlacement` の設定情報のみを介して機能する。

---

## 2. 各フェーズの詳細実装計画

### ■ Phase 1: シーン・データ基盤と複数体配置管理

#### 1. データモデル設計 (`Models/SceneData.cs`)
```csharp
public class SceneData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Scene";
    public uint TerritoryId { get; set; } = 0; // 0 = どこでも可
    public string Description { get; set; } = string.Empty;
    public List<SceneActorPlacement> Placements { get; set; } = new();
}

public class SceneActorPlacement
{
    public Guid PlacementId { get; set; } = Guid.NewGuid();
    public string CharacterTemplateId { get; set; } = string.Empty;
    public string CustomDisplayName { get; set; } = string.Empty; // 空ならテンプレート名
    public Vector3 Position { get; set; }
    public float Rotation { get; set; }
    public bool AutoSpawnOnTerritory { get; set; } = false;

    // Phase 2, 3 の設定用オブジェクト (null 安全)
    public SceneActorMotionConfig Motion { get; set; } = new();
    public SceneActorNamePlateConfig NamePlate { get; set; } = new();
}
```

#### 2. スポーン・ライフサイクル管理 (`Managers/SceneManager.cs`)
- `ActiveScene`: 現在アクティブなシーンの参照。
- `SpawnedSceneActors`: `Dictionary<Guid, SpawnedActorData>` で各 `PlacementId` と実アクターを 1 対 1 で追跡。
- **COMスロット安全確保**:
  - `ClientObjectManager` の空きスロットを逐次取得し、スロット不足（0xFFFFFFFF）時は適切に警告を出してそれ以上のスポーンを中断（クラッシュ防止）。
- **テリトリー変更検知**:
  - `ClientState.TerritoryChanged` を購読し、ゾーン移動時は即座に全シーンアクターの一括デスポーン処理を実行（他マップへの残骸残留を 100% 防止）。

#### 3. UI 構築 (`UI/SceneTab.cs`)
- シーンリスト（左ペイン）、シーン詳細（右ペイン）。
- 配置キャラクター一覧テーブル：
  - キャラクター名（テンプレート名）、座標、向き、スポーン状態（緑/灰インジケーター）、個別スポーン／デスポーンボタン。
- 「現在地に配置」「一括スポーン」「一括デスポーン」ボタン。

#### 【Phase 1 の品質検証チェックリスト】
- [ ] **[QA-1-1: 複数体同時スポーン]**: 異なるテンプレート（Glamourer、MCDF、NPC、Monster）の 4 キャラクターを同時にスポーンさせ、全員が正常な外見で出現するか。
- [ ] **[QA-1-2: 座標・回転の正確性]**: 指定した X, Y, Z, Rotation がゲーム内のアクター位置とミリ単位で一致しているか。
- [ ] **[QA-1-3: 一括デスポーンの完全性]**: 一括デスポーン後、CustomizePlus の一時プロファイル、Penumbra の一時コレクション、武器フラグ、COM スロットが完全に解放されているか（メモリリーク・残骸ゼロ）。
- [ ] **[QA-1-4: Character タブとの独立性]**: シーンアクターがスポーンしている状態で、Character タブの Preview スポーンを行っても互いに干渉・上書きされないか。
- [ ] **[QA-1-5: ゾーン移動テスト]**: シーンスポーン中にテレポ／デジョンを行い、移動先で古いアクターが綺麗に消去されているか。

---

### ■ Phase 2: モーション・視線・表情・接近リアクション制御

#### 1. モーション・ループ制御 (`Services/AnimationService.cs`)
- HDM の `AnimationService` 準拠で、指定アクターの `ActionTimeline` を操作。
- **ループ維持の仕組み**:
  - 単発モーション終了時にゲームエンジンが立ちポーズに戻すイベントを検知し、ループ指定時は即座にリプレイ（LoopReplay）をトリガー。
- **表情固定**:
  - 口元・目元スロットに表情タイムライン（笑顔、不機嫌、驚き等）を固定注入。

#### 2. 視線追従 (LookAt / Head Tracking)
- パペット構造体 `Character*` の `TargetId` に `localPlayer.GameObjectId` を設定。
- 自キャラの移動に合わせて、首と視線が自然に追尾することを確認。
- OFF の場合は `TargetId = 0xE0000000` または `0` にクリアして正面固定。

#### 3. 接近トリガー (Proximity Reaction)
- `Framework.Update` イベントで 0.1 秒間隔（10Hz）で自キャラと各パペットの 3D 距離を計算。
- **状態マシン**:
  ```text
  [State: Idle] (通常待機モーション再生中)
     │ 距離 <= TriggerDistance
     ▼
  [State: Reacting] (接近リアクション再生: 例: 手を振る)
     │ 再生完了 または 距離 > TriggerDistance + マージン
     ▼
  [State: Cooldown] (指定秒数は再発火しない)
     │ クールダウン終了
     ▼
  [State: Idle]
  ```

#### 【Phase 2 の品質検証チェックリスト】
- [ ] **[QA-2-1: ループモーションの連続性]**: 「座る」「腕組み」などをループ指定し、5 分以上放置してもポーズが崩れないか。
- [ ] **[QA-2-2: NPC専用モーション]**: 「掃除」「屋台の呼び込み」等の NPC 専用アニメーションが正常に再生できるか。
- [ ] **[QA-2-3: 接近検知と復帰]**: 3m に近づいた瞬間に会釈や手を振るモーションが発動し、離れるとスムーズに元の待機モーションに戻るか。
- [ ] **[QA-2-4: パフォーマンス]**: 10 体のパペットが同時に距離計算・モーション監視を行っても FPS 低下（スタッター）が発生しないか。
- [ ] **[QA-2-5: 外見整合性]**: モーション再生中・表情変更中に Customize+ の骨格変形や Glamourer の装備が破綻しないか。

---

### ■ Phase 3: ネームプレート・称号制御

#### 1. ネームプレート安全フック (`Services/NamePlateService.cs`)
- `INamePlateGui.OnNamePlateDraw` をフック。
- パペットの `GameObjectId`（COM オブジェクト ID）をハッシュセットで高速判定。
- `args.IsVisible = !config.HideNamePlate`
- 称号（Title）の書き換え／消去：
  - `args.Title = config.CustomTitle`

#### 【Phase 3 の品質検証チェックリスト】
- [ ] **[QA-3-1: 適用対象の局所性]**: 設定したパペット以外のプレイヤー、ミニオン、正規NPCのネームプレートが一切変更されないか。
- [ ] **[QA-3-2: 描画安定性]**: カメラを極端に引いた時、遮蔽物に隠れた時、ターゲットした時でもネームプレート描画エラーが発生しないか。
- [ ] **[QA-3-3: アンロード復元]**: シーンデスポーン時、ゲーム本来のネームプレート状態に瞬時に戻るか。

---

### ■ Phase 4: サウンド制御 (将来拡張)
- 3D 空間音響による接近時オーディオ再生。
- 距離減衰式: $\text{Volume} = \text{Clamp}(1.0 - \frac{\text{Distance}}{\text{MaxDistance}}, 0.0, 1.0) \times \text{BaseVolume}$
- **[QA-4-1]**: 接近時にフェードインし、離れると自然に消音するか。
- **[QA-4-2]**: ファイル不正時に安全にエラーログを出力しクラッシュしないか。

---

### ■ Phase 5: アセットハイダー (将来拡張)
- Stagehand 準拠のレイキャスト選択と `DisableDraw()`。
- **[QA-5-1]**: 目的の椅子・道具だけがピンポイントで消去できるか。
- **[QA-5-2]**: シーンデスポーン時、消したオブジェクトが確実に 100% 復活するか。
