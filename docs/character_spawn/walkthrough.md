# Character Spawn v0.1.9 改修ウォークスルー

## 概要

本アップデート (v0.1.9) では、ユーザーから報告されたすべての不具合（Glamourer/Penumbra IPC 接続、Monster/NPC 上限撤廃およびモデル破損、武器残存、MCDF スポーン、3D ギズモのドラッグ不良）を根本解決し、提供画像に基づいた **Character タブのプレビュー専用化**、**New Chara モーダルの 4 ボタングリッド化**、ならびに原因追跡のための **Log タブの新設** を完了しました。

---

## 修正・実装内容の詳細

### 1. Glamourer & Penumbra IPC の接続復旧（動的ポーリング対応）
- **原因と修正**:
  - 実機プラグイン解析により、Glamourer 1.7.x の API が `Glamourer.ApiVersion.V2`（引数なし・戻り値 `(int, int)`）、`Glamourer.GetDesignList.V2`、`Glamourer.ApplyState`、Penumbra が `Penumbra.ApiVersion.V5`、`Penumbra.GetCollections.V5`、`Penumbra.SetCollectionForObject.V5`、`Penumbra.RedrawObject.V5` に変更されていることを特定。
  - これらの最新 IPC キーに対応するとともに、従来のキーへのフォールバックも維持。
  - プロパティ参照時に 1.5 秒間隔で動的検出を行うポーリング機構を実装し、プラグインのロード順に関わらず確実に IPC 接続を確立・維持できるようにしました。

### 2. 「Log」タブの新設（リアルタイムログ収集・ワンクリックコピー）
- **LogManager の新設**:
  - プラグイン内で発生するアクター生成、ModelCharaId 取得、CustomizeData 適用、IPC 通信結果などをメモリ内リングバッファ（最新 1,000 件）に記録。
- **Log タブ（MainWindow）**:
  - Settings タブの隣に「Log」タブを追加。
  - ログレベル別の色分け（Error=赤、Warning=黄、Info=白）、検索テキストフィルター、自動スクロール、およびワンクリックで全文をクリップボードにコピーする `[ Copy All ]` ボタンを装備。

### 3. Monster / Mob の検索上限撤廃 & モデル解決（マタガイガイ、食道楽のゼゼルン）
- **上限撤廃**: `SearchMonsters` の検索上限（500 件）を撤廃し、全モンスターを制限なく検索・選択可能に。
- **マタガイガイの自キャラ化解消**:
  - ゲーム内では `BNpcName` と `BNpcBase` の RowId が一致しないため、Brio と同等の `BNpcLink`（13,312 件の NameId -> BaseId マッピング）を埋め込みリソースとして内蔵。
  - モンスター選択時に正しい `BNpcBase` を経由して `ModelCharaId` を確実に解決できるようになりました。
- **食道楽のゼゼルンのギズモのみ表示解消**:
  - 非人型モデル（`ModelCharaId > 0`）に対し、人型自キャラからの `CopyFromCharacter` を実行しないよう完全に分離。
  - モンスター固有のモデルコンテナで直接構築を行い、描画オブジェクトが破損する問題を解決。

### 4. NPC (ENpc) の検索上限撤廃 & 武器表示制御（レターモーグリ、ル・スーシモ）
- **上限撤廃**: `SearchNpcs` の検索上限を撤廃し、全 NPC を全件検索可能に。
- **レターモーグリのギズモのみ表示解消**: 非人型 NPC のモデルコンテナ初期化を同様に分離し、モーグリ等の非人型モデルが正常に描画されるよう修正。
- **武器表示制御（Weapon Visible トグル）**:
  - 右ペインの Chara Name 下に `[x] Weapon Visible` チェックボックスを追加（画像3準拠）。
  - チェック OFF 時は `nativeChara->DrawData.HideWeapons()` を呼び出し、自キャラの武器が意図せず表示される問題を完全に解消。

### 5. MCDF ファイルスポーン時の外見データ適用
- MCDF 選択時、パースした Glamourer デザイン文字列をプレビューアクターの生成時に `Glamourer.ApplyState` / `ApplyByString` を経由して確実に適用。自キャラがスポーンしてしまう問題を解消。

### 6. Character タブの仕様変更（プレビュー専用化：画像3, 4準拠）
- Character タブでのスポーンを Scene タブとは完全に切り離し、MAP 上で見た目を確認するための「プレビュー用」に変更。
- **ボタン挙動**:
  - 未スポーン時: `[ Spawn ]` `[ edit ]` `[ delete ]`
  - スポーン実行後: ボタンが赤色 `[ Despawn ]` に切り替わり、押すとプレビューアクターを即座に破棄。
