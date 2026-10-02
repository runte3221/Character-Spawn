# 修正内容の確認 (Walkthrough): v0.1.32.0

## 修正の要約

### 1. 武器のマップ上孤立・残存バグの撲滅 (`Managers/ActorManager.cs`)
- `DespawnCharacter` 冒頭において、COM からオブジェクトを消去する直前に `chara->GameObject.DisableDraw()` を確実に実行。
- これにより、子描画オブジェクトである武器モデル（マンダヴィル・ガンブレード等）が FF14 描画パイプラインから安全にアンロードされ、地面に武器だけが突き刺さって残る不具合が根絶されました。

### 2. 男性キャラ・異種族キャラが自キャラ（女性ミコッテ）に戻る問題の解消 (`Managers/ActorManager.cs`)
- `ApplyAppearanceDirect` で実行されていた `chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)` を完全削除。
- Brio および HDM の標準設計に準拠し、Glamourer が注入したスケルトンとモデルをそのままゲームエンジンに描画させることで、自キャラへのロールバックを防止しました。

### 3. Glamourer デザイン適用の最適化 (`Services/GlamourerIpc.cs`)
- Guid 指定時に失敗していた JSON文字列による `ApplyState` の無理な呼び出しを廃止。
- Brio 公式実装と同じく `ApplyDesign(targetGuid, actorIndex, 0, 7UL)`（Flags: 7 = `DesignDefault`: Once | Equipment | Customization）を直接最優先で実行するように修正。

### 4. 前キャラの外見情報の残留防止
- デスポーン時に GlobalIndex だけでなくアクター名（`actor.DisplayName`）でも Glamourer ステートを `RevertStateName` / `UnlockStateName` で解放し、同一インデックス再利用時のステート混ざりを解消。
