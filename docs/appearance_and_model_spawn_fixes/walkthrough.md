# 改修内容の確認 (Walkthrough) - v0.1.14

## 概要
本バージョンでは、`Penumbra.GameData.dll` の内部処理を CIL 逆アセンブルによって詳細解析し、Penumbra の `SetCollectionForObject` が `InvalidIdentifier (ec=16)` で拒否されていた真の根本原因を特定・解消しました。また、MCDF 設定画面に Penumbra Collection 選択コンボボックスを追加し、MCDF 外見と Mod コレクションの同時適用を実現しました。

---

## 修正内容のハイライト

### 1. Penumbra InvalidIdentifier (ec=16) の解明と OwnerId 設定 (`Managers/ActorManager.cs`)
- **問題**: Penumbra Collection（`[Male-Chonk]` 等）を選択してスポーンさせても、`ec=16` で失敗しコレクションが反映されなかった。
- **原因**: 
  - `Penumbra.GameData.dll` の `CreateBNpcFromObject` の CIL コードを解析した結果、Penumbra はアクターの `OwnerId` を参照しており、`OwnerId != 0xE0000000` の場合は親オブジェクトの探索（`objects.ById(ownerId)`）を試みることが判明。
  - `CreateBattleCharacter` で新規生成された BattleNpc の `OwnerId` は `0` で初期化されているため、親が見つからず直ちに `InvalidIdentifier` (16) で全リクエストが弾かれていた。
- **対策**:
  - `ActorManager.SpawnCharacter` において、`nativeChara->GameObject.OwnerId = 0xE000_0000;` を明示的に設定。
  - これにより親探索をバイパスし、`nameId == 0` かつ `puppetName`、`HomeWorld` から正規の Player 識別子が生成され、`SetCollectionForObject` が `ec=0` (Success) で完全に成功するようになりました。

### 2. MCDF セクションへの Penumbra Collection 選択 UI 追加 (`UI/CharacterLibraryTab.cs`)
- **問題**: MCDF でスポーンさせると外見データ（Glamourer）は正常に反映されるが、Penumbra Collection を選ぶことができず反映されない。
- **原因**: MCDF のモーダル設定画面に Penumbra Collection を選択する UI（コンボボックス）が存在しなかったため、テンプレート保存時にコレクション名が空文字になっていた。
- **対策**:
  - `DrawPenumbraCollectionSelector()` を抽出し、MCDF セクション内にも Penumbra Collection 選択コンボボックス（検索付き）を配置。
  - MCDF の外見と Penumbra Collection の双方を同時に保存・適用できるようになりました。

---

## 変更されたファイル一覧
- `package.json` (0.1.14 に更新)
- `CharacterSpawn.json` (0.1.14.0 に更新)
- `CharacterSpawn.csproj` (0.1.14.0 に更新)
- `repo.json` (0.1.14.0 に更新)
- `CHANGELOG.md` (0.1.14 リリースノート追加)
- `Managers/ActorManager.cs` (OwnerId = 0xE0000000 明示初期化)
- `UI/CharacterLibraryTab.cs` (MCDF セクションへの Penumbra Collection 選択 UI 追加)
- `docs/appearance_and_model_spawn_fixes/` (task.md, implementation_plan.md, walkthrough.md 同期)

