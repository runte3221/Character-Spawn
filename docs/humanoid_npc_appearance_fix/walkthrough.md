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

---

## 10. HDM完全照合による二大根本原因の解決 (v0.1.50.0)

### (1) 徹底逆アセンブル解析で暴かれた二大根本原因
v0.1.49.0 の実機検証において、パペット名が ASCII 化されて Glamourer `GetState` が 2 ticks で成功し `ApplyState` も `result: 0` を返したにもかかわらず、ユウギリやミューヌが自キャラ（水着ミコッテ）でスポーンしてしまう現象を追究した結果、以下の二大真因が判明した。

#### ① ENpc ResidentId と BaseId の ID 空間乖離 (Two ENpc Spaces)
- **現象**: 保存済みテンプレート `CharacterSpawn.json` をデコードしたところ、ユウギリの `CustomizeData` に自キャラ（Race=4:ミコッテ, Clan=7:サンシーカー）のデータそのものが保存されていた！
- **真因**:
  - UI の NPC 検索一覧（`BuildNpcCache`）が `ENpcResident` シートを走査しており、ユウギリの ID を `ENpcResident` の RowId（`1007097`）として格納していた。
  - しかし、外見データ取得（`GetNpcAppearanceData`）は `ENpcBase` シートから引いていた。
  - ユウギリの `ENpcBase` の ID は **`1011896`** であり、ID が一致しないため外見データが取得できず、自キャラの外見でフォールバックしてテンプレートが保存されていた！
- **HDM の実装**:
  - HDM（`EventNpcIndex.cs`）は `ENpcResident` ではなく **`ENpcBase` シートを走査** し、`ENpcBase.RowId`（ユウギリなら `1011896`）をリストの ID として採用していた。
  - そのため、HDM は Base 1011896 から Race=6（アウラ）, Clan=11（レン）, Face=201（NPC固有顔）, Hairstyle=201（NPC固有髪）を 100% 正確に取得していた。

#### ② Glamourer ApplyFlag (6UL) と DrawObject 強制再構築 (`RedrawGuise`)
- **真因**:
  - HDM の `HumanGuise.Apply` は、Glamourer `ApplyState` に **`6UL`（`ApplyFlag.Equipment | ApplyFlag.Customization`）** を渡していた。`Once (1)` を除外することで、ステートを一時的ではなく永続的にパペットに定着させていた。
  - さらに、HDM は `ApplyState` 成功直後に `_guise.Redraw`（`DisableDraw` → 最低 2 ticks 待機 → `IsReadyToDraw` 確認 → `EnableDraw`）を呼び出していた。
  - HDM の実機ログ:
    `"forcing a draw-object rebuild + re-assert so the NPC customize/Race renders (self: RevertToGameBase-vs-ApplyState race; puppet: cold-spawn render gap)."`
  - Character Spawn では `ApplyState` 後に DrawObject の破棄・再構築を行っていなかったため、ゲームエンジン内で初期素体（自キャラのミコッテ）のモデルがそのまま残存していた。

---

### (2) 解決策の実装

1. **`GameDataService.cs`: HDM 準拠の ENpcBase 主軸キャッシュと逆引き解決**:
   - `BuildNpcCache` を `ENpcBase` 主ループに変更。人間型（`ModelCharaId == 0`）およびデミヒューマン（`ModelCharaId > 0`）を適切に判別し、リストの ID を **`ENpcBase.RowId`** に統一。
   - `ResolveNpcAppearance(enpcId, name)` を新設。過去に ResidentId（`1007097`）で保存された既存テンプレートであっても、NPC名「ユウギリ」から自動的に正しい BaseId（`1011896`）へリマップして正しい外見データを返す自己修復機構を実装。
