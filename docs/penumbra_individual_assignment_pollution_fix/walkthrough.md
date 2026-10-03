# ウォークスルー: Penumbra 個別設定リスト（Individual Assignments）汚染防止改修

## 1. 変更概要
Penumbra の `Collections` → `Individual Assignments` に Character Spawn 由来の一時アクター設定（特に赤色の `[Use No Mods]` カード）が蓄積・残存してしまう不具合を解消しました。
既存のユーザー設定を誤削除しないよう、過去分の一括削除は行わず、**「今後のスポーン・デスポーンにおいて、Penumbra のリストに一切ゴミを残さず、Hide 時に自動的かつ確実に完全削除される安全なライフサイクル」** を確立しました。

---

## 2. 変更内容一覧

| ファイル | 変更内容 |
|---|---|
| `Services/PenumbraIpc.cs` | `UnassignCollectionForActor` から `Guid.Empty` の呼び出しを完全撤廃。<br>`setCollectionForObjectV5NullableGuid` を `(actorIndex, null, allowCreateNew: false, allowDelete: true)` で呼び出し、カード削除のみを実行するように修正。<br>`actorIndex <= 0` ガードを追加し、自キャラ（Index 0）の Penumbra 設定を物理保護。 |
| `Managers/ActorManager.cs` | `RevertLocalPlayer()` から `penumbraIpc.UnassignCollectionForActor(0)` の呼び出しを削除し、自キャラのコレクション設定への不要な干渉を完全撤廃。 |

---

## 3. 実機検証手順

### 検証 1: デスポーン時のカード完全削除（汚染防止）
1. Penumbra の `Collections` → `Individual Assignments` を開く。
2. Character Spawn で任意のキャラクター（例: Chonk）をスポーン（Show）させる。
3. Penumbra のリストにアクターのカードが表示され、指定コレクションが割り当てられていることを確認。
4. Character Spawn で [Hide]（デスポーン）をクリック。
5. Penumbra のリストから該当カードが**即座に自動削除**され、**`[Use No Mods]` のカードが残らないこと**を確認。

### 検証 2: 自キャラ（LocalPlayer）の保護確認
1. 自キャラに Penumbra で特定のコレクションを割り当てておく。
2. Character Spawn でアクターのスポーン・デスポーンを複数回行う。
3. 自キャラの Penumbra 設定が一切勝手に解除・リセットされていないことを確認。
