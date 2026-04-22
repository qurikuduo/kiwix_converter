# Kiwix Converter 架构设计

## 设计目标

- 把 ZIM 档案转换成按文章拆分的 Markdown 和面向 RAG 的 JSON 产物。
- 即使长时间转换或同步任务被暂停或中断，桌面界面仍然保持可操作。
- 在 SQLite 中持久化足够的任务状态，以便按文章粒度恢复执行。
- 把 `zimdump` 和 WeKnora HTTP API 当成明确的外部边界，而不是在界面层重新实现这些协议。

## 分层结构

- `KiwixConverter.WinForms`：桌面壳层、设置、任务表格、状态展示和人工操作入口。
- `KiwixConverter.Core.Infrastructure`：应用路径、JSON 默认配置、SQLite 仓储与初始化逻辑。
- `KiwixConverter.Core.Models`：设置、ZIM 清单、任务记录、检查点、元数据、同步记录和日志模型。
- `KiwixConverter.Core.Conversion`：`zimdump` 子进程访问、正文抽取、Markdown 转换、分块和路径重写。
- `KiwixConverter.Core.Services`：目录扫描编排、转换协调、WeKnora API 访问和可恢复同步执行。

## 持久化状态

- `settings` 保存目录、`zimdump` 路径和 WeKnora 同步配置。
- `zim_library` 保存本地 ZIM 清单。
- `conversion_tasks` 与 `article_checkpoints` 保存导出进度和恢复状态。
- `weknora_sync_tasks` 与 `weknora_sync_items` 保存上传进度和恢复状态。
- `log_entries` 与 `weknora_sync_log_entries` 保存可搜索的运行历史。

## 转换流程

1. 扫描已配置的 kiwix-desktop 目录，并把发现的 ZIM 文件 upsert 到数据库。
2. 使用 `zimdump` 读取档案元数据、文章列表、正文 HTML 和关联资源。
3. 抽取正文主体，规范化链接和图片，再把清洗后的 HTML 片段转换成 Markdown。
4. 为每篇文章生成 `content.md`、`metadata.json`、`chunks.jsonl` 和图片目录。
5. 持久化文章检查点和任务心跳，使中断后只需重试失败的局部切片。

## WeKnora 同步流程

1. 从已配置的 WeKnora 服务器加载知识库，并允许用户复用或新建知识库。
2. 从 `/api/v1/models` 加载实时 `KnowledgeQA`、`Embedding`、`VLLM` 模型 ID。
3. 使用用户配置的描述、chunk size、chunk overlap 和 parent-child 选项创建知识库。
4. 在上传内容前，把所选模型 ID 应用到知识库初始化配置。
5. 以每篇文章为单位上传 Markdown 手工知识，并保存可恢复的同步检查点。

## 运行说明

- 当前 Windows 发布包是 framework-dependent，需要 .NET 8 Desktop Runtime。
- 本地从源码构建需要 .NET 8 SDK。
- 应用已兼容多种 `zimdump` 输出格式，但前提仍然是系统里存在可用的 `zimdump.exe`。