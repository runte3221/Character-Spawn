# 修正内容の確認 (Walkthrough): Customize+ Profile 連携および UI 整理 (v0.1.17)

## 実施した変更
1. **UI 整理**:
   - `CharacterLibraryTab.cs` のキャラクタ追加・編集モーダルから不要な `Or Direct Design String / Code:` 複数行テキスト入力を削除。
   - デザイン選択（Glamourer Design）、Mod コレクション（Penumbra Collection）、体型・ボーン調整（Customize+ Profile）が整然と並ぶシンプルな UI に整理。
2. **Customize+ IPC サービス追加 (`Services/CustomizePlusIpc.cs`)**:
   - Customize+ の API v6+ IPC（`CustomizePlus.General.GetApiVersion`, `CustomizePlus.Profile.GetList`, `CustomizePlus.Profile.GetByUniqueId`, `CustomizePlus.Profile.SetTemporaryProfileOnCharacter`, `CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId`）に対応した IPC サービスを新規実装。
   - キャッシュ付きプロファイル一覧取得機能および一時プロファイルの適用・破棄メソッドを提供。
3. **データモデル拡張 (`Models/CharacterModels.cs`)**:
   - `CharacterTemplate` に `CustomizePlusProfileGuid` および `CustomizePlusProfileName` を追加。
   - `SpawnedActorData` に `TemporaryCustomizePlusGuid` を追加。
4. **MCDF 連携の拡張 (`Services/McdfParser.cs`)**:
   - MCDF アーカイブ内の `CustomizePlusData` をパースし、手動プロファイル未指定時でも MCDF 内包の C+ データを自動適用可能に。
5. **アクター管理連携 (`Managers/ActorManager.cs`)**:
   - `ApplyAppearanceDirect` で、テンプレートに指定された Customize+ プロファイル（または MCDF 内包データ）をアクターへ一時適用。
   - `DespawnCharacter`、`DespawnAll`、`OnTerritoryChanged` で一時プロファイルを自動解除。
6. **UI セレクター追加 (`UI/CharacterLibraryTab.cs`)**:
   - キャラクタ追加・編集モーダルの `Glamourer&Penumbra` セクションおよび `MCDF` セクションに `Customize+ Profile:` 選択ドロップダウンを追加。
   - プロファイル検索（名前 / 仮想フォルダパス）対応。
   - 右ペインのテンプレート詳細情報にも設定中プロファイル名を表示。
7. **バージョン更新**:
   - `v0.1.17` / `0.1.17.0` に更新。
