# Kiwix Converter Architecture

## Design Goals

- Convert ZIM archives into article-level Markdown and RAG-ready JSON artifacts.
- Keep the desktop UI operational even when long-running conversion or sync tasks are paused or interrupted.
- Persist enough task state in SQLite to resume work at article granularity.
- Treat `zimdump` and the WeKnora HTTP API as explicit external boundaries instead of reimplementing those protocols in the UI layer.

## Layered Structure

- `KiwixConverter.WinForms`: desktop shell, settings, task grids, status surfaces, and operator actions.
- `KiwixConverter.Core.Infrastructure`: application paths, JSON defaults, and SQLite repository/bootstrap logic.
- `KiwixConverter.Core.Models`: settings, scanned ZIM rows, task records, checkpoints, metadata, sync rows, and logs.
- `KiwixConverter.Core.Conversion`: `zimdump` subprocess access, content extraction, Markdown conversion, chunking, and path rewriting.
- `KiwixConverter.Core.Services`: library scan orchestration, conversion coordination, WeKnora API access, and resumable sync execution.

## Persistent State

- `settings` stores directories, `zimdump`, and WeKnora sync configuration.
- `zim_library` stores the local ZIM inventory.
- `conversion_tasks` and `article_checkpoints` store export progress and resume state.
- `weknora_sync_tasks` and `weknora_sync_items` store upload progress and resume state.
- `log_entries` and `weknora_sync_log_entries` store searchable operational history.

## Conversion Flow

1. Scan the configured kiwix-desktop directory and upsert the discovered ZIM files.
2. Use `zimdump` to read archive metadata, article lists, HTML, and linked resources.
3. Extract the main article body, normalize links and images, and convert the cleaned HTML fragment to Markdown.
4. Emit `content.md`, `metadata.json`, `chunks.jsonl`, and image folders for each article.
5. Persist article checkpoints and task heartbeats so only the failed local slice is retried after interruption.

## WeKnora Sync Flow

1. Load knowledge bases from the configured WeKnora server and let the user reuse or create a KB.
2. Load live `KnowledgeQA`, `Embedding`, and `VLLM` model IDs from `/api/v1/models`.
3. Create knowledge bases with user-configured description, chunk size, chunk overlap, and parent-child settings.
4. Apply the selected model IDs to KB initialization before uploading content.
5. Upload each exported article as manual Markdown knowledge while storing per-article sync checkpoints.

## Runtime Notes

- The packaged Windows release is framework-dependent and expects the .NET 8 Desktop Runtime.
- Local source builds require the .NET 8 SDK.
- `zimdump` version differences are handled in the client, but the app still assumes a working `zimdump.exe` is available.