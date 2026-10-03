# 実装計画: クォート付き CSV 解析の修正によるフォーギヴン・テスリーン等の Mob 復元 (v0.1.53.0)

## 1. 概要
Character Spawn の Monster/Mob 一覧において、「フォーギヴン・テスリーン」をはじめとするカンマを含む名前のモンスター（全 17 体）がリストから欠損していた問題を解消する。

## 2. 根本原因の技術的分析
`GameDataService.cs` の `BuildMonsterCache()` 内で、CSV 行を `line.Split(',')` で分割していたため、`"Tesleen, the Forgiven"` のようにダブルクォーテーションで囲まれたカンマ付きフィールドが分断されていた。
その結果、カラムのインデックスが後方に 1 つずつずれ、本来 `ModelCharaId`（2632）が入るべき `parts[3]` に `" the Forgiven\""` が渡り、`uint.TryParse` で弾かれてスキップされていた。

## 3. 実装方針
1. **RFC 4180 / HDM 準拠の CSV 行分割関数の導入 (`GameDataService.cs`)**:
   - `private static List<string> ParseCsvLine(string line)` を追加。
   - 文字列を一文字ずつ走査し、`inQuotes` フラグでクォート内部を追跡。
   - クォート内のカンマは区切り文字として扱わず、保護する。
   - 各フィールドの両端の空白および二重引用符（`"`）を自動トリム。
2. **`BuildMonsterCache()` での適用**:
   - `var parts = ParseCsvLine(line);` に置き換え。
   - `baseId`, `nameId`, `modelCharaId`, `mcType`, `scale` の取得が正確に行われ、`nameId`（8300）から `BNpcName` シートの日本語名「フォーギヴン・テスリーン」が正常に解決される。
3. **他機能への完全隔離**:
   - 変更は `GameDataService.cs` の CSV 解析関数のみに留まり、アクター生成、Glamourer、Penumbra、CustomizePlus 等の他のロジックには一切触れない。

## 4. 検証手順
- スクリプトによる全 CSV 行の整合性チェック（17 件のクォート行がすべて 9 カラム以上でパースされ、モデル ID が正しく抽出されること）。
- GitHub Actions CI/CD ビルドと Fastly CDN 反映確認。
