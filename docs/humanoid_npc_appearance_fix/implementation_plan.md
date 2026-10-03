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

## 7. v0.1.48.0 改修計画（独立キュー `HumanoidNpcApplyJob` による非同期同期待機）
- **背景と課題の分離**:
  - v0.1.45〜v0.1.46 で MCDF 適用に不具合が発生した原因は、MCDF と NPC の双方が通る共通メソッド `ApplyDesignToActor` のシグネチャ・引数型（JObject化）を変更してしまったため。
  - スポーン直後のアクターは、ゲームエンジンが生成してから数フレーム経たないと Glamourer のアクター認識テーブルに登録されないため、0フレーム目の即時適用では必ず `ec=2`（`ActorNotFound`）が返る。
  - HDM（`HumanGuise.cs`）の逆アセンブル解析により、HDM はフレーム更新ループ（`OnUpdate`）で Glamourer がパペットを認識するまで毎フレーム待機・リトライし、認識された瞬間に Glamourer 経由で適用していることが判明。
- **改修方針（他機能への影響ゼロ保証）**:
  1. **他パイプラインの完全保護**:
     - MCDF や 通常の Glamourer（Pipeline A/B）、Monster（Pipeline D）のコードには **1行も触れない**。
     - `Services/GlamourerIpc.cs` の共通メソッド `ApplyDesignToActor` も一切変更しない。
  2. **`Managers/ActorManager.cs` に `HumanoidNpcApplyJob` を新設**:
     - 人型NPC（`SourceType == Npc` かつ `ModelCharaId == 0`）をスポーンした際、`HumanoidNpcApplyJob` にエンキュー。
     - 毎フレームの `UpdateFrame` 内で、Glamourer がアクターを認識してステートを返すまで待機・リトライ（最大60フレーム、約1秒）。
     - 認識された瞬間に Glamourer 経由で外見を適用し、Penumbra Redraw を実行。
     - タイムアウト時のみ安全網として直接メモリ書き込みフォールバックを実行。
  3. **`Services/GlamourerIpc.cs` の `TryApplyNpcAppearance` の適正化**:
     - パペットのステートがまだ存在しない（`state == null`）場合は即座に `StateNull` を返してフレームリトライに委譲。
     - パペットのステートが取得できたら、そのステートに NPC の Customize / Equipment を書き込んで `ApplyState` を呼ぶ。
## 8. v0.1.49.0 改修計画（HDM 逆アセンブル解析に基づく ASCII パペット名とスポーン待機シーケンス完全同期）
- **徹底逆アセンブル解析によって判明した真因**:
  1. **パペット名の日本語文字トラップ**:
     - `ActorManager.GetPuppetName` がテンプレート名「ユウギリ」等から `"ユウギリ Cnpc"` という日本語文字を含む内部名を生成していた。
     - Glamourer の `ApiHelpers.FindState` は `actors.GetIdentifier(actor)` を呼ぶが、FF14 のプレイヤー名ルール（ASCII英字のみ）に違反しているため `id.IsValid` が false となり、**`ActorNotFound (42)` を返し続けていた**。
     - その結果、60 ticks 経ってもステートが取得できずタイムアウトし、直接メモリフォールバックが走ってゲームエンジンのサニタイズにより汎用顔に戻されていた。
     - HDM は、純粋な ASCII 英字 `"Hdm Aa"`, `"Hdm Ab"` を設定していたため、Glamourer が 100% 即座に認識（`id.IsValid == true`）していた。
  2. **描画待機シーケンスの不一致**:
     - Character Spawn ではスポーン直後に `EnableDraw()` を呼んでいたが、HDM ではスポーン直後は描画を無効化（`DisableDraw`）のまま保持し、`UpdateFrame` でゲームエンジンが `IsReadyToDraw()` を返してから `EnableDraw()` を呼び、さらに `DrawObject` の可視化準備が完了してから Glamourer の `ApplyState` を呼んでいた。
  3. **Customize マッピングの完全性確認**:
     - HDM の `HumanGuise.CustomizeMap`（36エントリ）と Character Spawn の実装は 100% 完全一致していることを証明。
