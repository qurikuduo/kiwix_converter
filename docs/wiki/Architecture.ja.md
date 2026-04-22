# Kiwix Converter アーキテクチャ

## 設計目標

- ZIM アーカイブを記事単位の Markdown と RAG 向け JSON 成果物へ変換すること。
- 長時間の変換や同期が停止・中断されても、デスクトップ UI を操作可能なまま保つこと。
- 記事粒度で再開できるだけのタスク状態を SQLite に永続化すること。
- `zimdump` と WeKnora HTTP API を明示的な外部境界として扱い、UI 層でそれらのプロトコルを再実装しないこと。

## レイヤー構成

- `KiwixConverter.WinForms`: デスクトップシェル、設定、タスク一覧、状態表示、オペレーター操作。
- `KiwixConverter.Core.Infrastructure`: アプリパス、JSON 既定値、SQLite リポジトリと初期化。
- `KiwixConverter.Core.Models`: 設定、ZIM 一覧、タスク記録、チェックポイント、メタデータ、同期行、ログ。
- `KiwixConverter.Core.Conversion`: `zimdump` 子プロセス、本文抽出、Markdown 変換、チャンク化、パス書き換え。
- `KiwixConverter.Core.Services`: ディレクトリ走査、変換調停、WeKnora API アクセス、再開可能な同期実行。

## 永続化状態

- `settings` はディレクトリ、`zimdump`、WeKnora 同期設定を保持します。
- `zim_library` はローカル ZIM 在庫を保持します。
- `conversion_tasks` と `article_checkpoints` はエクスポート進捗と再開状態を保持します。
- `weknora_sync_tasks` と `weknora_sync_items` はアップロード進捗と再開状態を保持します。
- `log_entries` と `weknora_sync_log_entries` は検索可能な運用履歴を保持します。

## 変換フロー

1. 設定済みの kiwix-desktop ディレクトリを走査し、発見した ZIM をデータベースへ upsert します。
2. `zimdump` を使ってアーカイブメタデータ、記事一覧、HTML、関連リソースを取得します。
3. 本文を抽出し、リンクと画像を正規化して、クリーンな HTML 断片を Markdown に変換します。
4. 各記事について `content.md`、`metadata.json`、`chunks.jsonl`、画像フォルダーを出力します。
5. 記事チェックポイントとタスク heartbeat を保存し、中断後は失敗した局所スライスだけを再試行します。

## WeKnora 同期フロー

1. 設定済み WeKnora サーバーからナレッジベースを読み込み、再利用または新規作成を選べます。
2. `/api/v1/models` からライブの `KnowledgeQA`、`Embedding`、`VLLM` モデル ID を読み込みます。
3. 説明、chunk size、chunk overlap、parent-child 設定を使って KB を作成します。
4. アップロード前に選択されたモデル ID を KB 初期化へ適用します。
5. 各記事の Markdown を手動ナレッジとして送信しつつ、再開可能な同期チェックポイントを保存します。

## 実行メモ

- 現在の Windows リリースは framework-dependent なので .NET 8 Desktop Runtime が必要です。
- ソースからのローカルビルドには .NET 8 SDK が必要です。
- 複数の `zimdump` 出力形式に対応していますが、利用可能な `zimdump.exe` が存在することは前提です。