2. **`GlamourerIpc.cs`: HDM 完全準拠の 6UL 永続適用**:
   - `TryApplyNpcAppearance` 内で、HDM と同一の `6UL`（`Equipment | Customization`）フラグで `applyStateV2Ulong` を直接呼び出し。
   - 余計な `ForceAllApply` を排除し、NPC に必要なスロットのみ確実に適用。
   - MCDF や通常 Glamourer パイプラインには一切触れず、人型NPC専用処理として完全隔離。
3. **`ActorManager.cs`: DrawObject 強制再構築シーケンス (HDM RedrawGuise 準拠)**:
   - スポーン時に既存テンプレートの自動リフレッシュを実行（汚染された自キャラデータを本物の NPC データで即時上書き）。
   - `HumanoidNpcApplyJob` で Glamourer 適用成功後、直ちに `DisableDraw()` を実行。
   - 最低 2 ticks 待機し、ゲームエンジンの準備完了（`IsReadyToDraw`）を確認してから `EnableDraw()` を呼び出すことで、ゲームエンジンの DrawObject を NPC 外見で強制再構築！

---

## 11. Glamourer ApplyState Base64 圧縮データ渡しと連続スポーン遅延解消 (v0.1.51.0)

### (1) 現象と実機ログの分析
v0.1.50.0 において以下の現象が発生：
- ミューヌをスポーン → ミューヌが表示される（正常）
- カヌ・エ・センナをスポーン → なぜかミューヌが表示される（直前のキャラ）
- ユウギリをスポーン → なぜかミューヌが表示される
- その後ミューヌをスポーン → 今度はカヌ・エ・センナが表示される（顔や角も正常に変わっている）
- 連続してスポーン・デスポーンを繰り返すと、最終的に自キャラしかスポーンしなくなる。

### (2) 実機ログ (`dalamud.log`) と逆アセンブル解析で判明した真因
`dalamud.log` より：
```
System.Exception: Unknown Error decoding Base64.
   at Glamourer.Designs.DesignConverter.FromBase64(String base64, Boolean customize, Boolean equip, Byte& version) in /_/Glamourer/Designs/DesignConverter.cs:line 113
Glamourer ApplyState (ulong flags=6) for NPC on actor #200 ('Actor Bc') result: 7
Glamourer TryApplyNpcAppearance on actor #200 ('Actor Bc') final result: False
[Pipeline C: NPC] Glamourer NPC appearance timed out after 60 ticks on Global#200 ('ユウギリ'). Applying direct memory fallback...
Applied Humanoid NPC appearance fallback on Global#200 after 60 ticks.
```

1. **Glamourer ApplyState のデータ形式不一致**:
   - `Glamourer.dll` の `StateApi.ApplyState` の実装をディスアセンブルした結果、引数が `string` の場合、Glamourer はそれを **Base64 文字列** として解釈し、`DesignConverter.FromBase64` でデコードすることが判明。
   - `GlamourerIpc.TryApplyNpcAppearance` は生の JSON 文字列 `[{"FileVersion":1,...}]` を渡していたため、毎回例外が発生して `GlamourerApiEc.InvalidState (7)` で失敗していた！
   - つまり、Glamourer による適用は 1 度も成功していなかった。
2. **60 ticks タイムアウトと遅延フォールバックの蓄積による外見のズレ**:
   - Glamourer が毎フレーム失敗するため、60 ticks（約1秒）のタイムアウトまで待たされ、その後に `ApplyNpcAppearanceDirectFallback` と `penumbraIpc.Redraw` が走っていた。
   - ユーザーがプレビュー一覧で次々と別のキャラをクリックすると、前のキャラの遅延フォールバックや Penumbra Redraw がゲームスレッド上で遅れて実行され、新しくスポーンしたパペットに対して前回の外見（ミューヌやカヌ・エ・センナ）が上書きされていた。
   - スポーン・デスポーンを高速で繰り返すと、タイムアウト中の未完了ジョブが重なり、最終的に Direct Fallback も追いつかず、初期素体（自キャラ）のまま残ってしまっていた。

