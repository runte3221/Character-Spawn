# 実装計画: パペット名キャッシュ分離と完全ステートリセット (v0.1.30)

## 概要
スポーン・デスポーンを繰り返した際に過去のキャラクターや削除済みキャラクターの外見・骨格が混入する問題を根本から解消する。

## 修正詳細

### 1. アクター名の一意化 (`Managers/ActorManager.cs`)
- 旧: `NextPuppetName()` による `Csp Aa`, `Csp Ab`... のインクリメント方式。
  - プラグインリロード時にシリアルがリセットされ、過去のキャラ名と再衝突していた。
- 新: `GetPuppetName(CharacterTemplate template)`
  ```csharp
  var suffix = template.Id.ToString("N")[..8];
  return $"Csp {suffix}";
  ```
  - テンプレートの Guid に基づき、キャラクターごとに固有かつ不変のパペット名を割り当てる。
  - これにより、Glamourer や Penumbra が名前単位で保持するキャッシュが他キャラと混ざる可能性を完全に排除。

### 2. Glamourer IPC 拡張 (`Services/GlamourerIpc.cs`)
- `RevertState(int actorIndex)`:
  - `Glamourer.RevertState` / `Glamourer.RevertToAutomation` を呼び出し、アクターを初期状態にリセット。
- `UnlockState(int actorIndex)`:
  - `Glamourer.UnlockState` を呼び出し、ステートのロックを解除。

### 3. デスポーン時・外見適用時の完全リセット (`Managers/ActorManager.cs`)
- `DespawnCharacter`:
  - `glamourerIpc.UnlockState(actor.GlobalIndex)`
  - `glamourerIpc.RevertState(actor.GlobalIndex)`
  - `penumbraIpc.UnassignCollectionForActor(actor.GlobalIndex)`
  - `customizePlusIpc.DeleteTemporaryProfileOnCharacter(actor.GlobalIndex)`
- `ApplyAppearanceDirect`:
  - 冒頭で `UnlockState`, `UnassignCollectionForActor`, `DeleteTemporaryProfileOnCharacter` を呼び出し、完全クリーンな状態から新しい外見・コレクション・プロファイルを適用。
- `ApplyCustomizePlusProfile`:
  - プロファイル指定の有無に関わらず冒頭で `DeleteTemporaryProfileOnCharacter` を呼び出し、未指定キャラに前回のプロファイルが残存しないことを保証。

### 4. ユーザー設定のクリーンアップ (`CharacterSpawn.json`)
- `Ruma` テンプレートの `CustomizePlusProfileGuid` と `CustomizePlusProfileName` を `null` に初期化。

### 5. バージョン更新・デプロイ
- バージョン `0.1.30` / `0.1.30.0`
- GitHub リポジトリへのプッシュ（GitHub Actions 自動ビルド）
- `repo.json` の更新
