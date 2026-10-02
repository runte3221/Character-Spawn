# 実装計画: デスポーン・再スポーン時クラッシュの完全防止 (HDM ライフサイクル準拠)

## 1. 概要
キャラクターをデスポーンした後に再度スポーン（またはプレビュー切り替え）した際にゲームが `0x12345679` でエラー落ちする重大な不具合を解消するため、HDM（Humanoid Puppet Master - Enceladeum）の設計仕様に準拠した堅牢なオブジェクトライフサイクル管理と例外防壁を導入します。

## 2. 根本原因の特定
- **未処理 C# 例外のネイティブ境界漏洩**:
  `Framework.Update` イベントハンドラから呼び出される `actorManager.UpdateFrame()` 内で、オブジェクトが破棄・過渡状態にある際にアクセス違反や例外が発生し、マネージド例外が Dalamud のネイティブ Detour に伝播してプロセスが保護終了（RaiseException）していた。
- **ステール生ポインタの参照**:
  `job.Spawned.NativeAddress` というキャッシュされた生ポインタをデリファレンスしていた。COM（ClientObjectManager）によるオブジェクトの破棄・再生成において、同一スロットのメモリが過渡状態にある場合、生ポインタの参照は未定義動作を引き起こす。
- **ジョブクリーンアップ漏れ**:
  `DespawnCharacter` 時に `pendingNpcJobs` が破棄されておらず、破棄済みアクターに対する Glamourer ポーリングが継続していた。

## 3. 実装詳細

### A. `SpawnedActorData.cs` / `CharacterModels.cs`
- `IsReady` プロパティを追加。
- スポーン直後は `false`。外見確定（Appearance finalized）および描画準備完了後に `true` とし、デスポーン時に `false` へリセット。

### B. `Plugin.cs`
- `DrawUI()` および `OnFrameworkUpdate()` 全体を `try-catch` で完全保護。
- 3D ギズモの描画対象条件に `targetActor.IsReady` を追加し、描画準備中のアクターに対するギズモ描画・トランスフォーム更新を遮断。

### C. `ActorManager.cs`
1. **HDM 黄金律の実装**:
   `readyJobs`, `pendingNpcJobs`, `monsterRedrawJobs` の各ループにおいて、生ポインタを一切信用せず、毎フレーム `objectTable[job.GlobalIndex]` から最新の `ICharacter` を安全に解決。
   `charaObj.Address == nint.Zero` の場合は安全にスキップ。
2. **多重例外保護**:
   `UpdateFrame()` 全体、各ジョブの個別反復、およびネイティブ構造体アクセス（`IsReadyToDraw`, `EnableDraw`, `IsVisible` 等）をすべて個別の `try-catch` で防壁化。
3. **`DespawnCharacter` の完全化**:
   `pendingNpcJobs.RemoveAll` を追加し、`actor.IsReady = false` を設定。
4. **トランスフォーム安全更新**:
   `UpdateActorTransform` において、`actor.IsReady` の確認および `objectTable` からの最新検証を追加。

## 4. 検証手順
1. キャラクター画面で任意のキャラクター（Ruma等）をプレビュー生成する。
2. [Despawn] ボタンを押して消去する。
3. すぐに再度 [Spawn] ボタンを押して再スポーンする。
4. ゲームがクラッシュせず、正常にキャラクターが描画・配置されることを確認する。
5. 複数回のスポーン・デスポーンおよびプレビュー切り替えで安定動作を確認する。
