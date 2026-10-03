# 実装計画: Edit非表示時のターゲット可否制御（カスタムネーム表示中のみターゲット可能化）

## 背景と問題の所在
- **現状**:
  - v0.1.89 / v0.1.90 で 3D 直接クリック選択のためアクターの `TargetableStatus` をデフォルト有効化したことにより、通常プレイ時にも全カスタムスポーン（名前非表示のモブや背景キャラクター等）がゲーム画面上でターゲット（左クリックや Tab 選択）可能となっていた。
- **ユーザーの要望**:
  - Edit（SceneEditWindow）を開いていない時は、**「カスタムネームを表示しているもの（`[x] Custom Name`）以外はターゲットできない」** ようにしてほしい。
  - これにより、通常プレイ時に背景モブやモンスターを誤ってターゲットすることを完全に防止し、公式 NPC のような自然な存在感（名前を表示させた主要 NPC だけターゲット可能）を実現する。
  - Edit を開いている間は、編集作業を円滑に行うため全アクターをターゲット可能とする。

---

## 修正内容詳細

### 1. `Managers/ActorManager.cs`
- `SetTargetablePolicy(Func<bool> isEditOpen, Func<SpawnedActorData, bool>? isCustomNameShown)` を新設。
- `EnforceActorDrawState`（`UpdateFrame` 毎フレーム実行）において：
  - `bool isEditOpen = isEditOpenFunc != null && isEditOpenFunc();`
  - `bool isCustomNameShown = isCustomNameShownFunc != null ? isCustomNameShownFunc(actor) : actor.NamePlate.Show;`
  - `bool shouldBeTargetable = actor.IsTargetable && (isEditOpen || isCustomNameShown);`
  - `shouldBeTargetable == true` の場合: `chara->GameObject.TargetableStatus |= ObjectTargetableFlags.IsTargetable;`
  - `shouldBeTargetable == false` の場合: `chara->GameObject.TargetableStatus &= ~ObjectTargetableFlags.IsTargetable;`
- これにより、Edit ウィンドウを閉じた瞬間、カスタムネーム非表示のアクターのターゲットフラグが自動解除され、通常プレイ時の誤ターゲットが 100% 解消される。

### 2. `Plugin.cs`
- 初期化時（UI・Manager 生成後）に `actorManager.SetTargetablePolicy` を呼び出し：
  - `isEditOpen`: `() => sceneEditWindow.IsOpen`
  - `isCustomNameShown`: 各アクターの配置データ（`sceneManager.GetPlacementForActor(actor)`）の `ShowCustomName` を判定。

### 3. バージョン更新・ドキュメント・CI/CD
- `tools/bump-version.ps1 0.1.92.0`
- `CHANGELOG.md` 追記
- `docs/edit_mode_targetable_rules/walkthrough.md` 作成
- コミット＆プッシュ（PowerShell `;` 結合）
- GitHub Actions CI/CD ビルド完了確認