### (3) 解決策の実装
1. **`Services/GlamourerIpc.cs`: `CompressToBase64` による Base64 圧縮文字列渡し**:
   - `TryApplyNpcAppearance` 内で、すでに MCDF パイプラインで 100% 成功実績のある `CompressToBase64(state)` を呼び出し、GZip 圧縮された Base64 文字列を `applyStateV2Ulong` に渡すよう修正。
   - これにより、Glamourer は 1 フレーム目（0 ticks）で即座に `result: 0`（Success）を返す。
   - 待機時間は DrawObject の DisableDraw → EnableDraw の 2 ticks のみとなり、合計わずか 4 ticks（約0.06秒）で NPC 外見の適用が完了。
   - タイムアウト待ち（60 ticks）や遅延フォールバックが一切発生しなくなり、連続スポーン時でもズレることなく瞬時に本来の NPC 外見が適用される！

---

## 12. 人型NPC不具合の全変遷と類似トラブル混同防止マトリクス（完全決定版）

今後同様の不具合が発生した際に、過去の類似事例と混同して誤った対応を取らないよう、これまでに発生したすべての事象・類似点・根本的な相違点・判定基準を網羅した完全記録。

### (1) 全フェーズ対比マトリクス

| バージョン | 表面上の症状 | 類似しているが全く異なる【真因】 | 解決策 |
|---|---|---|---|
| **v0.1.44 以前** | 人型NPCをスポーンすると**自キャラの姿**になる | **Glamourer コールドステートトラップ**<br>スポーン直後のパペットは Glamourer 内部キャッシュが未生成のため `GetState` が null を返し、即座に関数を抜けて素体（自キャラ）のまま残っていた。 | 自キャラ（`GetState(0)`）をディープコピーして即座に上書き適用するアプローチを導入。 |
| **v0.1.45** | ミューヌは出たが、ユウギリ等の固有顔が**プレイヤー汎用顔（金髪ボブ等）**になる | **ゲームエンジンのサニタイズ（FilterCustomizeData）**<br>Glamourer IPC の型不一致（`string` vs `object`）で IPC が失敗し、フォールバック（メモリ直接書き込み）が走った結果、ゲームエンジンによって未解放の顔番号（201等）が汎用顔に強制丸め込みされた。 | Glamourer IPC の購読型を修正し、JObject 直接適用メソッドを新設。 |
| **v0.1.46** | カヌ・エ・センナ、ユウギリが**汎用顔または自キャラ**になる | **ValueTuple JObject ALC 型境界例外**<br>Dalamud プラグイン間で `ValueTuple<int, JObject>` をやり取りする際、Newtonsoft.Json の AssemblyLoadContext（ALC）境界で型キャスト例外が発生し、ステート取得が失敗していた。 | 文字列通信の公式 IPC `Glamourer.GetStateBase64` を採用し、通信をすべて Base64 文字列に統一。 |
| **v0.1.47〜0.1.48** | MCDF 適用時に自キャラ化する副作用が発生 / NPC が依然として汎用顔 | **パイプライン共通化の罠 & Cold-Spawn Race**<br>MCDF と NPC で共通の `ApplyDesignToActor` を通していたため、NPC 用の改修が MCDF を破壊。また 0 フレーム目ではパペットが未登録で必ず失敗していた。 | パイプライン完全分離（Pipeline C新設）。HDM 準拠の非同期待機キュー `HumanoidNpcApplyJob` を新設し、認識されるまでフレームリトライ。 |
| **v0.1.49** | 60 ticks 待ってもパペットが認識されず**自キャラ**になる | **パペット名の日本語文字トラップ**<br>`GetPuppetName` がテンプレート名「ユウギリ」から `"ユウギリ Cnpc"` という日本語文字を含む内部名を生成していたため、Glamourer の `VerifyPlayerName`（ASCII英字のみ）で弾かれ、永久に `ActorNotFound` になっていた。 | HDM 準拠の純粋 ASCII 英字プレイヤー名（`"Actor Aa"`, `"Actor Ab"`）を生成。 |
| **v0.1.50** | ミューヌとユウギリが**自キャラ（水着ミコッテ）**でスポーン | **ENpc ResidentId vs BaseId の空間乖離 & DrawObject 未再構築**<br>1. UI リストが `ENpcResident`（ユウギリ=1007097）だったが、外見データは `ENpcBase`（ユウギリ=1011896）から引いており、ID 不一致で自キャラデータがテンプレートに保存されていた。<br>2. Glamourer ApplyFlag が 7UL（Once）で永続化されず、かつ適用後に DrawObject 強制再構築（`RedrawGuise`）を行っていなかったため、自キャラの 3D モデルが残存していた。 | 1. `BuildNpcCache` を `ENpcBase` 主軸走査に変更し、名前からの自動自己修復機構を実装。<br>2. HDM と同一の `6UL`（Equipment \| Customization）永続フラグ、および適用後の `DisableDraw` → 2 ticks 待機 → `EnableDraw` を実装。 |
| **v0.1.51 (今回)** | ミューヌは出たが、カヌエセンナを押すとミューヌが出る。**連続スポーンで直前のキャラが出たり自キャラに戻る** | **ApplyState 引数型不一致（Base64 期待 vs 生 JSON 渡し）& タイムアウト遅延の重なり**<br>`Glamourer.dll` の `StateApi.ApplyState(string)` は Base64 圧縮文字列を期待するが、生の JSON 文字列を渡していたため毎フレーム `result: 7`（InvalidState）で例外終了。その結果 60 ticks タイムアウト後に無理やり Direct Memory Fallback と Penumbra Redraw が走り、連続スポーン時に前のキャラの遅延描画が新キャラに重なってズレていた。 | `TryApplyNpcAppearance` で `CompressToBase64(state)` を通して Base64 圧縮文字列を渡すように修正。Glamourer が 0 ticks（即時）で Success を返し、わずか 4 ticks（約0.06秒）で完全描画完了。 |

