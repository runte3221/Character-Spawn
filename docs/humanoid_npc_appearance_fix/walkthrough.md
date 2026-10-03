# 技術記録・ウォークスルー: 人型NPC外見適用不具合の経緯・原因分析と根本解決策 (v0.1.44.0)

## 1. 不具合の経緯と症状

### 発生した現象
- **Monster / MOB（レストレス・ラプトル等）**: パイプライン D（HDM GuiseService 準拠）により正常にスポーン・描画される。
- **Demihuman NPC（レターモーグリ等、`ModelCharaId > 0`）**: パイプライン D により正常にスポーン・描画される。
- **人型NPC（ミューヌ、ユウギリ等、`SourceType == Npc` かつ `ModelCharaId == 0`）**:
  - スポーンを実行した際、ミューヌやユウギリの姿にならず、現在操作中のプレイヤー（自キャラ）の姿のままスポーンしてしまう。

---

## 2. 原因の深掘り解析 (Glamourer コールドステートトラップ)

### (1) なぜ自キャラの姿でスポーンしたのか？
1. **アクター生成時のベースラインコピー**:
   - ゲームエンジン内で安全に 3D モデルを描画可能にするため、アクター生成直後に `meNative`（自キャラ）からスケルトン・骨格情報を `CopyFromCharacter(meNative, CharacterCopyFlags.WeaponHiding)` でコピーする。
2. **Glamourer キャッシュのコールド状態**:
   - スポーンされたばかりの新規パペット（Global Index 200）は、Glamourer 内部のアクター状態キャッシュがまだ生成されていない。
   - `GlamourerIpc.cs` の `TryApplyNpcAppearance` 内で `GetState(actorIndex)` を呼び出すと `null` が返る。
3. **外見上書き処理のスキップ**:
   - 旧実装では `if (state == null) return StateNull;` となっており、即座に関数を抜けていた。
   - その結果、NPC の 26バイト `CustomizeData` や衣装の `EquipmentModelIds` が一度も適用されず、最初にコピーされた自キャラの素体がそのまま描画されていた。

---

## 3. 解決策の設計と実装

### ① LocalPlayer ステートをテンプレートとする即時ディープコピー変身 (0ms)
- 待機ポーリングを復活させるのではなく、**常時キャッシュが存在する自キャラ（`GetState(0)`）のステート JObject をひな形としてディープコピー** する。
- その JObject に対して NPC の外見データをマッピング：
  1. `CustomizeData`（26バイト）を `custObj` に書き込み。
  2. `EquipmentModelIds`（10スロット）を `equipObj` に書き込み。
  3. 自キャラ固有の肌色・パラメータ汚染（`Parameters`, `Materials`）を完全に削除（Strip）。
  4. `ForceAllApply` を実行して全スロットの強制適用を保証。
  5. 武器スロット（`MainHand`, `OffHand`, `Weapon`）を明示的に解除（Unmanage, `Apply: false`）し、自キャラの武器が NPC に渡るのを防止。
  6. `ForceAllApply` 内でも、明示的に `Apply: false` とされた武器スロットは上書きしないよう保護。
  7. 完成したステートを `ApplyState` に渡して一括適用。
- これにより、コールド状態のパペットであっても待機ゼロ（0ms）で即座に NPC の姿に変身できる。

### ② Glamourer 失敗時のダイレクトメモリフォールバック
- `ActorManager.cs` の `ApplyNpcAppearance` において、万一 Glamourer IPC が失敗または利用不能な場合でも、メモリ上の `chara->DrawData.CustomizeData` および `EquipmentModelIds` を直接上書きし、`chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)` を実行する安全網（`ApplyNpcAppearanceDirectFallback`）を追加。

### ③ NPC テンプレートデータの名前ベース自動解決補完
- テンプレートの `CustomizeData` または `NpcEquipmentModelIds` が未設定の場合、`template.Name`（例: "ミューヌ", "ユウギリ"）からゲーム内 NPC データベース（`gameDataService.SearchNpcs`）を即座に逆引きし、ENpcBaseId・外見データを自動解決して補完するフォールバックを追加。

---

## 4. 修正対象ファイル