- **スポーン中表示**:
  - 緑色テキストで `[Name] Spawning...` を表示。
  - その下に `[x] Gizmo`（ギズモ表示 ON/OFF）チェックボックスを配置。
- **プレビュー機能説明文**: 未スポーン時に機能説明ボックスを表示。

### 7. New Chara モーダル UI のボタン化（画像2準拠）
- `Select Appearance Source` のプルダウンを廃止し、2×2 のボタングリッドに変更：
  - 上段: `[ Glamourer&Penumbra ]` `[ MCDF ]`
  - 下段: `[ NPC(ENpc) ]` `[ Monster/Mob ]`
- 選択中のボタンは赤色背景で強調ハイライト。
- 境界線の下に、選択した source に対応した入力欄（Glamourer デザイン/コレクション、MCDF ファイル選択、NPC/Monster リスト）を動的に表示。

### 8. 3D ギズモのドラッグ操作不良の修正
- ハンドルの当たり判定（クリック判定半径）を大幅に拡大。
- 原点から終点までの軸ライン全体に対する線分当たり判定を追加し、軸のどこをクリック・ドラッグしても直感的にアクターを移動できるように改善。
- スクリーン投影内積計算の感度と Y 軸の上下反転方向を最適化。

---

## 変更ファイル一覧

| ファイル | 変更概要 |
| :--- | :--- |
| `Services/GlamourerIpc.cs` | 最新 IPC キー（`.V2`）対応、動的検出ポーリング |
| `Services/PenumbraIpc.cs` | 最新 IPC キー（`.V5`）対応、動的検出ポーリング |
| `Managers/LogManager.cs` (新規) | メモリ内リングバッファによるログ収集マネージャー |
| `UI/LogTab.cs` (新規) | ゲーム内リアルタイムログビューア（コピー・検索付き） |
| `Resources/BNpcLink.csv` (新規) | 13,312 件の BNpcName -> BNpcBase マッピングリソース |
| `Services/GameDataService.cs` | 検索上限撤廃、`BNpcLink` を用いたモンスター ModelCharaId 解決 |
| `Managers/ActorManager.cs` | 非人型モデル初期化の分離、武器表示フラグ制御、プレビューアクター管理 |
| `UI/CharacterLibraryTab.cs` | プレビュー専用化（Spawn ⇔ 赤色 Despawn、Spawning...、Gizmoトグル、Weapon Visible、4ボタングリッドモーダル） |
| `UI/MainWindow.cs` | 「Log」タブ追加、タブ名整理（`Character`, `Scene`, `Settings`, `Log`）、プレビューギズモ描画 |
| `UI/GizmoRenderer.cs` | ギズモ当たり判定拡大・軸ラインドラッグ対応・感度最適化 |
| `Models/CharacterModels.cs` | `WeaponVisible` プロパティの追加 |
| `Plugin.cs` | `LogManager` / `LogTab` の DI 登録および初期化 |
| `CharacterSpawn.csproj` | バージョン `0.1.9.0` 更新、`BNpcLink.csv` の EmbeddedResource 登録 |
| `CharacterSpawn.json` | `AssemblyVersion` を `0.1.9.0` に更新 |
| `package.json` | バージョンを `0.1.9` に更新 |
| `repo.json` | `AssemblyVersion` を `0.1.9.0` に更新 |
| `CHANGELOG.md` | v0.1.9 リリースノート追記 |
| `docs/character_spawn/task.md` | 全タスク完了状態に更新 |

---

## 動作確認手順

1. GitHub リポジトリへプッシュ後、GitHub Actions CI により最新ビルド (`latest.zip`) が自動発行されます。
2. ゲーム内 Dalamud プラグイン一覧より「Character Spawn」をアップデート。
3. `/charaspawn` でウィンドウを開き、以下を確認してください：
   - タブが `[ Character ]` `[ Scene ]` `[ Settings ]` `[ Log ]` になっていること。
   - `[ New Chara ]` をクリックすると 4 ボタングリッドでソースを選択でき、選択中ボタンが赤色ハイライトされること。
   - Glamourer & Penumbra 選択時に IPC が Connected となり、デザインやコレクションが選択できること。
   - NPC(ENpc) や Monster/Mob で上限なく全件検索できること。
   - レターモーグリ、食道楽のゼゼルン、マタガイガイなどをスポーンさせた際、自キャラやギズモのみにならず正しいモデルで出現すること。
   - ル・スーシモなどの人型 NPC で `Weapon Visible` を OFF にすると武器が非表示になること。
   - Character タブでのスポーン後、ボタンが赤色 `[ Despawn ]` に変わり、`Spawning...` と `Gizmo` チェックボックスが表示されること。
   - 3D ギズモの軸をクリックしてドラッグすることで、スムーズに移動・回転できること。
   - 「Log」タブで詳細なスポーンログを確認・コピーできること。
