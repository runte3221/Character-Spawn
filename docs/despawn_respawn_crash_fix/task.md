# タスクリスト: デスポーン・再スポーン時クラッシュ修正 (HDM ライフサイクル完全準拠)

## 状況・課題
- **現象**:
  - キャラクター（Ruma等）の初回スポーンは Penumbra コレクション・MOD が完璧に適用されて描画される。
  - しかし、一度デスポーンした後に再度スポーン（またはプレビュー切り替え）を行うと、ゲームが即座にエラー落ち（クラッシュ・CTD）する。
- **エラー分析**:
  - クラッシュコード: `0x12345679` (Dalamud のマネージド未処理例外保護による RaiseException)。
  - スタックトレース: `Framework.Update` -> `actorManager.UpdateFrame()` 内で発生。
  - 根本原因:
    1. `readyJobs`, `pendingNpcJobs`, `monsterRedrawJobs` で `job.Spawned.NativeAddress` というステール生ポインタをデリファレンスしていた。
    2. `UpdateFrame()` や各ジョブ処理が `try-catch` で保護されておらず、CLR 例外が Detour 境界を越えてプロセス強制終了を引き起こしていた。
    3. `DespawnCharacter()` で `pendingNpcJobs` のクリーンアップが漏れていた。
    4. 描画準備完了前（過渡状態）のアクターに対して 3D ギズモや Transform 更新が走るリスクが存在していた。

## タスク一覧
- [x] `SpawnedActorData` に `IsReady` ライフサイクル管理フラグを追加
- [x] `Plugin.cs` の `DrawUI()` および `OnFrameworkUpdate()` を `try-catch` で完全保護し、`targetActor.IsReady` を確認するように改修
- [x] `ActorManager.cs` の `UpdateActorTransform()`, `ApplyActorAnimation()`, `ApplyTargetable()` に `objectTable` 検証と `try-catch` を実装
- [x] `ActorManager.cs` の `DespawnCharacter()` に `actor.IsReady = false` および `pendingNpcJobs.RemoveAll` を追加
- [x] `ActorManager.cs` の `UpdateFrame()` を HDM 準拠（生ポインタ完全撤廃、毎フレーム `objectTable[job.GlobalIndex]` 解決、`try-catch` 二重保護）に改修
- [x] 各種外見確定パス（`ApplyAppearanceDirect`、MCDFパス、モンスター再描画、人型NPCポーリング）完了時に `IsReady = true` を設定
- [x] バージョンを `0.1.26` / `0.1.26.0` に更新
- [x] `CHANGELOG.md` 更新
- [x] Git コミット & プッシュ
- [x] CI ビルド完了確認とローカル `installedPlugins` への反映
- [ ] ユーザーによる動作確認（スポーン → デスポーン → 再スポーンの繰り返し検証）
