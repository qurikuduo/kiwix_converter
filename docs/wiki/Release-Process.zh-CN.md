# 发布流程

## 版本规则

- 发布版本采用语义化标签，例如 `v0.1.2`。
- 针对 CI/CD 或打包修复，优先增加 patch 版本号。
- major 和 minor 版本号应对应面向用户的功能变化。

## GitHub Actions 流程

1. `ci.yml` 在 `main` 分支 push 和 pull request 时运行。
2. `release.yml` 在语义化版本标签或手动传入版本号时运行。
3. release 工作流会构建 WinForms 应用、发布 Windows 包、生成校验和并创建 GitHub Release。

## 发布资产

- `KiwixConverter-win-x64-vX.Y.Z.zip`
- `KiwixConverter-win-x64-vX.Y.Z.zip.sha256`

## Release Note

自动生成的 release note 通过 `.github/release.yml` 配置，按常见的功能、修复、文档和维护等栏目组织内容。