- **改修方針**:
  1. **`Managers/ActorManager.cs` のパペット名生成を ASCII 英字化**:
     - `Interlocked.Increment(ref puppetSerial)` により、`$"Actor {c1}{c2}"`（ASCII英字プレイヤー名）を生成。
     - ネームプレート（頭上の名前表示）は `SpawnedActorData.NamePlate.CustomName = template.Name`（日本語）のまま維持されるため、ゲーム画面上では完全に元の名前が表示される。
  2. **`Managers/ActorManager.cs` の人型NPCスポーンシーケンスを HDM 準拠化**:
     - スポーン直後は `nativeChara->GameObject.DisableDraw()` を呼び、`HumanoidNpcApplyJob` にエンキュー。
     - `UpdateFrame` 内で `IsReadyToDraw()` を待って `EnableDraw()` を呼び、`DrawObject` の準備完了後に `glamourerIpc.TryApplyNpcAppearance` を呼ぶ。
  3. **`Services/GlamourerIpc.cs` に Name ベースのフォールバックを追加**:
     - `Glamourer.GetStateBase64Name` を購読し、`GetStateByName(actorName)` を新設。
     - `TryApplyNpcAppearance` 内で Index 経由で取得できなかった場合でも Name 経由で確実にステートを取得する多重防壁を構築。
- **検証手順**:
  1. `tools/release.ps1 0.1.49.0` で全自動リリース。
## 9. v0.1.50.0 改修計画（HDM完全照合によるENpc ID空間整合とDrawObject強制再構築）
- **徹底逆アセンブル照合によって判明した二大真因**:
  1. **ENpc ResidentId と BaseId の ID 空間乖離 (Two ENpc Spaces)**:
     - UI の検索リスト（`BuildNpcCache`）が `ENpcResident` を回していたため、ユウギリの ID が ResidentId `1007097` になっていた。
     - しかし外見データ（`GetNpcAppearanceData`）は `ENpcBase` から引いていた。
     - ユウギリの `ENpcBase` は `1011896` であり一致しないため、外見データが `null` となり、テンプレート保存時に自キャラデータ（ミコッテ）で汚染されていた。
     - HDM（`EventNpcIndex`）は `ENpcBase` を走査して `ENpcBase.RowId`（BaseId）をリスト ID に採用している。
  2. **Glamourer ApplyFlag (6UL) と DrawObject 強制再構築 (`RedrawGuise`)**:
     - HDM は `ApplyState` に `6UL`（Equipment=2 | Customization=4）を渡し、`Once (1)` を除外して永続適用していた。
     - さらに `ApplyState` 直後に `DisableDraw` → 2 ticks 待機 → `EnableDraw`（`RedrawGuise`）を実行し、ゲームエンジンの DrawObject を NPC 外見で強制再構築していた。
- **改修方針**:
  1. **`Services/GameDataService.cs`**:
     - `BuildNpcCache` を HDM と同様に `ENpcBase` 主ループに変更し、リスト ID を `ENpcBase.RowId`（BaseId）にする。
     - 既存の ResidentId や名前から正しい BaseId を解決するフェイルセーフを追加。
  2. **`Services/GlamourerIpc.cs`**:
     - `TryApplyNpcAppearance` 内の `applyStateV2Ulong` 呼び出し時のフラグを HDM と同一の `6UL`（Equipment | Customization）に変更。
     - 余計な `ForceAllApply` を排除し、NPC スロットのみ確実に適用。
  3. **`Managers/ActorManager.cs`**:
     - 人型NPCスポーン時、保存済みテンプレートが自キャラデータで汚染されている場合の自動リフレッシュを追加。
     - `HumanoidNpcApplyJob` で Glamourer 適用成功後に `DisableDraw` → 2 ticks 待機 → `EnableDraw`（DrawObject 強制再構築）を実行。
- **検証手順**:
  1. `tools/release.ps1 0.1.50.0` で全自動リリース。
  2. ユウギリ、ミューヌ、カヌ・エ・センナをスポーンさせ、100% 正しい本物の姿（固有顔・髪型・衣装）でスポーンすることを確認。





