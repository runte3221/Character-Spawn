# タスクリスト: AQR完全解析・外見適用の2フェーズ完全分離・Proteus外部干渉防御

## 課題・目的
1. **自キャラ変身の完全根絶**:
   - スポーンしたパペット（COM#0, Global#200）のみに Glamourer 外見と Penumbra コレクションが適用され、自キャラ（LocalPlayer, Index 0）には絶対に1ミリも影響を与えないこと。
   - 以前のテスト等で変身したまま残留していた自キャラのステートを確実に本来の姿に復元する。
2. **Glamourer未登録エラーとProteus外部プラグインによる自キャラMOD上書きの防御**:
   - アクター生成直後は Glamourer のキャッシュにアクターが存在しないため、直後の適用を Penumbra 事前割り当てのみに留める。
   - 描画準備完了待機（`ReadyJob`）のメインスレッド上で初めて Glamourer デザインを適用し、確実にパペットのみに反映させる。
3. **UI上でのワンクリック自キャラ復元機能**:
   - Character タブおよび Settings タブからいつでも自キャラを復元可能にする。

## 完了タスク
- [x] AQuestReborn (AQR) および Brio / Glamourer ソースコード / DLL のリバースエンジニアリング・完全解析
- [x] `Not on main thread!` を根絶するため、`Framework.RunOnFrameworkThread` での自キャラ安全復元を実装
- [x] `SpawnCharacter` 内の事前適用（`applyGlamourer: false`）と `ReadyJob` 内の確定適用（`applyGlamourer: true`）の完全分離
- [x] Character タブのアクションボタンに「Revert Player」ボタンを追加
- [x] バージョンを `0.1.41.0` に更新、CHANGELOG.md を追記
- [x] GitHub へのプッシュおよびローカルインストール同期