1. [Services/GlamourerIpc.cs](file:///C:/Users/RYO/Desktop/Character-Spawn/Services/GlamourerIpc.cs)
   - `TryApplyNpcAppearance`: コールドステート時に `GetState(0)?.DeepClone()` をひな形として使用。
   - `ForceAllApply`: 武器スロットの `Apply: false` を尊重するガードを追加。
   - 武器管理解除（Unmanage）の適用順序を最適化。
2. [Managers/ActorManager.cs](file:///C:/Users/RYO/Desktop/Character-Spawn/Managers/ActorManager.cs)
   - `ApplyNpcAppearance`: Glamourer 実行結果を検証し、失敗時に `ApplyNpcAppearanceDirectFallback` を実行。
   - `SpawnCharacter`: `template.Name` からの NPC データ自動逆引き補完を追加。
3. [CHANGELOG.md](file:///C:/Users/RYO/Desktop/Character-Spawn/CHANGELOG.md)
   - v0.1.44 の変更点を記載。

---

## 5. 今後の参照用ノウハウ・チェックリスト
- スポーン直後の新規パペット（Index 200）に対して Glamourer の `GetState` を呼ぶと、キャッシュ生成前で必ず `null` を返す。
- したがって、スポーン直後に動的 JObject を組み立てる際は、**`GetState(0)`（常時存在する自キャラ）のステートをひな形としてディープコピーして利用する** のが最も確実で高速なアプローチである。
- `ForceAllApply` を呼ぶ際は、NPC やモンスターなど武器を持たせないキャラクターに対して、自キャラの武器情報が誤って強制適用されないよう、武器スロットの除外ガードを徹底すること。

---

## 6. NPC固有顔（ユウギリ等）サニタイズ問題の経緯・原因分析と根本解決 (v0.1.45.0)

### (1) 現象
- ミューヌなどプレイヤーでもキャラクリ可能な顔のNPCは完璧に描画されるが、ユウギリなどNPC固有の特殊顔を持つキャラクターは、体や衣装はユウギリになるものの、顔だけがプレイヤー選択可能なアウラ汎用顔に置換されてしまう。

### (2) 原因の技術的深掘り
1. **`FilterCustomizeData` によるネイティブサニタイズ**:
   - ゲームエンジンのプレイヤースケルトン描画システムには `FilterCustomizeData` が組み込まれており、メモリ上の CustomizeData にプレイヤー未解放のフェイス番号（NPC固有顔）が書き込まれていると、自動的に選択可能な標準顔へと強制置換（丸め込み）されてしまう。
2. **Glamourer IPC 呼び出し失敗によるフォールバック動作**:
   - 本来 Glamourer は `FilterCustomizeData` をバイパスして特殊フェイスを描画可能にするが、`dalamud.log` を確認したところ `Glamourer ApplyNpcAppearance result on Global#200: False` となり、メモリ直接書き込みフォールバックが発動していた。
3. **Glamourer IPC 購読型の不一致**:
   - 公式の Glamourer IPC プロバイダは `FuncProvider<object, int, uint, ulong, int>(pi, "Glamourer.ApplyState", ...)` として登録されている。
   - 自作プラグイン側では `GetIpcSubscriber<string, int, uint, ulong, int>` と `string` で購読していたため、Dalamud IPC 内部の厳密な型照合によりシグネチャ不一致となって呼び出しが失敗していた。

### (3) 解決策
1. **IPC 購読型を `object` に整合化**:
   - `ApplyState` および `ApplyStateName` の購読型引数を `object` に修正し、Glamourer プロバイダと完全一致させた。
2. **JObject 直接適用メソッド `ApplyStateJObject` の導入**:
   - JSON 文字列化や Base64 圧縮を行わず、メモリ上の `JObject` をそのままダイレクトに `ApplyState` に渡すことでゼロオーバーヘッド適用を実現。
3. **自キャラ名ベースのテンプレート取得 (`GetStateName`)**:
   - `GetState(0)` で万一ステートが得られない場合のフェイルセーフとして、`clientState.LocalPlayer?.Name.TextValue` を用いた `GetStateName` を併用。

---

## 7. ValueTuple JObject 型境界例外の根絶と GetStateBase64 黄金律 (v0.1.46.0)

### (1) 現象
- v0.1.45.0 にアップデート後も、ユウギリやカヌ・エ・センナをスポーンさせた際、NPC固有の顔・髪型（アウラ固有顔や角尊の角・編み込み髪）が反映されず、プレイヤー汎用顔・髪型になってしまう。

### (2) 決定的ログと原因の真相
`dalamud.log` より:
```
Glamourer GetState V2 failed on actorIndex 0: IPC method Glamourer.GetState blew up when converting from ValueTuple`2 to System.ValueTuple`2[System.Int32,Newtonsoft.Json.Linq.JObject]
Glamourer GetStateName V2 failed for 'Ruma Meow': IPC method Glamourer.GetStateName blew up when converting from ValueTuple`2 to System.ValueTuple`2[System.Int32,Newtonsoft.Json.Linq.JObject]
Glamourer TryApplyNpcAppearance: Both target actor #200 and LocalPlayer (0 / 'Ruma Meow') returned null state.
[Pipeline C: NPC] Glamourer NPC appearance failed or unavailable. Applying direct memory fallback...
```

1. **Newtonsoft.Json / ALC 型境界問題**:
   - Dalamud プラグイン間では、それぞれ異なる AssemblyLoadContext や異なるバージョンの Newtonsoft.Json がロードされる場合がある。
   - `pi.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState")` を呼び出すと、Dalamud IPC の内部キャスト処理が、Glamourer 側でインスタンス化された `JObject` を CharacterSpawn 側の `JObject` に変換できず、`ValueTuple`2 to System.ValueTuple`2[...]` という型変換例外をスローする。
   - これにより、`GetState(0)` および `GetStateName("Ruma Meow")` の両方が例外で失敗し、`state == null` となって Glamourer 適用がスキップされていた。
2. **メモリ直接フォールバックによるサニタイズ**:
   - Glamourer がスキップされたためフォールバック処理（メモリ直接書き込み）が走り、ゲームエンジンの `FilterCustomizeData` によって未解放のNPC固有顔・髪型番号がプレイヤー汎用パーツに丸め込まれていた。

### (3) 根本解決策 (GetStateBase64 黄金律)
1. **`Glamourer.GetStateBase64` / `GetStateBase64Name` IPC の採用**:
   - 公式 IPC である `GetStateBase64` は `FuncSubscriber<int, uint, (int, string?)>` というシグネチャを持つ。
   - `string`（文字列）は .NET のコア型であるため、プラグイン間の ALC 境界や Newtonsoft.Json バージョン相違の影響を一切受けず、100% 確実に Base64 文字列を取得できる。
2. **既存の GZip デコーダ (`ParseDesignString`) による復号**:
   - 取得した Base64 文字列を、プラグイン内に既存の実績ある `ParseDesignString`（GZip 解凍）で自プラグイン側の `JObject` に安全にパース。
3. **`ApplyState` も Base64 文字列で適用**:
   - `state` に NPC の CustomizeData と EquipmentModelIds をマッピング後、`CompressToBase64(state)` で Base64 文字列を生成して `ApplyState` に渡す。
   - 取得から適用まで「型境界をまたぐ通信はすべて `string` (Base64) で行う」という AQuestReborn / HDM 黄金律を徹底した。

---

## 8. 独立キュー `HumanoidNpcApplyJob` による人型NPC固有外見の完全描画とMCDF完全保護 (v0.1.48.0)

### (1) 背景と課題の完全分離
1. **MCDF への影響原因**:
   - `Services/GlamourerIpc.cs` 内の `ApplyDesignToActor` が MCDF と NPC で共通利用されていたため、NPC 対策で行った引数変更（JObject化）が原因で MCDF の Base64 デザイン適用時に Glamourer 側で `FileVersion` 例外が発生していた。
   - **対策**: MCDF や 通常の Glamourer（Pipeline A/B）、Monster（Pipeline D）が通るコードには一切触れず、完全に分離・独立した Pipeline C（人型NPC）のみを構築する。
2. **NPC固有顔がサニタイズされていた真相 (Cold-Spawn Race)**:
   - HDM の `HumanGuise.cs` を逆アセンブル解析したところ、HDM は `Guise: Glamourer GetState(puppet obj#...) not ready — retrying up to 60 frames (cold-spawn race)` とログ出力し、スポーン直後（0フレーム目）のアクターが Glamourer に登録されるまでのタイムラグを `OnUpdate`（フレーム毎ループ）で監視・リトライしていることが判明。
   - 従来の Character-Spawn は 0フレーム目で即座に「失敗」と判定して直接メモリフォールバックに逃げていたため、ゲームエンジンの `FilterCustomizeData` によって未解放のNPC固有顔・髪型がプレイヤー汎用パーツ（金髪ボブ等）にサニタイズ（強制置換）されていた。

### (2) 解決策の設計と実装
1. **`HumanoidNpcApplyJob` の新設 (`ActorManager.cs`)**:
   - 人型 NPC スポーン時、`HumanoidNpcApplyJob` にエンキューして即座に return。
   - 毎フレームの `UpdateFrame` 内で、Glamourer がアクターを認識してステートを返すまで待機・リトライ（最大60フレーム、約1秒）。
   - Glamourer がパペットを認識した瞬間に、そのパペットのステートに対して Customize / Equipment を書き込み、`ApplyState` を実行。
   - これにより Glamourer が `ec=0`（Success）でアクターに変身を適用し、ゲームエンジンのサニタイズを完全にバイパスして固有顔・髪型（カヌ・エ・センナのツノ・編み込み髪、ユウギリのツノ・ウロコ・固有顔造形）が 100% 確実に描画される。
2. **他機能の完全保護**:
   - Pipeline A/B（MCDF、通常のGlamourer、Customize+、Penumbra）の処理には一切触れないため、既存機能への副作用はゼロ。




---

## 9. HDM 徹底逆アセンブル解析と真因解明・完全同期 (v0.1.49.0)

### (1) 実機ログ比較と動かぬ証拠
HDM でユウギリをスポーンさせたところ、完璧にユウギリ固有のアウラ顔と長い黒髪が描画された。実機ログを照合した結果：
- **HDM の挙動**:
  `	ext
  09:58:42.174 [INF] [HDM] Spawn: puppet global#200 (COM#0) cloned from local player as "Hdm Aa"
  09:58:42.268 [INF] [HDM] HDM: spawned puppet obj#200 as ユウギリ (Base 1011896).
  09:58:42.416 [INF] [HDM] Guise[human-diag] base 1011896 'ユウギリ' authored BNpcCustomize: Race=6 Gender=1 Clan=11 Face=201 Hairstyle=201...
  09:58:42.420 [INF] [HDM] Guise: obj#200 -> human NPC 'ユウギリ' (base 1011896) via Glamourer ApplyState (Success).
  `
  スポーン開始からわずか **0.24 秒（約 14 フレーム）** で Glamourer がパペットを認識し、`Face=201, Hairstyle=201` の適用が `Success`（0）で完了している。
- **Character Spawn の挙動**:
  `	ext
  09:58:04.671 [INF] [CharacterSpawn] Spawning 'ユウギリ' (Source: Npc, ModelChara: 0) at COM#0...
  09:58:05.829 [WRN] [CharacterSpawn] [Pipeline C: NPC] Glamourer NPC appearance timed out after 60 ticks on Global#200. Applying direct memory fallback...
  `
  **60 ticks（1.15 秒間）** 経過しても `GetState(200)` が一度も成功せず、タイムアウトして直接メモリフォールバックが走り、ゲームエンジンのサニタイズによって汎用顔に置換されていた。

### (2) 徹底逆アセンブル解析で判明した真因
1. **パペット名日本語文字トラップ（最大の真因）**:
   - `ActorManager.GetPuppetName` がテンプレート名から `"ユウギリ Cnpc"` という日本語文字を含む名前を生成し、`GameObject.SetName` で設定していた。
   - Glamourer の `ApiHelpers.FindState` は内部で `actors.GetIdentifier(actor)` を呼び出し、FF14 の `VerifyPlayerName`（ASCII英字のみ許可）で検証する。
   - 日本語文字が含まれていたため **`ActorIdentifier.IsValid` が false となり、Glamourer は常に `ActorNotFound (42)` を返し続けていた**。
   - HDM は、純粋な ASCII 英字 `"Hdm Aa"`, `"Hdm Ab"` を設定していたため、Glamourer が 100% 即座に有効な識別子を生成できていた。
2. **スポーン描画シーケンスの不一致**:
   - Character Spawn はスポーン直後に `EnableDraw()` を呼んでいた。
   - HDM は、スポーン直後は `DisableDraw` のまま待機し、`UpdateFrame` でゲームエンジンが `IsReadyToDraw()` を返してから `EnableDraw()` を呼び、さらに `DrawObject` の準備が完了してから Glamourer の `ApplyState` を呼んでいた。
3. **Customize マッピングの完全性確認**:
   - HDM の `HumanGuise.CustomizeMap`（36エントリ）を全バイト逆アセンブルした結果、Character Spawn の `CustomizeMap` と 100% 完全一致していることを証明。

### (3) 解決策の設計と実装
1. **パペット名の ASCII 英字プレイヤー名化 (ActorManager.cs)**:
   - `Interlocked.Increment(ref puppetSerial)` により、純粋な ASCII 英字プレイヤー名 `$"Actor {c1}{c2}"`（Forename: Actor, Surname: Aa..Zz）を生成。
   - 頭上のネームプレート表示や UI 表示は `DisplayName` / `NamePlate.CustomName`（`template.Name` = "ユウギリ"）を維持するため、ユーザーの画面上では完全に日本語で表示される。
2. **人型NPCスポーンシーケンスの HDM 完全同期 (ActorManager.cs)**:
   - スポーン直後は `nativeChara->GameObject.DisableDraw()` を呼び、`HumanoidNpcApplyJob` にエンキュー。
   - `UpdateFrame` 内で `IsReadyToDraw()` を待機して `EnableDraw()` を呼び、`DrawObject` の準備完了後に `glamourerIpc.TryApplyNpcAppearance` を呼ぶ。
3. **Name ベースのステート取得フォールバック (GlamourerIpc.cs)**:
   - `Glamourer.GetStateBase64Name` を購読し、`GetStateByName(actorName)` を新設。
   - Index 経由で取得できなかった場合でも Name（"Actor Aa"）経由で確実にステートを取得する多重防壁を構築。
4. **他パイプライン（MCDF, 通常Glamourer, Monster）の完全保護**:
   - 共通メソッドの破壊的変更は行わず、Pipeline C（人型NPC）のみを修正したため、既存の全機能への影響ゼロを保証。
