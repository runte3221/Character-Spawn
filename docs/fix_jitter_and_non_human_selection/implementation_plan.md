# 実装計画: 追従停止時の上下ジッター解消および全モデル種別のクリック選択対応

## 背景と問題の所在
1. **階段・段差での激しい上下振動（ジッター）**:
   - `MovementService.cs` において、停止距離到達時に足元を地面にスナップさせる処理（`TryGetGroundHeight`）の直後、プレイヤーがターゲットの場合に `yDiffAtStop = diff.Y` を用いてプレイヤーの Y 座標へ引っ張る処理が実行されていた。
   - 階段や段差の境界にいる場合、プレイヤーとアクターの高低差により「地面への引き上げ」と「プレイヤーへの引き下げ」が毎フレーム交互に競合し、1 秒間に数十回の高速な上下ジッターが発生していた。
2. **モンスター・デミヒューマン・マウント・ミニオンがクリック選択できない**:
   - **原因 A (ゲーム内ターゲット連動)**:
     - パイプライン D で生成されるモンスター、デミヒューマン（サキュバス等）、マウント、ミニオンは、`CopyFromCharacter` や非同期再描画の過程でゲームエンジンの `GameObject.TargetableStatus` が `0`（ターゲット不可）にリセットされていた。
     - そのためゲーム画面上で左クリックしてもターゲットサークルが出ず、`TargetManager.Target` による自動選択が働かなかった。
   - **原因 B (3D スクリーン空間直接クリック判定)**:
     - `GizmoRenderer.cs` の `CheckActorClickSelection` が人型固定（高さ 1.85m、幅が高さの 45%）でスクリーン矩形を計算していた。
     - 横幅や体長が数メートルある大型モンスターやマウントは左右の胴体や翼がクリック判定から外れ、逆に体高 0.3〜0.5m のミニオンは頭上の虚空を判定していて本体をクリックしても当たらなかった。

---

## 修正内容詳細

### 1. `Services/MovementService.cs`
- `StepTowardTarget` の停止処理（360〜390行目付近）から、競合していた `yDiffAtStop` によるプレイヤー Y 座標への引っ張り処理を完全削除。
- 停止時の高度はすべて `TryGetGroundHeight`（ネイティブ BGCollision 地面レイキャスト）に一元化し、階段や斜面にピタッと美しく静止させる。

### 2. `Managers/ActorManager.cs`
- `MonsterRedrawJob` 完了時（891行目付近）に `chara->GameObject.TargetableStatus |= ObjectTargetableFlags.IsTargetable;` を明示再設定。
- `EnforceActorDrawState`（`UpdateFrame`、毎フレーム実行）において、全アクティブアクターの `TargetableStatus |= ObjectTargetableFlags.IsTargetable;` を常時維持（enforce）。
- これにより、全種別のアクターがゲーム画面上で通常通り左クリックでターゲット可能になり、`TargetManager.Target` 自動同期が確実に機能する。

### 3. `UI/GizmoRenderer.cs`
- `CheckActorClickSelection` に `IObjectTable` を導入。
- 各アクターのゲーム内実座標（`ICharacter.Position`）およびモデル固有の当たり判定半径（`HitboxRadius`）を取得。
- カメラの右方向ベクトル（`cameraRight`）を用いて、3D 空間上のモデル半径 $R$ をスクリーン空間の左右幅として正確に投影。
- モデル種別（ミニオン、マウント、大型モンスター、人型）に応じた適切な頭上高さ $H$ を算出し、最小クリック領域（28px）を保証。
- サキュバスの浮遊モデルやミニオン、巨大マウントでも全身を直感的にクリック＆ホバー選択可能にする。

### 4. バージョン更新・ドキュメント・CI/CD
- `tools/bump-version.ps1 0.1.90.0`
- `CHANGELOG.md` 追記
- `docs/fix_jitter_and_non_human_selection/walkthrough.md` 作成
- コミット＆プッシュ（PowerShell `;` 結合）
- GitHub Actions CI/CD ビルド完了確認
