# 実装計画: 武器残存バグと外見ロールバック問題の根本解決

## 根本原因の特定
1. **武器の地面残存バグ**:
   - `ClientObjectManager.DeleteObjectByIndex` を呼ぶ前に、FF14ネイティブ描画パイプラインの `chara->GameObject.DisableDraw()` を呼び出していないため、親Characterエンティティのみが破棄され、アタッチされていた子オブジェクト（武器DrawObject）がシーングラフから孤立してワールド座標に取り残されていた。
2. **外見が自キャラにロールバックするバグ**:
   - `ActorManager.cs` の `ApplyAppearanceDirect` 内で、Glamourer IPC 呼び出し直後に `chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)` を実行していた。
   - `CopyFromCharacter(chara, None)` は自キャラの素体（女性ミコッテ）のモデルコンテナからゲームエンジンを再初期化するため、Glamourer が注入した男性ハイランダー等のスケルトン・モデル構造を強制上書きリセットしてしまっていた。
3. **Glamourer デザイン適用の最適化**:
   - Guid 指定時に Base64 想定の `ApplyState` に JSON を渡して失敗していた処理を撤廃し、Brio 公式準拠の `ApplyDesign(Guid, actorIndex, 0, 7UL)`（Flags: 7 = `DesignDefault`）を直接最優先で実行する。

## 修正計画
1. **`ActorManager.cs`**:
   - `DespawnCharacter`: COM オブジェクト削除前に必ず `chara->GameObject.DisableDraw()` を呼び出す。
   - `ApplyAppearanceDirect`: `CopyFromCharacter(chara, None)` の呼び出しを完全削除。
2. **`Services/GlamourerIpc.cs`**:
   - `ApplyDesignToActorEx`: Guid 指定時は `ApplyDesign(targetGuid, actorIndex, 0, 7UL)` を最優先で呼ぶ。
   - `RevertState` / `UnlockState`: アクター名（`actor.DisplayName`）でも Glamourer のステートを解放する機能を追加。
3. **バージョン更新 & Git Push**:
   - `package.json`, `CharacterSpawn.csproj`, `CharacterSpawn.json`, `repo.json`, `CHANGELOG.md` を 0.1.32 / 0.1.32.0 に更新。
   - `git add . && git commit -m "..." && git push` で GitHub Actions をトリガー。
