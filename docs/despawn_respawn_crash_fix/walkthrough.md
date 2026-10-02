# 修正内容の確認 (Walkthrough): デスポーン・再スポーン時クラッシュの完全防止

## 1. 修正概要
本バージョン（v0.1.26）では、キャラクターを一度デスポーンした後に再スポーンした際、あるいはプレビューを切り替えた際に発生していたゲームのクラッシュ（`0x12345679` / SEH RaiseException）を根本解決しました。
HDM（Hardware/Humanoid Puppet Master）の公式アーキテクチャに準拠し、ステール生ポインタの参照を完全撤廃するとともに、フレーム更新ループ全体に多重例外保護防壁を構築しました。

## 2. 実施した修正一覧

### 1) 生ポインタの完全撤廃と毎フレーム解決（HDM 方式）
- `readyJobs`, `pendingNpcJobs`, `monsterRedrawJobs` の各待機・ポーリング処理において、キャッシュされた生ポインタ `job.Spawned.NativeAddress` を直接参照する実装を廃止しました。
- Dalamud の `IObjectTable[job.GlobalIndex]` から毎フレーム最新の `ICharacter` を安全に解決し、`charaObj.Address != nint.Zero` であることを厳重に検証してからネイティブ処理を行うように改修しました。

### 2) 多重例外防壁（Unhandled Exception 漏洩の完全防止）
- Dalamud の Detour 境界を越えてプロセスを道連れに終了させていた原因を断つため、以下のメソッド全体を多重の `try-catch` で保護しました：
  - `Plugin.cs`: `DrawUI()`, `OnFrameworkUpdate()`
  - `ActorManager.cs`: `UpdateFrame()`, 各ジョブ個別ループ, `UpdateActorTransform()`, `ApplyActorAnimation()`, `ApplyTargetable()`

### 3) ライフサイクル状態フラグ `IsReady` の導入
- スポーン直後の描画準備中（ベースライン実体化前や過渡状態）のアクターに対して 3D ギズモや座標変更処理がアクセスしないよう、外見確定まで `IsReady = false` に維持する保護ガードを導入しました。

### 4) デスポーン時のジョブクリーンアップ漏れの解消
- `DespawnCharacter` 時に `pendingNpcJobs` が破棄対象から漏れていた問題を修正し、デスポーン直後のアクターに対する Glamourer の残留ポーリングを完全に阻止しました。

## 3. テスト・検証手順
1. **スポーン・デスポーンの反復テスト**:
   - キャラクター（Ruma等）をプレビュー生成（Spawn）します。
   - [Despawn] ボタンを押して消去します。
   - すぐに再度 [Spawn] ボタンを押します。
   - クラッシュせずに、直ちに正常な外見（Penumbra コレクション・MOD 適用状態）で再スポーンされることを確認します。
2. **プレビュー切り替えテスト**:
   - 別のキャラクターや NPC、モンスターを順次選択してスポーンします。
   - ゲームが落ちることなく、安定して描画が切り替わることを確認します。
