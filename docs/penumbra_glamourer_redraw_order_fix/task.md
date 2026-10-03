# タスクリスト: Glamourer 適用前の先行 Penumbra Redraw 抑止による外見・武器チラつき解消 (v0.1.54.0)

## 1. 不具合の事象と原因究明フェーズ
- [x] **事象確認**:
  - `Lyle Nude` 等の Glamourer + Penumbra テンプレートをスポーンさせた際、一瞬武器だけが違う、またはモデル自体が違う（自キャラ等が一瞬見える）現象が発生する。
  - 何度かスポーン・デスポーンを繰り返すと正常に戻る。
- [x] **実機ログ解析 (`dalamud.log` 11:44〜11:45)**:
  - スポーン直後（パペット生成時）に安全な描画骨格を確立するため、自キャラ（プレイヤー）の装備・武器が一旦ベースラインとしてコピーされる。
  - その直後、`ActorManager.cs` が `Penumbra.SetCollectionForActor` 成功直後に `penumbraIpc.Redraw(actorIndex)` を呼んでいた。
  - この瞬間、パペットはまだ Glamourer 適用前（自キャラの装備・武器のまま）であるため、Penumbra が自キャラの装備で不要な先行再描画を走らせていた。
  - 直後に Glamourer の `ApplyDesignToActor` が走り、二重のリロードと Penumbra Mod の非同期ディスク読み込みが競合して、わずか 100ms 程度の間に武器やモデルのチラつきが発生していた。
  - キャッシュ後はディスク読み込み遅延がゼロになるため、目視できなくなっていた。

## 2. 設計・実装フェーズ
- [x] **`Managers/ActorManager.cs` の整流化**:
  - [x] パイプライン A において、Glamourer が適用される場合は直前の不要な先行 `penumbraIpc.Redraw(actorIndex)` を抑止。
  - [x] Glamourer 自身の再描画により、Penumbra コレクションが新しい外見で一発同期適用されるように整流化。
  - [x] Glamourer が未設定の場合（Penumbra 単体）のみフォールバックとして `penumbraIpc.Redraw(actorIndex)` を呼ぶ安全ガードを実装。
  - [x] 他パイプライン（MCDF、人型NPC、モンスター、CustomizePlus）に一切触れず、完全隔離を保証。

## 3. ドキュメント・リリース・検証フェーズ
- [x] `docs/penumbra_glamourer_redraw_order_fix/` 配下の 3 ファイル作成・同期
- [x] `CHANGELOG.md` 更新（v0.1.54.0）
- [ ] 全自動リリースパイプライン実行 (`tools/release.ps1 0.1.54.0`)
- [ ] CI/CD ビルド成功と CDN 反映の確認