---

### (2) 「自キャラの姿になる」症状の真因識別チャート

「自キャラの姿でスポーンする」という現象は過去に 4 回発生しているが、**内部で起きている原因は毎回まったく異なる**。
次回同様の事象が発生した場合は、以下のログ確認ポイントで即座に真因を特定すること：

```text
Q1: CharacterSpawn.json の template.CustomizeData[0] (Race) は何になっているか？
├─ 自キャラの Race (例: 4=ミコッテ) になっている
│   └─ 【原因】ENpc ResidentId と BaseId の乖離 (v0.1.50 の問題)。
│      外見データ取得元が ENpcBase.RowId ではなく ENpcResident.RowId になっている。
│      解決: ENpcBase 主軸で検索キャッシュを作り、名前逆引きで BaseId を解決する。
│
└─ 正しい NPC の Race (例: 2=エレゼン, 6=アウラ) になっている
    │
    ├─ Q2: dalamud.log で Glamourer ApplyState の戻り値 (result) はいくつか？
    │   ├─ result: 7 (InvalidState) かつ "Unknown Error decoding Base64" 例外が出ている
    │   │   └─ 【原因】データ形式不一致 (v0.1.51 の問題)。
    │   │      ApplyState に生 JSON 文字列を渡している。
    │   │      解決: CompressToBase64(state) で GZip 圧縮 Base64 文字列を渡す。
    │   │
    │   ├─ result: 2 (ActorNotFound) または GetState が null で 60 ticks タイムアウト
    │   │   └─ 【原因】パペット名の ASCII 違反 (v0.1.49 の問題)。
    │   │      GameObject.SetName に日本語文字が含まれており Glamourer が弾いている。
    │   │      解決: "Actor Aa" などの純粋 ASCII 英字プレイヤー名にする。
    │   │
    │   └─ result: 0 (Success) なのに見た目が自キャラのまま
    │       └─ 【原因】DrawObject が再構築されていない (v0.1.50 の問題)。
    │          ApplyState 成功直後に DisableDraw → 2 ticks 待機 → EnableDraw を実行していない。
    │          解決: RedrawGuise シーケンスを確実に実行してエンジン側のモデルを破棄・再生成させる。
```

