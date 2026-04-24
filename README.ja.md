# Kiwix Converter

[![CI](https://github.com/qurikuduo/kiwix_converter/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/qurikuduo/kiwix_converter/actions/workflows/ci.yml)
[![Release Workflow](https://github.com/qurikuduo/kiwix_converter/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/qurikuduo/kiwix_converter/actions/workflows/release.yml)
[![Latest Release](https://img.shields.io/github/v/release/qurikuduo/kiwix_converter?display_name=tag&sort=semver)](https://github.com/qurikuduo/kiwix_converter/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-F2C94C.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=.net)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6?logo=windows11)](https://github.com/qurikuduo/kiwix_converter/releases/latest)
[![UI Languages](https://img.shields.io/badge/UI%20Languages-English%20%7C%20%E7%AE%80%E4%BD%93%E4%B8%AD%E6%96%87%20%7C%20%E6%97%A5%E6%9C%AC%E8%AA%9E%20%7C%20Espa%C3%B1ol%20%7C%20%D8%A7%D9%84%D8%B9%D8%B1%D8%A8%D9%8A%D8%A9-0A7C86)](#言語版)

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
- パッケージ版アプリを実行する場合は .NET 8 Desktop Runtime
- ソースからビルドする場合は .NET 8 SDK
- `zimdump` が `PATH` にある、またはアプリからパス指定できること

## デスクトップ実行ファイル

- パッケージ版デスクトップアプリは、持ち運びと確認がしやすいよう、実行時データをできるだけ EXE と同じフォルダー側に保存します。
- SQLite の設定とタスク状態は `data/kiwix-converter.db` に保存されます。
- 起動時と実行時のトレースログは `logs/kiwix-converter-YYYY-MM-DD.log` に書き込まれます。
- パッケージフォルダーが書き込み不可の場合は `%LocalAppData%\KiwixConverter` にフォールバックします。

## スクリーンショット

以下は現在の Windows 公開ビルドから取得した実際の画面です。

![Kiwix Converter メインウィンドウ](docs/images/app-main-window.png)

## アーキテクチャ設計

- `KiwixConverter.WinForms` はデスクトップシェル、設定入力、タスク一覧、状態表示、運用フローを担当します。
- `KiwixConverter.Core` はスキャン、変換、WeKnora 同期、SQLite 永続化を担当し、UI 層を薄く保ちます。
- ZIM アーカイブへのアクセス境界は `zimdump` で、メタデータ、記事一覧、本文 HTML、リソース取得を一元化します。
- RAG 同期の境界は WeKnora HTTP API で、ナレッジベース探索、モデル読込、KB 作成、記事アップロードを扱います。
- 長時間処理は記事単位のチェックポイントを持つ永続化タスクとして管理されるため、再起動後も全アーカイブのやり直しは不要です。

## 技術フロー

1. ディレクトリ走査でローカル ZIM 在庫を SQLite に upsert してから変換を開始します。
2. 変換では `zimdump` からメタデータと記事 HTML を取得し、本文抽出、リンク書き換え、画像出力、Markdown と JSON の生成を行います。
3. 各記事は `content.md`、`metadata.json`、`chunks.jsonl`、チェックポイントを書き出すため、失敗してもその局所スライスだけを再試行できます。
4. WeKnora 同期では完了済みエクスポートを読み込み、`/api/v1/models` からライブのモデル ID を取得し、chunk 設定付きの KB を解決または作成し、記事単位の Markdown を再開可能な形で送信します。

## 初心者向けクイックスタート

アプリを使うだけなら、GitHub Release の Windows zip をダウンロードし、.NET 8 Desktop Runtime をインストールするのが最も簡単です。Visual Studio で開いたり、`dotnet build` で自分でビルドしたりする場合だけ .NET 8 SDK が必要です。

### 1. .NET のインストール

- パッケージ版アプリを実行するだけなら Windows x64 用の .NET 8 Desktop Runtime をインストールします。
- ソースからビルドするなら .NET 8 SDK をインストールします。
- インストール後は、ターミナルやアプリを再起動して `dotnet` が `PATH` で見えることを確認してください。

### 2. `zimdump` のインストール

Kiwix Converter は ZIM を直接読まず、Kiwix ツール群の `zimdump` を呼び出します。

Windows での一般的な手順:

1. `zimdump.exe` を含む Kiwix tools パッケージをダウンロードします。
2. `C:\Kiwix\tools\` のような固定フォルダーに展開します。
3. 次のどちらかを行います。
   - そのフォルダーを Windows の `PATH` に追加する
   - `PATH` は変更せず、初回起動時に `zimdump.exe` を手動で選択する

### 3. 起動時の依存関係チェック

アプリは起動時に `zimdump` の有無を自動確認します。

- `zimdump` が見つかれば、そのまま変換できます。
- 見つからない場合は警告を表示し、すぐに `zimdump.exe` を参照できます。
- 依存関係が未設定でもアプリ自体は開いたままにできますが、変換とメタデータ抽出は利用できません。

### 4. WeKnora 同期の設定

最初の組み込み RAG 同期先は WeKnora です。

`WeKnora Sync Configuration` で次を設定します。

- WeKnora のベース URL
- 認証方式: `API Key` または `Bearer Token`
- アクセストークン
- ナレッジベース ID またはナレッジベース名
- `/api/v1/models` から取得できる `KnowledgeQA`、`Embedding`、`VLLM` の各モデル ID（任意）
- 指定名が存在しない場合にナレッジベースを自動作成するかどうか

同期 UI では以下を行えます。

- サーバーからナレッジベース一覧を読み込む
- 同期前に接続テストを行う
- ナレッジベース作成時や同期開始時に設定済みのチャット、Embedding、マルチモーダルモデルを再適用する
- 完了済みの変換出力を選択して同期する
- 同期履歴、ログ、進捗、ETA、停止/再開状態を監視する

## 使い方

1. パッケージ版を使う場合は .NET 8 Desktop Runtime を、ソースから実行する場合は .NET 8 SDK をインストールします。
2. `zimdump` をインストールします。
3. アプリを起動します。
4. 初回起動時に以下を設定します。
   - `kiwix-desktop` ディレクトリ
   - 既定の出力ディレクトリ
   - 必要に応じて `zimdump` 実行ファイルのパス
5. 起動チェックで `zimdump` が見つからない場合は、`zimdump.exe` を指定するか `PATH` を修正します。
6. `Scan ZIM Files` を実行してローカルの ZIM を同期します。
7. ダウンロード一覧から ZIM を選び、必要ならタスクごとの出力先を上書きします。
8. 変換を開始し、タスク画面で進捗、停止/再開、ログを確認します。
9. WeKnora に送る場合は `WeKnora Sync` を開き、完了済みの変換出力を選んで同期タスクを開始します。

## CI / Release 自動化

- [`.github/workflows/ci.yml`](.github/workflows/ci.yml) は `main` への push と PR で自動ビルドを実行します。
- [`.github/workflows/release.yml`](.github/workflows/release.yml) は `main` への各 push ごとに、最新タグを基準に次の patch バージョンを自動計算して GitHub Release を公開します。
- 同じ release workflow は `workflow_dispatch` にも対応しており、必要な場合はバージョン番号を手動で上書きできます。
- [`.github/release.yml`](.github/release.yml) はリリースノートの自動生成フォーマットを定義します。

## Wiki ソース

- 多言語 Wiki の原稿は [docs/wiki](docs/wiki) にあります。
- 現在はホームページとリリース手順を、英語・中国語・日本語・スペイン語・アラビア語で提供しています。

## ライセンス

このプロジェクトは MIT License の下で公開されています。詳細は [LICENSE](LICENSE) を参照してください。