# 実装計画: 外見適用およびモデルスポーンの根本改修 (v0.1.14)

## 1. 課題と根本原因の特定

### A. Penumbra Collection が適用されない (ec=16 / InvalidIdentifier)
- **原因**: `Penumbra.GameData.dll` の `CreateBNpcFromObject` を CIL 逆アセンブルした結果、Penumbra はアクターの `OwnerId` を検査し、`OwnerId != 0xE0000000` (`GameObject.InvalidGameObjectId`) の場合は親オブジェクトの探索（`objects.ById(ownerId)`）を行うことが判明。`CreateBattleCharacter()` で生成された GameObject は `OwnerId` が `0` で初期化されているため、親が見つからずに `InvalidIdentifier` (16) を返し、コレクションの割り当てが失敗していた。
- **対策**: スポーン直後に `nativeChara->GameObject.OwnerId = 0xE000_0000` を明示的に代入。これにより Penumbra は親オブジェクト探索をスキップし、`nameId == 0` かつ `puppetName`、`HomeWorld` から正規の Player 識別子を生成し、コレクション割り当てが `ec=0` (Success) で成功する。

### B. MCDF でスポーンさせると Penumbra Collection が読み込まれない
- **原因**: `UI/CharacterLibraryTab.cs` の MCDF モーダルセクションに Penumbra Collection を選択する UI が存在しなかったため、テンプレート保存時に `PenumbraCollectionName` が空文字のまま保存されていた。
- **対策**: MCDF モーダルセクションに Penumbra Collection 選択コンボボックス（検索機能付き）を追加し、MCDF の外見と Penumbra Collection の同時バインドおよび保存・適用を可能にする。

---

## 2. アーキテクチャ改修方針

### 1. アクターの OwnerId 明示初期化 (`Managers/ActorManager.cs`)
- `SpawnCharacter` において、`nativeChara->GameObject.OwnerId = 0xE000_0000;` を設定。
- Penumbra の `CreateBNpcFromObject` の Player 識別子生成パスを確実に通過させる。

### 2. MCDF セクションの Penumbra コレクションセレクター共通化 (`UI/CharacterLibraryTab.cs`)
- `DrawPenumbraCollectionSelector()` を抽出し、Glamourer セクションおよび MCDF セクションの双方から呼び出し可能にする。

---

## 3. 検証・品質保証
- `CreateBNpcFromObject` の CIL 逆アセンブル検証による `OwnerId` 境界条件の証明。
- MCDF モーダルにおける Penumbra Collection 選択およびテンプレート保存・反映の整合性確認。
