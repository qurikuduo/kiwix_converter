# Kiwix Converter Wiki

欢迎来到 Kiwix Converter 的 wiki。

## 概览

Kiwix Converter 用于把 kiwix-desktop 下载的 ZIM 档案转换成按文章拆分的 Markdown、元数据 JSON 和面向 RAG 的 JSONL 分块文件。

## 主要内容

- 桌面界面：配置、扫描、任务控制、历史和日志
- 转换引擎：`zimdump` 集成、正文抽取、Markdown 转换、图片导出和链接重写
- 持久化：基于 SQLite 的设置、任务检查点、档案元数据和日志
- 发布自动化：CI 构建打包和语义化版本发布

## 架构与数据流

- `KiwixConverter.WinForms` 负责面向操作员的桌面壳层，`KiwixConverter.Core` 负责扫描、转换、同步和持久化。
- 目录扫描会先把 ZIM 清单 upsert 到 SQLite，再进入转换阶段。
- 转换通过 `zimdump` 获取元数据和文章 HTML，然后生成 Markdown、元数据 JSON、分块文件和图片目录。
- WeKnora 同步可以从 `/api/v1/models` 加载实时模型 ID，按 chunk 参数创建或复用知识库，并以可恢复任务状态上传文章 Markdown。

## 普通用户快速上手

如果你不是开发者，最省事的路径是：

1. 下载最新的 Windows Release zip 包。
2. 安装 .NET 8 Desktop Runtime。
3. 安装 `zimdump`，并把它加入 `PATH`，或者在程序里手动选择 `zimdump.exe`。
4. 启动程序，设置 `kiwix-desktop` 目录和输出目录，然后扫描 ZIM 档案。

如果你不是直接使用发布包，而是打算从源码构建，请改为安装 .NET 8 SDK。

## 启动依赖检查

桌面程序现在会在启动时检查 `zimdump`。

- 找到 `zimdump`：可以直接开始转换。
- 未找到 `zimdump`：程序会提示，并允许你立即浏览选择可执行文件。
- 即使暂时没有配好 `zimdump`，程序仍可继续打开，但导出相关功能会保持不可用。

## WeKnora 同步

程序内置的第一阶段 RAG 同步目标是 WeKnora。

当前桌面流程支持：

- 配置基础地址，以及 `API Key` / `Bearer Token` 两种认证方式
- 从 WeKnora 服务器读取知识库列表
- 在允许时按名称自动创建知识库
- 配置 `KnowledgeQA`、`Embedding`、`VLLM` 模型 ID，可从 `/api/v1/models` 获取
- 在创建知识库或启动同步时，重新应用已配置的聊天、Embedding、多模态模型
- 选择哪些已完成转换的档案需要同步
- 查看同步历史、按任务日志、进度、ETA、暂停/恢复，以及可恢复的断点状态

## 语言页面

- [Home](Home)
- [首页（简体中文）](Home.zh-CN)
- [ホーム（日本語）](Home.ja)
- [Inicio (Español)](Home.es)
- [الصفحة الرئيسية (العربية)](Home.ar)

## 其他页面

- [架构设计](Architecture)
- [Release Process](Release-Process)
- [发布流程（简体中文）](Release-Process.zh-CN)
- [リリース手順（日本語）](Release-Process.ja)
- [Proceso de release (Español)](Release-Process.es)
- [عملية الإصدار (العربية)](Release-Process.ar)

## 安装说明补充

- `zimdump` 不是本仓库自带的文件，它来自 Kiwix 工具链。
- 当前发布出来的 Windows 包是 framework-dependent，因此建议在首次运行前先安装 .NET 8 Desktop Runtime。
- 如果要在本地自行构建，仍然需要 .NET 8 SDK。