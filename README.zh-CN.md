# Kiwix Converter

Kiwix Converter 是一个基于 WinForms + SQLite 的桌面工具，用于把 kiwix-desktop 下载的 ZIM 文件转换成按文章拆分的 Markdown 与 RAG 友好型 JSON 产物。

## 语言版本

- English: [README.md](README.md)
- 简体中文: [README.zh-CN.md](README.zh-CN.md)
- 日本語: [README.ja.md](README.ja.md)
- Español: [README.es.md](README.es.md)
- العربية: [README.ar.md](README.ar.md)

## 核心能力

- 扫描已配置的 kiwix-desktop 下载目录，并同步本地 ZIM 文件列表。
- 通过 `zimdump` 读取 ZIM 元数据、文章列表、正文 HTML 和图片资源。
- 只提取正文区域，移除导航和侧边栏，重写内部链接到本地 Markdown 路径。
- 输出 `content.md`、`metadata.json`、`chunks.jsonl`，满足 RAG 导入需要。
- 以 SQLite 保存任务、日志和文章级检查点，支持暂停、恢复和跨会话续跑。

## 运行要求

- Windows
- .NET 8 SDK
- `zimdump` 已安装，并且在 `PATH` 中或可在界面里手动指定路径

## 项目结构

```text
KiwixConverter.sln
src/
  KiwixConverter.Core/
  KiwixConverter.WinForms/
docs/
  architecture.md
  wiki/
```

## 使用流程

1. 启动程序。
2. 首次运行时设置：
   - `kiwix-desktop` 目录
   - 默认输出目录
   - 可选的 `zimdump` 可执行文件路径
3. 点击 `Scan ZIM Files` 扫描本地档案。
4. 在下载列表中选择 ZIM，必要时填写单任务输出覆盖目录。
5. 启动转换任务，并在任务页查看进度、暂停/恢复状态和日志。

## 自动化构建与发布

- [`.github/workflows/ci.yml`](.github/workflows/ci.yml) 会在 `main` 分支 push 和 PR 时自动编译并上传 Windows 构建产物。
- [`.github/workflows/release.yml`](.github/workflows/release.yml) 会在语义化版本发布时自动打包并创建 GitHub Release。
- [`.github/release.yml`](.github/release.yml) 定义自动生成的 release note 模板与分类。

## Wiki 源文档

- 多语言 wiki 内容保存在 [docs/wiki](docs/wiki) 目录下。
- 当前已提供英文、中文、日文、西班牙文、阿拉伯文的主页与发布流程页面。