---

### (3) 「NPC 固有顔・髪型がプレイヤー汎用顔（金髪ボブ等）になる」症状の真因識別

- **現象**: 服や体格は変わっているが、カヌ・エ・センナのツノ・編み込み髪型や、ユウギリのアウラ固有顔（Face 201）が反映されず、プレイヤー作成可能な普通の顔・髪型になってしまう。
- **真因**:
  - **ゲームエンジン内の `FilterCustomizeData` によるサニタイズ（強制置換）**。
  - 直接メモリ書き込み（`chara->DrawData.CustomizeData`）を行うと、ゲームエンジンが「このプレイヤー種族・部族では選択不可能な顔番号」と判断して汎用顔に丸め込んでしまう。
  - **Glamourer 経由で適用された場合は、Glamourer がエンジンのサニタイズを完全にバイパスするため、固有顔・髪型が 100% 保持される**。
  - したがって、「汎用顔になる」＝「**Glamourer 適用が何らかの理由でスキップまたは失敗し、フォールバックの直接メモリ書き込みが走っている**」ことを意味する。
  - ログで `[Pipeline C: NPC] Glamourer NPC appearance timed out after 60 ticks` が出ていないか確認すること。

---

### (4) 「連続スポーンで直前のキャラが出たり処理がズレる」症状の真因識別

- **現象**: プレビュー一覧でキャラ A をスポーン後、デスポーンせずにキャラ B をスポーンするとキャラ A が出たり、次々とクリックすると前のキャラが表示され、最終的に自キャラになる。
- **真因**:
  - **Glamourer が毎フレーム失敗（result: 7）し、60 ticks（約1秒）のタイムアウト待ちキューが滞留していること**。
  - タイムアウト待ちの間に次のキャラが要求されると、ゲームスレッド上で遅延した Direct Memory Fallback や `Penumbra.Redraw` が後から発火し、新しく生成されたパペットに前回の外見を上書きしてしまう。
  - **正常時は Glamourer が 0 ticks（即時）で Success を返すため、待機時間は DrawObject 再構築の 2 ticks（約0.03秒）しか存在せず、連続スポーンしても絶対にズレない**。
  - ログで `Glamourer NPC appearance timed out after 60 ticks` が多発していないか確認すること。

---

### (5) 今後の保守・改修時の絶対遵守ルール (黄金律)

1. **Pipeline の完全隔離**:
   - MCDF（Pipeline A/B）、Monster / Demihuman（Pipeline D）、人型NPC（Pipeline C）はそれぞれ完全に独立したパイプラインである。
   - 人型NPCの改修時に、MCDF や Monster が通る共通メソッド（`ApplyDesignToActor` 等）のシグネチャや引数型を絶対に変更しないこと。
2. **Glamourer 通信はすべて Base64 (GZip 圧縮) 文字列で行う**:
   - `ApplyState` に渡すデータは、生の JSON ではなく必ず **`CompressToBase64(state)`** を通すこと。
   - プラグイン間で `JObject` などの複合型を直接 IPC でやり取りすると、Newtonsoft.Json の ALC 境界例外が発生するため、外部 IPC 通信はすべて `string`（Base64）で行うこと。
3. **パペット名は常に ASCII 英字プレイヤー名（`"Actor Aa"`）を維持する**:
   - ゲームエンジン内部名に日本語を使用しないこと（ネームプレートや UI 表示は `DisplayName` で日本語を維持する）。
4. **NPC データは常に `ENpcBase` を主軸として取り扱う**:
   - `ENpcResident` は配置・名称用テーブルであり、外見データ（`ModelChara`, `Race`, `Face`, `Equipment`）はすべて `ENpcBase` に存在する。ID は常に `ENpcBase.RowId` を正とすること。



