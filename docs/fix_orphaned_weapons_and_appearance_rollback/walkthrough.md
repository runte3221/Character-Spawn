# 修正内容の確認 (Walkthrough): v0.1.32.0

## 修正の要約

### 1. 武器のマップ上孤立・残存バグの撲滅 (`Managers/ActorManager.cs`)
- `DespawnCharacter` 冒頭において、COM からオブジェクトを消去する直前に `chara->GameObject.DisableDraw()` を確実に実行。
- これにより、子描画オブジェクトである武器モデル（マンダヴィル・ガンブレード等）が FF14 描画パイプラインから安全にアンロードされ、地面に武器だけが突き刺さって残る不具合が根絶されました。

### 2. 男性キャラ・異種族キャラが自キャラ（女性ミコッテ）に戻る問題の解消 (`Managers/ActorManager.cs`)
- `ApplyAppearanceDirect` で実行されていた `chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)` を完全削除。
- Brio および HDM の標準設計に準拠し、Glamourer が注入したスケルトンとモデルをそのままゲームエンジンに描画させることで、自キャラへのロールバックを防止しました。

### 3. Glamourer デザイン適用の最適化 & GZip Base64 強制ステート注入 (`Services/GlamourerIpc.cs`)
- **`Race: Apply = False` バグの解消**:
  - `Chonk`（`Kimo-1-Default`）のデザインファイルでは `Race: Apply = False` に設定されており、Guid による通常適用では Race がミコッテ女性のまま維持され、Clan（ハイランダー）と Gender（男性）のみ適用されて種族不一致エラーとなり、ゲームエンジンが自キャラに巻き戻していました。
  - デザイン JObject から `ForceAllApply` を実行し、`Race` を含む全スロットの `Apply` を強制的に `true` に書き換えた上で、Glamourer ネイティブ仕様の GZip 圧縮 Base64 文字列（`H4sI...`）にエンコード。
  - `Glamourer.ApplyState(compressedBase64, actorIndex, 0, 7UL)` を通じて全スロットを 100% 確実に強制注入・変身させるように根本改修しました。

### 4. 前キャラの外見情報の残留防止
- デスポーン時に GlobalIndex だけでなくアクター名（`actor.DisplayName`）でも Glamourer ステートを `RevertStateName` / `UnlockStateName` で解放し、同一インデックス再利用時のステート混ざりを解消。
