# タスクリスト: 外見適用およびモデルスポーンの根本改修 (v0.1.14)

## 概要
Glamourer design、Penumbra Collection、MCDF、NPC、モンスターのスポーン時に発生していた外見未反映（自キャラ化）およびギズモのみ表示となる不具合を、A Quest Reborn (AQR) および HDM の完全解析に基づき根本解決する。

## タスク一覧

- [x] **Penumbra InvalidIdentifier (ec=16) の完全解消**
  - [x] `Penumbra.GameData.dll` の `CreateBNpcFromObject` を CIL 逆アセンブルし、`OwnerId != 0xE0000000` の場合に親オブジェクト探索失敗で `InvalidIdentifier (16)` が返されることを解明
  - [x] `ActorManager.SpawnCharacter` で `nativeChara->GameObject.OwnerId = 0xE000_0000` を明示的に設定
  - [x] Penumbra の Player 識別子（name, homeWorld）判定ルートを通過させ、`SetCollectionForObject` の戻り値 `ec=0` (Success) を達成

- [x] **MCDF モーダルでの Penumbra Collection 選択 UI 追加**
  - [x] `UI/CharacterLibraryTab.cs` の MCDF 設定セクションに Penumbra Collection 選択コンボボックス（検索対応）を追加
  - [x] MCDF 外見と Penumbra Collection の同時バインドおよび保存・適用を実現

- [x] **バージョン更新・ドキュメント同期・リリース**
  - [x] バージョンを `0.1.14` / `0.1.14.0` に更新 (`package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`)
  - [x] `CHANGELOG.md` に詳細を追記
  - [x] `docs/appearance_and_model_spawn_fixes` のドキュメント更新
  - [x] Git コミット & プッシュ
  - [x] ローカル環境（XIVLauncher installedPlugins）への最新成果物配置
