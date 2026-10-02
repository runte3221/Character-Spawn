# 開発完了ウォークスルー: Character Spawn プラグイン

任意のキャラクター（NPC / MOB / カスタムキャラ）をマップ上にスポーン・配置し、エモートや表情、3Dギズモ操作、Stagehandライクなプリセット管理を提供するDalamudプラグイン「Character Spawn」の初期開発が完了しました。

---

## 主な実装機能の概要

### 1. 二段階の直感的なワークフロー
- **Step 1: Character Library（キャラクリ・ライブラリ）**
  - **外見ソース選択（AQR準拠）**:
    - **Glamourer & Penumbra**: Glamourerのデザイン文字列やPenumbraコレクションの割り当て
    - **Monster / Mob**: `BNpcName` / `BNpcBase` からゲーム内モンスターを検索・指定
    - **NPC (ENpc)**: `ENpcResident` / `ENpcBase` から既存NPCを検索・指定
    - **MCDFファイル**: Mare Synchronos形式の `.mcdf` アーカイブからデザインを自動抽出・インポート
    - **自キャラ / ターゲットコピー**: 現在のターゲットや自キャラの外観をワンクリックで複製
  - テンプレートに名前をつけて保存・管理。
- **Step 2: Stage & Scene（マップ配置・演出）**
  - ライブラリからキャラクターを選んで「Spawn onto Map」でローカル召喚。
  - 複数のスポーン中キャラクターをリスト管理し、選択したキャラクターに対して演出・設定を適用。

### 2. 位置・回転操作（3Dギズモ & UI操作のハイブリッド）
- **3Dギズモ（スクリーン投影型）**:
  - キャラクターの足元に赤(X)・緑(Y)・青(Z)の移動矢印および黄色(Yaw)の回転リングを直接描画。
  - マウスドラッグで画面上から直感的に移動・回転操作が可能。
- **UIパネル操作**:
  - X, Y, Z, Yaw のドラッグスライダー ＋ `+0.1` / `-0.1` などの微調整トグルボタン。
  - 「自キャラの位置にスナップ」「自キャラの正面1.5mに配置」のワンクリックボタン。

### 3. アニメーション・表情・演出制御
- **ActionTimeline 全モーション検索**:
  - 通常エモート、NPC専用待機ポーズ、戦闘待機などすべての `ActionTimeline` から検索可能。
- **シームレスループ再生**:
  - エモート等のワンショットモーションも待機モーション（`BaseTimeline`）として保持し、途切れずリピート再生。
- **独立した表情（Facial Expression）設定**:
  - モーションとは独立して、笑顔や目閉じなどの表情（`fac_...`）を指定・維持。
- **自キャラ視線追従（LookAt / Head Tracking）**:
  - トグルをONにすると、プレイヤーの移動に合わせてキャラクターの頭部・視線が常に自キャラを追従。

### 4. ネームプレート・ターゲット制御
- **ネームプレート**:
  - 表示 / 非表示の切り替え。
  - 自由な表示名を設定可能（空欄時はデフォルト名）。
- **ターゲット可否**:
  - クリックしてターゲットサークルを出すか、背景としてクリック不可にするかをトグルで切り替え。
- **当たり判定**:
  - クライアントサイド生成のため、プレイヤーは自由にすり抜け可能（非干渉）。

### 5. シーンプリセット管理（Stagehandライク）
- **シーン保存**:
  - 現在のマップに配置された全キャラクター（位置・向き・モーション・表情・ネーム設定）を1つの「シーン」として保存。
- **Show / Hide**:
  - シーン一覧からワンクリックで全キャラの一括スポーン（Show） / デスポーン（Hide）。
- **ゾーン（TerritoryType）連動**:
  - エリア移動時は安全に全アクターを自動デスポーン。
  - **「Auto-Spawn on Zone」**にチェックを入れたシーンは、そのマップに入場した際に自動で復元・スポーン。

---

