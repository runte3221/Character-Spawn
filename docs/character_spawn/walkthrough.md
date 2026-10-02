# Character Spawn v0.1.8 改修ウォークスルー

## 概要

本アップデート (v0.1.8) では、ユーザーから提供されたレイアウト設計（フォルダ階層ツリー構造＋右側詳細パネル＋作成/編集モーダル）に沿って **Character Library UI を全面刷新** し、併せて報告されていた **4 件の主要な不具合・改善要望** をすべて解消しました。

---

## 修正・実装内容の詳細

### 1. Character Library レイアウトの刷新（提供画像準拠）
- **左ペイン（フォルダ・キャラ階層ツリー）**:
  - ルート直下のキャラクターおよび作成したフォルダ（`📁 FolderName`）をツリービュー（`TreeNodeEx`）で階層表示。
  - フォルダの開閉展開および配下キャラクターの選択（ハイライト表示）に対応。
  - 下部に `[New Chara]`、`[New Folder]`、`[Delete]` ボタンを均等配置。
- **右ペイン（選択キャラクター詳細パネル）**:
  - 選択中のキャラクター名をインライン表示・即時編集（テキストボックス入力で即座に設定ファイルへ反映）。
  - `[Spawn]`、`[edit]`、`[delete]` の 3 アクションボタンを配置。
  - 外見タイプ（Monster/NPC/Glamourer等）、フォルダ、モデルID、装備保持状況などの詳細サマリーを表示。
- **New Chara / Edit Chara モーダルウィンドウ**:
  - `[New Chara]` または `[edit]` をクリックした際に開く専用ポップアップダイアログ。
  - キャラ名入力、所属フォルダ選択コンボボックス、外見ソース選択、および各ソース固有の詳細設定を集約。
  - 下部に `[Save to Chara]` と `[Cancel]` ボタンを設置。

### 2. Glamourer & Penumbra 選択の改善（AQRスタイル）
- **Glamourer Design ドロップダウン**:
  - `Glamourer.GetDesignList` IPC から取得した保存済みデザイン一覧を検索テキストフィルター付きのコンボボックスで選択可能に。
  - デザイン名を選択すると自動でキャラ名にも反映され、Guid が設定される。直接の文字列入力にも対応。
- **Penumbra Collection ドロップダウン**:
  - `Penumbra.GetCollections` IPC から取得したコレクション一覧を検索テキストフィルター付きコンボボックスで選択可能に。

### 3. Monster / Mob 検索件数の上限緩和
- これまで 10 件固定で絞り込まれていた制限を撤廃し、最大 500 件まで検索結果を取得可能に拡張。
- 縦 160px のスムーズなスクロールリストボックスで多数のモンスターを快適に閲覧・選択可能。

### 4. NPC (ENpc) 検索上限緩和 & スポーン時の外見不具合解消
- **検索件数上限の拡大**: 最大 500 件まで検索結果を取得可能に。
- **自キャラの姿でスポーンしてしまう問題の根本修正**:
  - **非人型NPC（レターモーグリ等）**: `ModelCharaId > 0` の場合、`nativeChara->ModelContainer.ModelCharaId` にモデルIDを設定し、`CopyFromCharacter(nativeChara, CharacterCopyFlags.None)` で Native モデルを更新。
  - **人型NPC（ミューヌ等）**: `ENpcBase` から抽出した 26 バイトの `CustomizeData`（種族、性別、顔、髪型、肌色等）および `NpcEquip` / `ENpcBase` から取得した 10 スロットの装備モデルID（Head, Body, Hands, Legs, Feet 等）を `nativeChara->DrawData.CustomizeData` と `EquipmentModelIds` に直接コピーして適用。

### 5. MCDF ファイル選択のエクスプローラー連携
- 手動のテキストパス入力を改善し、`[Browse...]` ボタンを新設。
- Windows の `comdlg32.dll`（`GetOpenFileNameW` API）を STA バックグラウンドスレッドで起動し、ゲームのフレーム描画を停止させることなくネイティブの「ファイルを開く」ダイアログで `.mcdf` ファイルを選択可能に。
- ファイル選択と同時に自動でアーカイブをパースし、含まれる Glamourer デザインを抽出。

---

## 変更ファイル一覧

| ファイル | 変更概要 |
| :--- | :--- |
| `Services/FilePicker.cs` (新規) | Win32 `GetOpenFileNameW` によるネイティブファイル選択ダイアログの実装 |
| `Services/GameDataService.cs` | 検索件数上限を 500 件に拡張、ENpcBase からの CustomizeData (26B) および装備抽出 |
| `Managers/ActorManager.cs` | スポーン時の NPC 外見適用処理（非人型 ModelCharaId、人型 CustomizeData & 装備モデルID） |
| `UI/CharacterLibraryTab.cs` | 提供画像に合わせた階層ツリー・詳細・モーダル UI の全面刷新 |
| `Models/CharacterModels.cs` | `FolderPath`、`NpcEquipmentModelIds` フィールドの追加 |
| `Configuration.cs` | 空フォルダ保持用 `Folders` リストの追加 |
| `package.json` | バージョンを `0.1.8` に更新 |
| `CharacterSpawn.json` | `AssemblyVersion` を `0.1.8.0` に更新 |
| `CharacterSpawn.csproj` | `Version`, `AssemblyVersion`, `FileVersion` を `0.1.8.0` に更新 |
| `repo.json` | `AssemblyVersion` を `0.1.8.0` に更新 |
| `CHANGELOG.md` | v0.1.8 リリースノートを追記 |
| `docs/character_spawn/task.md` | タスク完了状態に更新 |

---

## 次のステップ（ユーザーによるゲーム内動作確認）

1. GitHub リポジトリへプッシュ後、GitHub Actions CI により最新ビルド (`latest.zip`) が Releases に自動発行されます。
2. ゲーム内 Dalamud プラグイン一覧より「Character Spawn」をアップデート（または再読み込み）。
3. `/charaspawn` でウィンドウを開き、以下を確認してください：
   - Character Library タブが左ツリー＋右詳細パネルの構成になっていること。
   - `[New Chara]` でモーダルが開き、Glamourer/Penumbra ドロップダウン、Monster/NPC の多数スクロールリスト、MCDF のエクスプローラー選択ができること。
   - レターモーグリやミューヌをスポーンさせた際、自キャラにならず本来の姿で出現すること。
