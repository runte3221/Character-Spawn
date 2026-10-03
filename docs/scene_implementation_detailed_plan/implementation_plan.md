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
3. **大規模シーン（最大100体）・大容量MOD対応の非同期分散アーキテクチャ**:
   - 100体規模の配置、大容量カスタムモーションMOD、3Dサウンド、アセット非表示が複合してもメインスレッドを停止させない。
   - 1フレームに全アクターを一括生成せず、優先度付き非同期キューでフレーム分散（スタッガー）スポーン。
   - 距離ベース動的仮想化（Distance Culling）により常時実体化数を 20〜30 体に制限し、COMスロット枯渇とVRAM爆発を防止。
   - MCDF インメモリキャッシュと Penumbra 非同期ロード待機（ディファード Redraw）により素体化（MOD抜け）を根絶。

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

    // Phase 5: マップ上の消去アセット一覧（正式実装）
    public List<SceneHiddenAssetEntry> HiddenAssets { get; set; } = new();
}

public class SceneActorPlacement
{
    public Guid PlacementId { get; set; } = Guid.NewGuid();
    public string CharacterTemplateId { get; set; } = string.Empty;
    public string CustomDisplayName { get; set; } = string.Empty; // 空ならテンプレート名
    public Vector3 Position { get; set; }
    public float Rotation { get; set; }
    public bool AutoSpawnOnTerritory { get; set; } = false;

    // Phase 2: モーション・視線・接近リアクション設定
    public SceneActorMotionConfig Motion { get; set; } = new();

    // Phase 3: ネームプレート・称号設定
    public SceneActorNamePlateConfig NamePlate { get; set; } = new();

