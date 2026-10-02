# 実装計画: Customize+ Profile 連携および UI 整理 (v0.1.17)

## 1. ユーザー要望
1. `Or Direct Design String / Code:` の欄は直接入力を行わないため、項目自体を非表示にする。
2. Customize+ (https://github.com/XIV-Tools/CustomizePlus) の Profile を選択し、スポーン時に反映できるようにする。

## 2. 設計方針
### A. UIクリーンアップ
- `UI/CharacterLibraryTab.cs` の `DrawModalGlamourerSection` 内にある `Or Direct Design String / Code:` ラベルおよび `ImGui.InputTextMultiline("##CustomGlamString", ...)` を削除。
- デザイン選択コンボボックスから `customGlamourerString` に GUID が代入されるため、外見文字列の保存・適用機能には影響を与えない。

### B. Customize+ IPC 実装 (`Services/CustomizePlusIpc.cs`)
- CustomizePlus の API v6+ IPC インターフェースを利用：
  - `CustomizePlus.General.GetApiVersion` -> `(int Breaking, int Feature)`
  - `CustomizePlus.Profile.GetList` -> `IList<(Guid, string, string, List<(string, ushort, byte, ushort)>, int, bool)>`
  - `CustomizePlus.Profile.GetByUniqueId` -> `(int ErrorCode, string? profileJson)`
  - `CustomizePlus.Profile.SetTemporaryProfileOnCharacter` -> `(int ErrorCode, Guid? uniqueId)`
  - `CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId` -> `int ErrorCode`
  - `CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter` -> `int ErrorCode`
- IPC の利用可否フラグ `IsAvailable`、キャッシュ付きプロファイル一覧取得 `GetProfiles()`、プロファイル適用・解除メソッドを提供。

### C. データモデルの拡張 (`Models/CharacterModels.cs`)
- `CharacterTemplate`:
  - `CustomizePlusProfileGuid`: 選択された C+ プロファイルの GUID (string?)
  - `CustomizePlusProfileName`: 選択された C+ プロファイルの名前 (string?)
- `SpawnedActorData`:
  - `TemporaryCustomizePlusGuid`: スポーン時に割り当てられた一時プロファイルの GUID (Guid?)

### D. アクターライフサイクル連携 (`Managers/ActorManager.cs`)
- スポーン時（`ApplyAppearanceDirect`）:
  - `template.CustomizePlusProfileGuid` が指定されていれば、`customizePlusIpc.SetTemporaryProfileByGuid(actorIndex, guid)` を呼び出し、結果の一時プロファイル GUID を `spawned.TemporaryCustomizePlusGuid` に保持。
- デスポーン時（`DespawnCharacter` / `DespawnAll` / `OnTerritoryChanged`）:
  - `spawned.TemporaryCustomizePlusGuid` を通じて一時プロファイルを削除・クリーンアップ。

### E. UI への Profile 選択セレクター追加 (`UI/CharacterLibraryTab.cs`)
- `Glamourer&Penumbra` セクションおよび `MCDF` セクションの後に `Customize+ Profile` ドロップダウンを追加。
- 接続状態ステータス表示（`Customize+ IPC: Connected`）とプロファイル選択 Combo を描画。
