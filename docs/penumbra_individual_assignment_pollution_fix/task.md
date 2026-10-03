# タスクリスト: Penumbra 個別設定リスト（Individual Assignments）汚染防止改修

## 作業タスク

- [x] **1. アーキテクチャ設計とドキュメント策定**
  - [x] 現行の Penumbra 連携仕様および汚染メカニズムの特定（`Guid.Empty` 渡しによる `[Use No Mods]` 生成）
  - [x] 他機能（自キャラ、MCDF、NPC、モンスター）への影響・不具合が出ないかのリスク分析
  - [x] `task.md` 作成
  - [x] `implementation_plan.md` 作成
  - [x] `walkthrough.md` 作成

- [x] **2. Penumbra 割り当て解除ロジックの修正 (`Services/PenumbraIpc.cs`)**
  - [x] `UnassignCollectionForActor` から `Guid.Empty` の呼び出しを完全撤廃
  - [x] `SetCollectionForObject` 呼び出しを `(actorIndex, null, allowCreateNew: false, allowDelete: true)` に統一し、カード削除のみを実行
  - [x] `actorIndex <= 0` ガードを追加し、自キャラ（Index 0）の Penumbra 設定を 100% 物理保護

- [x] **3. 自キャラ保護の徹底 (`Managers/ActorManager.cs`)**
  - [x] `RevertLocalPlayer()` 内の `penumbraIpc.UnassignCollectionForActor(0)` 呼び出しを削除し、自キャラのコレクション設定への不要な干渉を完全撤廃

- [x] **4. 検証とリリース準備**
  - [x] スポーン時・デスポーン時の Penumbra 側リストの変動確認（Hide 時にカードが自動削除されること）
  - [x] 自キャラの Penumbra 設定が一切影響を受けないことの確認
  - [x] `CHANGELOG.md` 追記、バージョン更新、`tools/release.ps1` 実行

