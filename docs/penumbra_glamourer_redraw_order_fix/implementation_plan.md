# 実装計画: Glamourer 適用前の先行 Penumbra Redraw 抑止による外見・武器チラつき解消 (v0.1.54.0)

## 1. 概要
`Lyle Nude` 等の Glamourer デザインと Penumbra コレクションを併用したテンプレートのスポーン時、自キャラの武器やモデルが一瞬チラつく（または武器だけが自キャラのものになる）現象を、描画パイプラインの呼び出し順序整流化により根本解決する。

## 2. 根本原因の技術的分析
1. **自キャラ骨格の初期複写**:
   - パペット生成時、描画可能な骨格を確保するため自キャラの装備・武器が一旦コピーされる。
2. **Glamourer 適用前の不要な先行 Redraw**:
   - `ActorManager.cs` の通常スポーン（パイプライン A）において、Penumbra コレクション割り当て直後に `penumbraIpc.Redraw(actorIndex)` を呼んでいた。
   - この時点ではまだ Glamourer による外見上書き前であるため、Penumbra は「自キャラの装備・武器」に対してモデル・テクスチャの再読み込みをリクエストしてしまう。
3. **二重リロードと非同期読み込み競合**:
   - その直後に Glamourer の `ApplyDesignToActor` が呼ばれ、Glamourer 自身もアクターの再描画を命令する。
   - ゲームエンジン内で「自キャラのモデル・武器のロード」と「Glamourer の新外見のロード」が非同期に重なり、数フレームの間だけ自キャラの武器やモデルが画面に露出していた。

## 3. 実装方針
- **`Managers/ActorManager.cs` の改修**:
  - `Penumbra.SetCollectionForActor` 成功直後の `penumbraIpc.Redraw` 呼び出しを直ちに実行せず、フラグ `penSuccess` として保持。
  - Glamourer の適用成否を `glamApplied` として記録。
  - Glamourer が正常に適用された場合:
    - Glamourer 内部で自動的にアクターの再描画が行われるため、Penumbra 側からの余計な先行 Redraw は完全にスキップ。Penumbra のコレクションは Glamourer の新外見で直接適用される。
  - Glamourer が未設定（Penumbra のみ設定）の場合:
    - `if (penSuccess && !glamApplied)` の条件により、フォールバックとして `penumbraIpc.Redraw(actorIndex)` を実行し、既存の Penumbra 単体運用を完全保証。
- **他機能への完全隔離保証**:
  - MCDF パイプライン（`ApplyMcdfBundleDirect`）: 変更なし。
  - 人型NPC パイプライン（`HumanoidNpcApplyJob`）: 変更なし。
  - モンスター / デミヒューマン パイプライン: 変更なし。
  - CustomizePlus パイプライン: 変更なし。

## 4. 検証手順
- `tools/release.ps1 0.1.54.0` による全自動リリース。
- CI/CD ビルド成功と Fastly CDN 反映確認。
