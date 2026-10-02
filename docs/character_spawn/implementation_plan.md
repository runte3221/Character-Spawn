# 実装計画書: v0.1.9 大幅改修（IPC復旧・NPC/Monsterスポーン・プレビュー化・UI刷新）

最新のユーザーフィードバックに基づき、Glamourer & Penumbra IPC の接続復旧、NPC / Monster スポーンの根本原因解消、Characterタブのプレビュー専用化、New Chara モーダルのボタン化、Logタブの新設、3Dギズモのドラッグ修正を包括的に実施します。

---

## 1. ユーザー要望と改修方針

### (1) Glamourer & Penumbra IPC の接続復旧
- **現状の課題**: `Glamourer design` / `Penumbra Collection` で「IPC Not detected」と表示され、保存デザインが取得できない。
- **原因**:
  - `Glamourer.ApiVersions`（引数あり）を呼び出していたが、最新実機DLL（1.7.1.3）では `Glamourer.ApiVersion.V2`（引数なし・戻り値 `(int, int)`）に変更されており例外が発生していた。
  - `Glamourer.GetDesignList.V2`, `Penumbra.ApiVersion.V5`, `Penumbra.GetCollections.V5` 等への更新が必要。
  - 起動時の1回のみ検出しており、後から起動した場合に検出されない。
- **改修方針**:
  - 最新 IPC キー（`.V2` / `.V5`）に更新し、旧バージョンへのフォールバックを実装。
  - 動的リトライポーリング（参照時にキャッシュが期限切れなら再チェック）を導入し、確実に検出する。

### (2) 「Log」タブの新設（リアルタイムログ収集）
- **現状の課題**: キャラクターのスポーン成否や外見データ適用の原因をユーザーがゲーム内で確認できない。
- **改修方針**:
  - `Managers/LogManager.cs` を新設し、最新ログをリングバッファ（最新500件）に保持。
  - MainWindow の「Settings」タブの隣に「Log」タブを追加。
  - スポーン実行ログ、ModelCharaId、CustomizeData、IPC成否、エラー詳細を表示し、ワンクリックでクリップボードへコピー可能にする。

### (3) Monster / Mob 上限撤廃 & スポーン不具合（マタガイガイ、食道楽のゼゼルン）解消
- **上限撤廃**: `SearchMonsters` の上限（500件）を撤廃し、全件検索可能にする。
- **マタガイガイの自キャラ化原因**: `BNpcName.RowId` と `BNpcBase.RowId` は一致しないため、`ModelCharaId = 0` となり自キャラが表示されていた。
  - 解決策: Brio 同等の `BNpcLink`（`BNpcNameId` と `BNpcBaseId` のマッピング）を内蔵し、正しい `ModelCharaId` を取得する。
- **食道楽のゼゼルンのギズモのみ原因**: 非人型モデル（ModelCharaId > 0）に対し、人型の自キャラから `CopyFromCharacter` を実行したためモデルコンテナとスケルトンが不整合を起こして描画不能になっていた。
  - 解決策: 非人型モデルの場合は自キャラからのコピーを行わず、モデルコンテナを正しく初期化して描画を有効化する。

### (4) NPC (ENpc) 上限撤廃 & スポーン不具合（レターモーグリ、ル・スーシモ）解消
- **上限撤廃**: `SearchNpcs` の上限（500件）を撤廃し、全件検索可能にする。
- **レターモーグリのギズモのみ原因**: 非人型NPC（ModelCharaId > 0）に対する人型コピーの不整合を同様に解消。
- **ル・スーシモの武器問題 & Weapon Visible トグル**:
  - 右ペインに `[x] Weapon Visible`（武器表示 ON/OFF）チェックボックスを追加（画像3準拠）。
  - チェックOFF時は `HideWeapons()` を適用し、自キャラの武器が残らないようにする。

### (5) MCDF ファイルスポーン時の自キャラ化解消
- MCDF 選択時、Glamourer IPC 復旧と連動してパース済みの外見文字列（または Base64）をプレビューアクターに確実に適用する。

### (6) Character タブのプレビュー専用化（画像3, 4準拠）
- Character タブでのスポーンは Scene タブとは完全に切り離し、現在の MAP 上で見た目情報を確認するための「プレビュー用」とする。
- 右ペインのボタン構成：
  - 未スポーン時: `[ Spawn ]` `[ edit ]` `[ delete ]`
  - スポーン後: ボタンが赤色 `[ Despawn ]` に切り替わり、押すとプレビューアクターを即消去。
  - スポーン中表示: 緑色テキスト `[Name] Spawning...` ＋ `[x] Gizmo`（ギズモ表示 ON/OFF）チェックボックスを表示。

### (7) New Chara モーダル UI のボタン化（画像1準拠）
- `Select Appearance Source` のプルダウンを廃止し、2×2 のボタングリッドに変更：
  - 上段: `[ Glamourer&Penumbra ]` `[ MCDF ]`
  - 下段: `[ NPC(ENpc) ]` `[ Monster/Mob ]`
  - 選択中のボタンは赤色背景ハイライト。
  - 境界線（セパレータ）の下に、選択した source に対応した固有の入力項目を表示。

### (8) 3D ギズモのドラッグ操作不良の修正
- ハンドルの当たり判定（半径）を拡大し、軸の線（ライン全体）もクリック・ドラッグ可能にする。
- スクリーン座標から 3D 移動量への投影計算の精度と感度を調整。

---

## 2. 変更対象ファイル

1. **`Services/GlamourerIpc.cs`**: 最新 IPC キー（`.V2`）対応、動的検出ポーリング。
2. **`Services/PenumbraIpc.cs`**: 最新 IPC キー（`.V5`）対応、動的検出ポーリング。
3. **`Managers/LogManager.cs` (新規)**: メモリ内ログ収集マネージャー。
4. **`Services/GameDataService.cs`**: 上限撤廃、`BNpcLink` マッピングの追加。
5. **`Managers/ActorManager.cs`**: 非人型モデル初期化の分離、武器表示フラグ制御、プレビューアクター管理。
6. **`UI/CharacterLibraryTab.cs`**: 4ボタングリッドモーダル、プレビュー専用化（Spawn ⇔ 赤色 Despawn、Gizmoトグル、Weapon Visible）。
7. **`UI/LogTab.cs` (新規)**: ゲーム内ログビューアタブ。
8. **`UI/MainWindow.cs`**: 「Log」タブ追加、タブ名整理（`Character`, `Scene`, `Settings`, `Log`）。
9. **`UI/GizmoRenderer.cs`**: ドラッグ操作当たり判定拡大・軸ラインドラッグ対応。
10. **`Models/CharacterModels.cs`**: `WeaponVisible` フラグの追加。
11. **バージョン管理・メタデータ**:
    - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json` (0.1.9.0)
    - `CHANGELOG.md`
    - `docs/character_spawn/task.md`, `walkthrough.md`
