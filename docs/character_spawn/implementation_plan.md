# 実装計画書: Character Library UI刷新 & 外見・検索・ファイル選択不具合修正

ユーザーからの要望に基づき、Character Libraryのレイアウトをフォルダ階層構造（ツリービュー）に刷新し、判明している各不具合（Glamourer/Penumbra選択UI、Monster/NPC検索上限、NPC外見の反映、MCDFファイル選択ダイアログ）を根本解決します。

---

## 1. ユーザー要望と改修方針

### (1) Character Library レイアウトの刷新（提供画像準拠）
- **左ペイン（フォルダツリー）**:
  - フォルダ・キャラクターの階層ツリービュー（開閉可能、フォルダ・キャラクターアイコン表示）。
  - 下部ボタン：`[New Chara]`, `[New Folder]`, `[Delete]`。
- **右ペイン（選択キャラクター情報）**:
  - `Chara Name` 表示・インライン編集。
  - アクションボタン：`[Spawn]`（マップ召喚）、`[edit]`（編集モーダル呼び出し）、`[delete]`（削除）。
- **「New Chara」ポップアップ・モーダル**:
  - `New Chara` ボタン押下で専用の作成ウィンドウ（またはモーダル）を表示。
  - 外見ソース選択（Glamourer & Penumbra / Monster / NPC / MCDF / Player Clone）。
  - `[Save to Chara]` でツリー内の現在選択中フォルダ（またはルート）に保存。

### (2) Glamourer & Penumbra 選択方法の改善（AQR準拠）
- 文字列の手動入力ではなく、Glamourer IPCから取得したデザイン一覧（`Dictionary<Guid, string>`）およびPenumbra IPCから取得したコレクション一覧をドロップダウン（検索フィルタ付きコンボ）で選択可能にする。

### (3) Monster / NPC 検索上限の拡大
- 現在10件に制限されていた `maxResults` を撤廃・大幅拡大（100件以上＋スクロールリスト化）。

### (4) NPC (ENpc) 外見が自キャラになってしまう不具合の修正
- `ENpcBase` から `ModelCharaId`、`CustomizeData`（人型NPCの髪型・顔・肌色等）、装備モデルIDを取得し、アクター生成時に適用。
- モーグリなどの非人型NPCだけでなく、ミューヌなどの人型NPCも完全に本来の姿でスポーンするように修正。

### (5) MCDF ファイル選択ダイアログ（エクスプローラー連携）
- Win32 API (`comdlg32.dll` の `GetOpenFileNameW`) によるファイル選択ダイアログを実装し、「Browse...」ボタンから `.mcdf` ファイルをエクスプローラーで選択できるようにする。

---

## 2. 変更対象ファイル

1. **`Models/CharacterModels.cs`**:
   - `CharacterTemplate` に `FolderPath`、`NpcEquipmentIds` などの外見保持フィールドを追加。
2. **`Configuration.cs`**:
   - `Folders`（フォルダパスの永続化リスト）を追加。
3. **`Services/GameDataService.cs`**:
   - `SearchNpcs`, `SearchMonsters` の上限緩和。
   - `ENpcBase` の詳細データ（Customize, Equipment）抽出メソッドを追加。
4. **`Services/FilePicker.cs` (新規)**:
   - Win32 ネイティブファイルオープンダイアログの実装。
5. **`Managers/ActorManager.cs`**:
   - NPC外見（`ENpcBase` 由来のモデル・カスタマイズ・装備）の正確なアクター適用処理を追加。
6. **`UI/CharacterLibraryTab.cs`**:
   - フォルダツリー ＋ 詳細パネル ＋ 新規作成モーダルの新UIに全面刷新。
   - AQR準拠のGlamourerデザイン・Penumbraコレクション選択UIを実装。
7. **バージョン管理・メタデータ**:
   - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json` (v0.1.8)
   - `CHANGELOG.md`
   - `docs/character_spawn/task.md`, `walkthrough.md`
