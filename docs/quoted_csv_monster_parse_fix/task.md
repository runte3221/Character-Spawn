# タスクリスト: クォート付き CSV 解析の修正によるフォーギヴン・テスリーン等の Mob 復元 (v0.1.53.0)

## 1. 不具合の事象と原因究明フェーズ
- [x] **事象確認**:
  - Monster/Mob 一覧で「フォーギヴン・テスリーン」が検索・表示されない。
  - HDM では `Tesleen, the Forgiven` (BaseId: 10123, ModelChara: 2632) として表示されている。
- [x] **原因究明**:
  - `GameDataService.cs` の `BuildMonsterCache()` において、`line.Split(',')` によるナイーブな分割を行っていた。
  - `mob-model-index.csv` の行データ `10123,8300,"Tesleen, the Forgiven",2632,3,141,5,1,1.5` において、`"Tesleen, the Forgiven"` 内のカンマでカラムが split され、`parts[3]` が本来の `ModelCharaId` ではなく `" the Forgiven\""` となり、`uint.TryParse` に失敗して行ごと破棄されていた。
  - 同様に英語名にカンマを含む計 17 体（統制者ハシュマリム、背徳の皇帝マティウス、聖天使アルテマ、美眼／楽聖のインク＝ゾン等）が全て脱落していた。

## 2. 設計・実装フェーズ
- [x] **`Services/GameDataService.cs` の改修**:
  - [x] クォート文字（`"`）内のカンマを保護する RFC 4180 / HDM 準拠の CSV 行パーサー関数（`ParseCsvLine`）を実装。
  - [x] `BuildMonsterCache()` 内で `line.Split(',')` の代わりに `ParseCsvLine(line)` を使用。
  - [x] パース後のカラムから不要な外側クォートをトリム。
  - [x] 他のパイプライン（NPC、Glamourer、Penumbra、CustomizePlus）に一切影響を与えないことを保証（完全隔離）。

## 3. ドキュメント・リリース・検証フェーズ
- [x] `docs/quoted_csv_monster_parse_fix/` 配下の 3 ファイル作成・同期
- [x] `CHANGELOG.md` 更新（v0.1.53.0）
- [ ] 全自動リリースパイプライン実行 (`tools/release.ps1 0.1.53.0`)
- [ ] CI/CD ビルド成功と CDN 反映の確認
