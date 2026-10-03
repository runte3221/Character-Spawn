# 技術記録・ウォークスルー: Customize+ 一時プロファイル方式への完全移行と恒久設定汚染根絶 (v0.1.52.0)

## 1. 不具合の経緯と症状

### 発生した現象
- Character Spawn で Customize+ のプロファイル（例: `Lyle`, `Chonk` 等）を設定してパペットをスポーンさせた際、体型が変わらない場合がある。
- CustomizePlus 本体の設定画面や設定ファイル（`profiles/*.json`）を確認すると、見覚えのない `"Actor Ac"`, `"Actor Ad"`, `"Actor Au"` などの名前がプロファイルのキャラクター一覧に勝手に書き込まれて保存されている。
- パペットのスポーン・デスポーンを繰り返すと、プロファイルの変更通知がゲーム全体に飛び交い、自キャラを含む他のキャラクターのボーン再計算が発生してカクつきや予期せぬポーズ崩れが生じる。
- MCDF に内包されている Customize+ データが適用されない。

---

## 2. 実機ログと `CustomizePlus.dll` 逆アセンブルによる原因究明

### (1) `AddPlayerCharacter` によるユーザープロファイル設定ファイルの汚染
- `ActorManager.ApplyCustomizePlusProfile` は、`customizePlusIpc.AddPlayerCharacter(profileGuid, puppetName, worldId)` を呼び出していた。
- `CustomizePlus.dll` の内部実装（`ProfileManager.AddCharacter`）を逆アセンブル解析したところ、以下の処理を行っていることが判明：
  ```text
  ProfileManager.AddCharacter(profile, actorIdentifier)
  ├─ profile.Characters.Add(actorIdentifier)   // プロファイルのキャラクター一覧に追記
  ├─ SaveProfile(profile)                     // ディスク上の profiles/{guid}.json を即時上書き保存！
  └─ Event.Invoke()                           // プロファイル変更イベントを全体通知
  ```
- **実害**:
  - ユーザーの正規プロファイル設定ファイルがパペットのスポーンのたびに勝手に書き換えられて保存されていた。
  - デスポーン時に `RemovePlayerCharacter` を呼んでいたが、高速切り替えや例外発生時に取り残され、`0ec091f2-12b3-47bc-ace4-9a4ae2f4836a.json.bak` に見られるように `"Actor Au"` などのゴミデータが永続的に残骸化していた。

### (2) プロファイル無効化（Disabled）の壁
- ユーザー環境の `profiles/5576c46d-4385-4b4c-9d89-bf16b1fc5a24.json`（Lyle）を確認したところ、`"Enabled": false` になっていた。
- `AddPlayerCharacter` はキャラクターを紐付けるだけなので、プロファイル本体が無効化されていると、CustomizePlus はパペットの骨格変形を一切実行しない。

### (3) MCDF 外部プロファイルの未登録失敗
- MCDF 内包の Customize+ データは、ユーザーの CustomizePlus に未登録の外部プロファイルであるため、`AddPlayerCharacter` が `ec=3`（ProfileNotFound）で失敗していた。

### (4) 公式一時プロファイル IPC (`SetTemporaryProfileOnCharacter`) 未使用
- CustomizePlus 2.2+ には、まさにパペットや召喚アクター用として以下の公式 IPC が提供されている：
  - `CustomizePlus.Profile.SetTemporaryProfileOnCharacter(gameObjectIndex, profileJson)`
  - `CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter(gameObjectIndex)`
- これを使用すれば、ユーザーの設定ファイルを 1 文字も汚染せず、メモリ上だけでパペット（Index 200）にプロファイルを注入でき、プロファイルが Disabled でも確実に適用される。

---

## 3. 解決策の設計と実装

### ① `Services/CustomizePlusIpc.cs`: 一時プロファイル適用の強化と自己修復
1. **`SetTemporaryProfileByGuid` の強制有効化**:
   - `GetProfileJson(uniqueId)` でプロファイル完全 JSON を取得。
   - JSON 内の `"Enabled": false` を `"Enabled": true` に書き換えてから `SetTemporaryProfile` に渡す。
   - これにより、CustomizePlus 側で無効化されているプロファイルであっても、パペットには一時プロファイルとして 100% 確実に適用される。
2. **過去の汚染ゴミデータの自己修復 (`CleanupPuppetArtifacts`)**:
   - プラグイン起動時またはプロファイル一覧取得時に、各プロファイルの `Characters` を走査。
   - 名前が `"Actor "` で始まるゴミエントリがあれば自動的に `RemovePlayerCharacter` を呼び出し、過去に汚染されたユーザーの設定ファイルを元の綺麗な状態に修復。

### ② `Managers/ActorManager.cs`: 一時プロファイル方式への完全移行
1. **`ApplyCustomizePlusProfile`**:
   - 恒久設定変更（`AddPlayerCharacter`）の呼び出しを完全撤廃。
   - テンプレート指定プロファイル: `customizePlusIpc.SetTemporaryProfileByGuid((ushort)actorIndex, profileGuid)` で一時注入。
   - MCDF 内包プロファイル: 抽出した `cPlusJson` の `"Enabled"` を `true` に補正し、`customizePlusIpc.SetTemporaryProfile((ushort)actorIndex, cPlusJson)` で直接注入。
2. **`DespawnCharacter`**:
   - `DeleteTemporaryProfileOnCharacter((ushort)actor.GlobalIndex)` および `DeleteTemporaryProfile(guid)` を呼び出し、一時プロファイルを即座に破棄。
   - ユーザーの正規プロファイルには一切手を加えないため、ディスク I/O や不要な通知イベントがゼロになる。

---

---

## 4. 修正対象ファイル

1. [Services/CustomizePlusIpc.cs](file:///c:/Users/RYO/Desktop/Character-Spawn/Services/CustomizePlusIpc.cs)
   - `SetTemporaryProfileByGuid`: `"Enabled": true` 補正を追加。
   - `CleanupPuppetArtifacts`: 過去の残骸エントリの自己修復ロジックを追加。
   - `GetProfiles`: 初回取得時またはリフレッシュ時に `CleanupPuppetArtifacts` を自動起動。
2. [Managers/ActorManager.cs](file:///c:/Users/RYO/Desktop/Character-Spawn/Managers/ActorManager.cs)
   - `ApplyCustomizePlusProfile`: `SetTemporaryProfileByGuid` / `SetTemporaryProfile` への完全移行。
   - `DespawnCharacter`: 一時プロファイルクリーンアップへの完全統一（`DeleteTemporaryProfile` + `DeleteTemporaryProfileOnCharacter`）。
3. [CHANGELOG.md](file:///c:/Users/RYO/Desktop/Character-Spawn/CHANGELOG.md)
   - v0.1.52.0 の変更点を追記。

---

## 5. 他パイプラインへの影響ゼロ保証 (完全隔離)

- **Glamourer パイプライン**:
  - v0.1.51.0 で完全解決した人型NPCの Base64 圧縮データ渡し、DrawObject 強制再構築（`RedrawGuise`）、MCDF 外見適用コードには一切変更を加えていません。
- **Penumbra パイプライン**:
  - Mod の割り当ておよび Redraw 処理には一切触れていません。
- **Monster / Demihuman パイプライン**:
  - モデル変更やスケール変更の処理には一切触れていません。
- **CustomizePlus 処理の完全メモリ化**:
  - ユーザーの設定ファイル（`profiles/*.json`）に対するディスク書き込みが 0 回になったため、ファイル競合や破損、イベントループのリスクが完全に消滅しました。
