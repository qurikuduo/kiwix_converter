# リリース手順

## バージョニング

- リリースは `v0.1.2` のようなセマンティックバージョンタグを使用します。
- CI/CD やパッケージ修正では、主にパッチバージョンを増やします。
- メジャーおよびマイナーバージョンは、ユーザー向けの変更内容に応じて更新します。

## GitHub Actions フロー

1. `ci.yml` は `main` への push と pull request で実行されます。
2. `release.yml` はセマンティックバージョンのタグ、または明示的なバージョン入力による `workflow_dispatch` で実行されます。
3. release ワークフローは WinForms アプリのビルド、Windows パッケージの公開、チェックサム生成、GitHub Release の作成を行います。

## リリース資産

- `KiwixConverter-win-x64-vX.Y.Z.zip`
- `KiwixConverter-win-x64-vX.Y.Z.zip.sha256`

## リリースノート

自動生成されるリリースノートは `.github/release.yml` により制御され、機能追加、修正、ドキュメント、メンテナンスといった一般的な区分で整理されます。