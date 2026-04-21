# Kiwix Converter

Kiwix Converter は、kiwix-desktop でダウンロードした ZIM アーカイブを、記事単位の Markdown と RAG 向け JSON に変換する WinForms + SQLite デスクトップアプリです。

## 言語版

- English: [README.md](README.md)
- 简体中文: [README.zh-CN.md](README.zh-CN.md)
- 日本語: [README.ja.md](README.ja.md)
- Español: [README.es.md](README.es.md)
- العربية: [README.ar.md](README.ar.md)

## 主な機能

- 設定された kiwix-desktop ディレクトリを走査し、利用可能な ZIM ファイルを同期します。
- `zimdump` を使ってメタデータ、記事一覧、本文 HTML、画像リソースを取得します。
- ナビゲーションやサイドバーを除外し、本文だけを Markdown に変換します。
- 各記事ごとに `content.md`、`metadata.json`、`chunks.jsonl` を出力します。
- SQLite にタスク、ログ、記事単位のチェックポイントを保存し、停止・再開・クラッシュ後の復旧をサポートします。

## 実行要件

- Windows
- .NET 8 SDK
- `zimdump` が `PATH` にある、またはアプリからパス指定できること

## 使い方

1. アプリを起動します。
2. 初回起動時に以下を設定します。
   - `kiwix-desktop` ディレクトリ
   - 既定の出力ディレクトリ
   - 必要に応じて `zimdump` 実行ファイルのパス
3. `Scan ZIM Files` を実行してローカルの ZIM を同期します。
4. ダウンロード一覧から ZIM を選び、必要ならタスクごとの出力先を上書きします。
5. 変換を開始し、タスク画面で進捗、停止/再開、ログを確認します。

## CI / Release 自動化

- [`.github/workflows/ci.yml`](.github/workflows/ci.yml) は `main` への push と PR で自動ビルドを実行します。
- [`.github/workflows/release.yml`](.github/workflows/release.yml) はセマンティックバージョンに基づくリリースを自動作成します。
- [`.github/release.yml`](.github/release.yml) はリリースノートの自動生成フォーマットを定義します。

## Wiki ソース

- 多言語 Wiki の原稿は [docs/wiki](docs/wiki) にあります。
- 現在はホームページとリリース手順を、英語・中国語・日本語・スペイン語・アラビア語で提供しています。