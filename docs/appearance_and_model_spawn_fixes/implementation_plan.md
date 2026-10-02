# 実装計画: AQR完全準拠のMCDF Mod展開＆Penumbra一時コレクション連携

## 目的
1. **MCDF完全互換**: 他ユーザーから受け取ったMCDF（自環境にModが未インストールの状態）でも、MCDF内に同梱されている3Dモデル、テクスチャ、マテリアル、FileSwap、MetaManipulationをローカルキャッシュに抽出し、Penumbraの一時コレクション（Temporary Collection）に登録することで100%外見を再現する（A Quest Reborn完全準拠）。
2. **Penumbra手動コレクションの排除（MCDF時）**: MCDF利用時にユーザーに手動でPenumbraコレクションを選ばせる方式を廃止し、AQRと同様に全自動で一時コレクションを生成・適用する。
3. **リソースライフサイクル管理**: アクターのデスポーン時やエリア移動時に、Penumbraの一時コレクションを確実に解放・削除する。
4. **バージョン管理の確実化**: XIVLauncherが古いv0.1.9フォルダ等を読み込むことによる不整合を防ぐ。

## 設計詳細
- **McdfParser**:
  - LZ4ストリーム解凍後、JSON内の `Files` 配列から各ファイルの実バイナリを `mcdf_cache` フォルダに展開。
  - `FileSwaps` および `Files` のゲーム内パスとキャッシュファイルパスのマッピングテーブル（`ModPaths`）を構築。
- **PenumbraIpc**:
  - `Penumbra.CreateTemporaryCollection.V6`: 一時コレクション生成。
  - `Penumbra.AssignTemporaryCollection.V5`: スポーンアクター（`globalIndex`）に一時コレクションを割り当て。
  - `Penumbra.AddTemporaryMod.V5`: 抽出されたModファイル群および `ManipulationData` を一時Modとしてコレクションに登録。
  - `Penumbra.DeleteTemporaryCollection.V5`: デスポーン時に一時コレクションを削除。
- **ActorManager**:
  - スポーン時に上記Penumbra一時コレクションサイクルを実行後、Glamourerデザインを適用し、Penumbra Redrawを実行。
  - デスポーン時に `TemporaryCollectionGuid` を参照してコレクションを解放。