## ファイル構成とコード構造

```
Character-Spawn/
├── .github/
│   └── workflows/
│       └── build.yml               # CI/CD (GitHub Actions自動ビルド＆リリース)
├── docs/
│   └── character_spawn/
│       ├── task.md                 # タスクリスト
│       ├── implementation_plan.md  # 実装計画書
│       └── walkthrough.md          # 完了ウォークスルー (本ファイル)
├── Managers/
│   ├── ActorManager.cs             # ローカルアクター生成・削除・Transform管理
│   ├── HeadTrackingManager.cs      # 自キャラへの視線・頭部IK追従
│   ├── NamePlateController.cs      # ネームプレートの表示名上書き・非表示化
│   └── TimelineManager.cs          # ActionTimeline再生、シームレスループ、表情設定
├── Models/
│   └── CharacterModels.cs          # テンプレート、配置、シーン、Transformデータ構造
├── Services/
│   ├── GameDataService.cs          # Luminaデータ検索 (ENpc, BNpc, ActionTimeline, 表情)
│   ├── GlamourerIpc.cs             # Glamourer IPC連携
│   ├── PenumbraIpc.cs              # Penumbra IPC連携
│   └── McdfParser.cs               # MCDFアーカイブ抽出パーサー
├── UI/
│   ├── CharacterLibraryTab.cs      # タブ1: キャラクター作成・ライブラリ
│   ├── StageSceneTab.cs            # タブ2: マップ配置、Transform、演出、シーンプリセット
│   ├── GizmoRenderer.cs            # 3Dスクリーン投影マニピュレータ
│   └── MainWindow.cs               # プラグインメインウィンドウ
├── .gitignore
├── CHANGELOG.md
├── CharacterSpawn.csproj           # .NET 10 / Dalamud API 15プロジェクトファイル
├── CharacterSpawn.json             # Dalamudプラグインマニフェスト
├── Configuration.cs                # プラグイン設定・永続化
├── package.json                    # バージョン管理メタデータ
├── Plugin.cs                       # プラグインエントリーポイント・コマンド・イベント
└── README.md
```

---

## ランチャー（/xlplugins）への登録手順

1. ゲーム内で `/xlplugins` を開く
2. 左下の **「設定（Settings）」** （歯車アイコン）をクリック
3. **「実験的（Experimental）」** タブを開く
4. **「カスタムプラグインリポジトリ（Custom Plugin Repositories）」** の入力欄に以下のURLを入力：
   ```
   https://raw.githubusercontent.com/runte3221/Character-Spawn/main/repo.json
   ```
5. 右側の **「＋」**（追加）ボタンを押し、下部の **「保存して閉じる（Save and Close）」** をクリック
6. プラグイン一覧の検索欄で **`Character Spawn`** と検索すると表示され、ワンクリックでインストール可能です。

## 起動コマンド
- `/charaspawn` または `/cspawn` でメインウィンドウの開閉が可能です。

---

## [v0.1.6] 不具合修正と改善内容
1. **「Spawn onto Map」および「Delete」ボタンが押せないUI問題の修正**:
   - `CharacterLibraryTab.cs` の一覧表示を `ImGui.BeginTable` による3列テーブルレイアウトに刷新。
   - `ImGui.Selectable` によるクリック領域の独占を解消し、ボタンが確実に反応するように修正しました。
2. **キャラクターがマップ上にスポーンしない不具合の解消**:
   - 旧方式のSigScannerによる関数呼び出し（7.xパッチでシグネチャ不整合となり失敗していた）を廃止。
   - BrioおよびA Quest Rebornで実証されている標準構造体 `ClientObjectManager.Instance()->CreateBattleCharacter` / `DeleteObjectByIndex` を採用。
   - キャラクター生成後、プレイヤー外見のコピー、モンスター/NPCモデルIDの適用、`GameObject.EnableDraw()` による確実な描画有効化を行うアーキテクチャに刷新しました。

