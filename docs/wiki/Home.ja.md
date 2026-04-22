# Kiwix Converter Wiki

Kiwix Converter wiki へようこそ。

## 概要

Kiwix Converter は、kiwix-desktop の ZIM アーカイブを記事単位の Markdown、メタデータ JSON、RAG 向け JSONL チャンクに変換します。

## 主な領域

- デスクトップ UI: 設定、スキャン、タスク制御、履歴、ログ
- 変換エンジン: `zimdump` 連携、本文抽出、Markdown 変換、画像出力、リンク書き換え
- 永続化: SQLite による設定、タスクチェックポイント、アーカイブメタデータ、ログ
- リリース自動化: CI ビルド、パッケージ化、セマンティックバージョンのリリース

## クイックスタート

非開発者にとって最も簡単な流れは次のとおりです。

1. 最新の Windows Release zip をダウンロードします。
2. .NET 8 Desktop Runtime をインストールします。
3. `zimdump` をインストールし、`PATH` に追加するか、アプリ内で `zimdump.exe` を選択します。
4. アプリを起動し、`kiwix-desktop` ディレクトリと出力先を設定してから ZIM をスキャンします。

ソースからビルドする場合は .NET 8 SDK が必要です。

## 起動時の依存関係チェック

デスクトップアプリは起動時に `zimdump` をチェックします。

- `zimdump` が利用可能なら変換の準備は完了です。
- `zimdump` が見つからない場合は警告を出し、その場で実行ファイルを選択できます。
- `zimdump` が未設定でもアプリは開いたままにできますが、エクスポート機能は利用できません。

## WeKnora 同期

最初の組み込み RAG 同期先は WeKnora です。

現在のデスクトップフローでは以下をサポートします。

- ベース URL と `API Key` / `Bearer Token` 認証
- WeKnora サーバーからのナレッジベース一覧取得
- 有効時の名前ベース自動作成
- 完了した変換出力の選択と同期
- 同期履歴、タスク別ログ、進捗、ETA、停止/再開、再開可能なチェックポイント

## 言語ページ

- [Home](Home)
- [首页（简体中文）](Home.zh-CN)
- [ホーム（日本語）](Home.ja)
- [Inicio (Español)](Home.es)
- [الصفحة الرئيسية (العربية)](Home.ar)

## 追加ページ

- [Release Process](Release-Process)
- [发布流程（简体中文）](Release-Process.zh-CN)
- [リリース手順（日本語）](Release-Process.ja)
- [Proceso de release (Español)](Release-Process.es)
- [عملية الإصدار (العربية)](Release-Process.ar)

## インストール補足

- `zimdump` はこのリポジトリには含まれず、Kiwix ツール群から取得します。
- 現在の Windows リリースは framework-dependent なので、初回起動前に .NET 8 Desktop Runtime を入れておくことを推奨します。
- ローカルビルドには引き続き .NET 8 SDK が必要です。