    // Phase 4: 接近時サウンド設定（正式実装）
    public SceneActorSoundConfig Sound { get; set; } = new();
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

### ■ Phase 4: 3D空間オーディオ・接近サウンド制御（正式実装）

#### 1. 3D空間オーディオエンジン (`Services/SoundService.cs`)
- パペットの 3D 座標とプレイヤー座標の距離減衰・パンニング計算。
- 距離減衰式:
  $$\text{Volume} = \text{Clamp}\left(1.0 - \frac{\text{Distance}}{\text{TriggerDistance}}, 0.0, 1.0\right) \times \text{BaseVolume}$$
- 接近判定:
  - プレイヤーが `TriggerDistance` 内に入ると、設定された音声（ゲーム内環境音/SE、または外部 WAV/OGG/MP3 ファイル）を自動再生（フェードイン）。
  - 離脱時は自然にフェードアウト・停止。

#### 2. UI 統合
- 各配置キャラクターの設定モーダル内に「サウンド設定」タブを追加。
- 音声ファイルパス選択（または内蔵サウンド一覧）、プレビュー再生ボタン、検知距離・基準音量スライダー。

#### 【Phase 4 の品質検証チェックリスト】
- [ ] **[QA-4-1: 接近フェードと減衰]**: プレイヤーが近づいた時に音が自然に立ち上がり、離れると距離に応じて減衰・消音するか。
- [ ] **[QA-4-2: 音声ファイルの安全ハンドリング]**: 指定ファイルが存在しない、またはフォーマット不正の場合でも、ゲームが落ちず安全に警告ログが出るか。
- [ ] **[QA-4-3: 複数音源の定位]**: 複数のパペットに異なる音声を配置した場合、それぞれの方向（左右パン）と距離から自然に立体的に聞こえるか。

---

### ■ Phase 5: マップアセット消去・スポイト制御（正式実装）

#### 1. スポイト機能とオブジェクト検出 (`Services/AssetHiderService.cs`)
- Stagehand / Brio 準拠のレイキャスト処理。
- マウスカーソル直下、またはプレイヤー前方の視線光線（Ray）とマップの衝突判定（BG / Layout / Housing オブジェクト）。
- クリックされたオブジェクトのポインタ（`DrawObject*`）と識別子（ModelId, SGB ID, 座標）を取得。

#### 2. 非表示制御とシーン保存
- 該当オブジェクトの描画ポインタに対して `DrawObject->DisableDraw()` または可視フラグ操作を実行し、**マップ上から目的の小道具・家具をピンポイントで消去**。
- シーンデータ `SceneData.HiddenAssets` に消去したオブジェクト情報を記録。
- シーンスポーン時に自動消去、シーンデスポーン時またはゾーン移動時に `EnableDraw()` で 100% 確実に復元。

#### 3. UI 統合
- Scene タブに「アセット消去（スポイト）」サブセクションを追加。
- 「スポイト開始」トグルボタン、選択中のオブジェクトのハイライト表示。
- 「消去アセット一覧」テーブル、個別「復元」ボタン、一括復元ボタン。

#### 【Phase 5 の品質検証チェックリスト】
- [ ] **[QA-5-1: ピンポイント消去]**: 目的の椅子・道具だけが正確に消え、周囲の地面・壁・建物全体が巻き添えで消えないか。
- [ ] **[QA-5-2: シーン連動の完全復元]**: シーンデスポーン時、または別エリアへテレポした際、消去されていたオブジェクトが 100% 元通り再表示されるか。
- [ ] **[QA-5-3: シーン再スポーン時の自動再消去]**: 一度保存したシーンを再スポーンさせた際、以前消去したオブジェクトが自動的に再び消去されるか。

---

## 3. 大規模シーン（最大100体規模）・大容量MOD対応アーキテクチャと段階的ロードマップ

### 3.1 技術的限界とボトルネックの分析
- **COMスロット上限**: FFXIV の BattleCharacter テーブル（上限 200〜400 枠）の枯渇リスク。
- **DirectX / VRAM 圧迫**: 100体分の高解像度MODテクスチャ・メッシュ・ボーンによる VRAM 枯渇クラッシュ。
- **ボーン評価 CPU 負荷**: 100体全員が常時フルスケルトンのカスタムモーションを計算することによる FPS 大幅低下。
- **Penumbra / IPC キュー競合**: 100体分の Mod 登録と Redraw 要求殺到による非同期スレッドのパンク。
- **オーディオチャンネル上限**: DirectSound / XAudio2 の発音上限オーバーとメモリ圧迫。

### 3.2 4大基盤アーキテクチャ
1. **距離ベース動的仮想化 (Distance Culling & LOD)**:
   - 100体配置されていても、自キャラからの距離（例: 半径 50m 以内）にいるアクターのみ動的実体化。
   - 常時実体化数を 20〜30 体程度に制限し、COM 枯渇と VRAM 爆発を物理的に防止。
2. **非同期優先度スポーンパイプライン (Async Priority Queue & Staggered Spawning)**:
   - 自キャラに近いアクターから優先的に順次スポーン。
   - 1アクターあたり「生成 → 外見・Mod登録 → 数フレーム待機（Penumbra安定化） → モーション適用」を各フレーム分散実行。
   - メインスレッドのフレームヒッチを 0ms（毎フレーム 60fps 維持）に抑え、リソース競合を根絶。
3. **イベント駆動型コンポーネントシステム (ECS)**:
   - **モーション（MOD対応）**: 自キャラが一定距離（例: 5m〜10m）に近づいた瞬間にのみ大容量カスタムモーションを発動・評価（遠距離は軽量待機アイドル）。
   - **サウンド（近接 3D 音響）**: プレイヤーが可聴範囲（例: 8m以内）に入った時のみ 3D 空間音響としてストリーミング再生。離脱時は即座にアンロード。
4. **環境アセット制御の完全分離 (AssetVisibilityManager)**:
   - マップ上の既存アセット（椅子・小物等）の非表示化をキャラクター管理パイプラインから完全独立。
   - アクターの頭数に左右されず、相互干渉ゼロで安全に DrawObject フラグを制御。

### 3.3 段階的実装ロードマップ
- **ステップ 1（現在作業中）: 非同期スポーンキューと MCDF 安定化**
  - 将来のパイプライン拡張に耐えうる「非同期スポーンキュー（フレーム分散）」を `SceneManager` に導入。
  - `McdfParser` にインメモリキャッシュを導入し、同一 MCDF の解凍負荷（137ms）をゼロ化。
  - Penumbra の非同期ロード完了を待ってから描画を確定させるディファード機構により、素体化（MOD抜け）を完全解消。
- **ステップ 2: モーション・表情コンポーネント＆近接トリガー実装**
  - キャラクター個別のモーション設定、自キャラ接近時の発動トリガーを実装。
- **ステップ 3: 距離ベース仮想化（Distance Culling）と 100 体スケーリング**
  - 大規模シーンでも FPS を落とさない動的スポーン制御を有効化。
- **ステップ 4: サウンド制御＆背景アセット非表示機能の実装**
  - 独立した `AssetVisibilityManager` によるマップアセットのスポイト・非表示化および 3D サウンド再生。

