# 実装計画: 人型NPC（ミューヌ、ユウギリ等）外見適用不具合の根本解決 (v0.1.44.0)

## 1. 概要
モンスターやデミヒューマン（レターモーグリ等）は正常にスポーン・描画される一方、人型NPC（ミューヌ、ユウギリ等、`ModelCharaId == 0`）をスポーンさせた際に、NPCの外見にならず操作中の自キャラ（LocalPlayer）の姿でスポーンしてしまう問題を根本解決する。

## 2. 根本原因の技術的分析
1. **Glamourer コールドステートトラップ**:
   - スポーン直後の新規パペット（Index 200）は、Glamourer 内部のアクター状態キャッシュがまだ生成されていないため、`GetState(actorIndex)` が `null` を返す。
   - 旧実装の `TryApplyNpcAppearance` は `if (state == null) return StateNull;` と即座に失敗し、外見上書き処理が行われていなかった。
2. **ベースライン素体の残存**:
   - アクター生成時に drawable 骨格を確立するため、自キャラから `CopyFromCharacter(meNative, CharacterCopyFlags.WeaponHiding)` で素体をコピーしている。
   - 外見上書きがスキップされた結果、この自キャラの姿がそのまま表示され、「自キャラがスポーンした」ように見えていた。

## 3. 実装方針と改修箇所

### ① `Services/GlamourerIpc.cs`: LocalPlayer テンプレートによる即時ディープコピー変身 (0ms)
- `GetState(actorIndex)` が `null` の場合、常時存在する自キャラ（`GetState(0)`）の JObject をひな形としてディープコピー。
- NPC の 26バイト `CustomizeData` と 10スロットの `EquipmentModelIds` を上書き。
- 自キャラ固有の肌色・パラメータ汚染（`Parameters`, `Materials`）を完全に Strip。
- `ForceAllApply` を実行後、武器スロット（`MainHand`, `OffHand`, `Weapon`）を明示的に解除（Unmanage, `Apply: false`）して自キャラ武器の混入を防止。
- `ForceAllApply` 内で明示的に `Apply == false` と設定された武器スロットを上書きしないよう保護ガードを追加。
- `ApplyState` を呼ぶことで、待機ポーリングを挟まず 0ms で確実に NPC の姿に変身させる。

### ② `Managers/ActorManager.cs`: Glamourer 失敗時のダイレクトメモリフォールバック
- `ApplyNpcAppearance` 内で Glamourer の戻り値を検証。
- 失敗または Glamourer 未起動時は `ApplyNpcAppearanceDirectFallback` を呼び出し、アクターメモリの `DrawData.CustomizeData` および `EquipmentModelIds` を直接上書きし、`CopyFromCharacter` を実行する。

### ③ `Managers/ActorManager.cs`: NPC テンプレートデータの名前ベース自動解決補完
- テンプレートの `CustomizeData` または `NpcEquipmentModelIds` が未設定の場合、`template.Name`（例: "ミューヌ", "ユウギリ"）からゲーム内 NPC データベースを即座に逆引きし、ENpcBaseId・外見データを自動解決して補完する。

## 4. 検証手順 (v0.1.44.0)
1. `tools/release.ps1 0.1.44.0` を実行し、全自動リリース（バージョン一括更新、Git コミット・プッシュ、CI/CD ビルド監視、マニフェスト整合性確認）。
2. ゲーム内でプラグインを v0.1.44.0 に更新。
3. ミューヌ、ユウギリをそれぞれスポーンさせ、自キャラの姿ではなく正常な NPC の顔・髪型・衣装でスポーンすることを確認。
4. レターモーグリ（デミヒューマン）およびモンスターが引き続き正常にスポーンできることを確認。

## 5. v0.1.45.0 改修計画（NPC固有顔サニタイズ防止）
- **課題**: 直接メモリ書き込み時に `FilterCustomizeData` によりユウギリ等の固有NPC顔がプレイヤー汎用顔に丸め込まれていた。Glamourer IPC の `ApplyState` 購読型が `string` だったため IPC が失敗していた。
- **改修方針**:
  1. `Services/GlamourerIpc.cs`: `ApplyState` / `ApplyStateName` の購読型を `object` に修正。
  2. `Services/GlamourerIpc.cs`: `ApplyStateJObject` メソッドを新設し、JObject をダイレクトに渡す。
  3. `Services/GlamourerIpc.cs`: `GetStateName` を新設し、自キャラ名ベースのテンプレート取得フォールバックを追加。
  4. `Managers/ActorManager.cs`: `localPlayerName` を渡すように連携。
- **検証手順**:
  1. `tools/release.ps1 0.1.45.0` で全自動リリース。
  2. ユウギリをスポーンさせ、固有のツノ・ウロコ・顔造形が完全に描画されることを確認。

## 6. v0.1.46.0 改修計画（ValueTuple JObject 型不一致根絶と GetStateBase64 黄金律）
- **根本原因**:
  - `dalamud.log` 解析により、`pi.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState")` で `converting from ValueTuple'2 to System.ValueTuple'2[System.Int32,Newtonsoft.Json.Linq.JObject]` 例外が発生し、自キャラテンプレートの取得に失敗していた。
  - これはプラグイン間（ALC境界・Newtonsoft.Jsonアセンブリ境界）で `JObject` を ValueTuple でやり取りする際に Dalamud IPC 内部で発生する既知の型キャスト例外。
- **改修方針**:
  1. `Services/GlamourerIpc.cs`: `Glamourer.GetStateBase64` / `Glamourer.GetStateBase64Name`（`FuncSubscriber<int, uint, (int, string?)>`）を最優先利用。
  2. 取得した Base64 文字列を既存の `ParseDesignString`（GZipデコード）で安全に `JObject` に復号。
  3. `ApplyStateJObject` でも `CompressToBase64(state)` により Base64 文字列（`string`）として Glamourer に渡すことで、適用時の型境界トラブルも完全根絶。
- **検証手順**:
  1. `tools/release.ps1 0.1.46.0` で全自動リリース。
  2. カヌ・エ・センナ、ユウギリをスポーンさせ、角尊の角・固有髪型、アウラ固有顔が 100% 確実に描画されることを確認。


