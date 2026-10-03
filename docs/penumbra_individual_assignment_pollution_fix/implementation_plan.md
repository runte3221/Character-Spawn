# 実装計画: Penumbra 個別設定リスト（Individual Assignments）汚染防止改修

## 1. 課題と背景
Penumbra の `Collections` → `Individual Assignments`（キャラ別コレクション設定一覧）において、Character Spawn が生成した一時的なアクター（`Cs Aa`, `Csp Ag`, `Actor Bf` 等）が大量に登録され、その大半が赤色の `[Use No Mods]` としてリストに蓄積・残存してしまう現象（リストの汚染）が発生していた。

ユーザーの意図しない既存コレクション設定が誤削除されるリスクを避けるため、過去に生成されてしまった設定のクリーンアップはユーザーの手動操作に委ねるものとし、本改修では **「今後、Character Spawn がスポーン・デスポーンを繰り返しても、Penumbra のリストに一切ゴミを残さず、自動的に完全消去される仕組み」** を構築する。

---

## 2. 根本原因の特定
`Services/PenumbraIpc.cs` の `UnassignCollectionForActor(actorIndex)` において、以下の 2 つの IPC を連続で実行していた：
```csharp
// 1. null を渡して削除を試みる
setCollectionForObjectV5NullableGuid.InvokeFunc(actorIndex, null, true, true);

// 2. その直後に Guid.Empty を渡す
setCollectionForObjectV5Guid.InvokeFunc(actorIndex, Guid.Empty, true, true);
```
- Penumbra 公式 IPC の仕様：
  - `collectionId == null`: `collections.Active.RemoveIndividualCollection(idx)` により、個別設定カードを**リストから削除**する。
  - `collectionId == Guid.Empty`: `ModCollection.Empty`（Use No Mods）のコレクションを**割り当てる**。
  - `allowCreateNew == true`: 設定が存在しない場合、**新しいカードをリストに永続作成**する。
- このため、`null` でカードが削除された直後に、`Guid.Empty` によって **「Use No Mods」の新しいカードがリストに再作成・永続保存** されてしまっていた。
- スポーン前（初期化時）およびデスポーン時の両方でこの処理が走るため、スポーンや削除を行うたびに空の `[Use No Mods]` カードが増殖していた。

---

## 3. 実装方針と変更内容

### 3.1 `Services/PenumbraIpc.cs` の改修
1. **`Guid.Empty` の呼び出し完全撤廃**:
   - `setCollectionForObjectV5Guid` に `Guid.Empty` を渡す処理を完全に削除。
2. **`null` 渡しによるカード削除の確実化**:
   - `setCollectionForObjectV5NullableGuid.InvokeFunc(actorIndex, null, allowCreateNew: false, allowDelete: true)`
   - 新規作成を不許可（`allowCreateNew: false`）にし、削除のみを許可（`allowDelete: true`）することで、Penumbra 側で確実にカード削除（`RemoveIndividualCollection`）のみが走るように統一。
3. **自キャラ（LocalPlayer: Index 0）の物理保護ガード**:
   - `if (actorIndex <= 0) return false;` を追加し、自キャラの Penumbra 設定を絶対に操作しないように遮断。

### 3.2 `Managers/ActorManager.cs` の改修
1. **`RevertLocalPlayer()` の見直し**:
   - `penumbraIpc.UnassignCollectionForActor(0)` の呼び出しを削除。自キャラ（プレイヤー自身）が Penumbra で設定している個人コレクションやデフォルト設定を Character Spawn 側から誤ってリセット・削除するリスクを完全に排除。

---

## 4. 不具合・他機能への影響リスク検証

| パイプライン / 対象 | 影響と安全性 |
|---|---|
| **自キャラ (LocalPlayer: Index 0)** | **完全保護**。<br>`actorIndex <= 0` ガードおよび `RevertLocalPlayer` からの呼び出し削除により、自キャラの Penumbra 設定には Character Spawn が 100% 干渉しない。 |
| **MCDF アクター** | **影響なし（安全）**。<br>MCDF は Penumbra の一時コレクション（Temporary Collection）を使用しており、もともと Individual Assignments リストには登録されない。メモリ上からの解放も正常に動作。 |
| **通常アクター（Glamourer / 自キャラクローン）** | **改善（汚染解消）**。<br>Penumbra コレクション指定時、スポーン中は正常に Mod が適用され、Hide（デスポーン）した瞬間に Penumbra の Individual Assignments リストからカード自体が自動的に綺麗に消去される。`[Use No Mods]` のカードが残ることは一切なくなる。 |
| **モンスター / NPC** | **影響なし（安全）**。<br>Penumbra コレクション未指定のため、初期化時・削除時に不要なカードが作られず、Penumbra リストが常にクリーンに保たれる。 |

---

## 5. 検証手順
1. Penumbra の `Collections` → `Individual Assignments` を開く。
2. Character Spawn でアクター（Chonk 等の Penumbra コレクション指定アクター）をスポーン（Show）させる。
   - Penumbra 上に該当アクターのカードが 1 枚表示され、指定コレクション（例: `[Male-Chonk]`）が適用されていることを確認。
3. Character Spawn で [Hide] をクリック（デスポーン）。
   - Penumbra の一覧から、該当アクターのカードが**自動的に完全に消滅（削除）**することを確認。
   - **`[Use No Mods]` のカードが残らないこと**を確認。
4. 自キャラのコレクション設定が一切影響を受けていないことを確認。
