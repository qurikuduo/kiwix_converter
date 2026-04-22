# Architecture Review

## Goals

- Convert Kiwix-downloaded ZIM archives into article-level Markdown.
- Preserve multilingual UTF-8 text, including Chinese, Japanese, Korean, Arabic, and English content.
- Produce RAG-ready artifacts with chunked text and comprehensive metadata.
- Support pause/resume, crash recovery, and history/log inspection through a WinForms desktop UI.

## Architectural Split

### `KiwixConverter.Core`

- `Infrastructure`
  - Application data paths
  - JSON defaults
  - SQLite repository and schema bootstrap
- `Models`
  - Settings, ZIM library rows, tasks, checkpoints, archive metadata, logs, RAG chunks
- `Conversion`
  - `ZimdumpClient` for subprocess execution and resilient parsing of `zimdump` output
  - `ContentTransformService` for main content extraction, formula normalization, Markdown conversion, and chunking
  - `LinkPathService` for deterministic article/image path mapping and internal link rewriting
- `Services`
  - `LibraryScanner` for directory sync
  - `ConversionCoordinator` for task orchestration, checkpointing, snapshots, and export writing
  - `WeKnoraClient` for HTTP access to knowledge bases, models, and manual knowledge endpoints
  - `WeKnoraSyncCoordinator` for resumable sync task execution and per-article upload tracking
  - `KiwixAppService` as the UI-facing application service boundary

### `KiwixConverter.WinForms`

- Single main form with:
  - required directory/tool configuration
  - scanned download list
  - per-task output override
  - task controls for convert, pause, resume
  - searchable history
  - searchable logs
  - completion balloon notifications

## Data Model

The SQLite schema is centered on conversion and sync state:

- `settings`: configured directories and snapshot interval
- `zim_library`: synced ZIM files discovered in the configured kiwix-desktop directory
- `conversion_tasks`: one record per export run, including progress and pause state
- `article_checkpoints`: per-article completion or skip state used for resume
- `log_entries`: queryable operational logs
- `weknora_sync_tasks`: one record per WeKnora upload run, including progress, pause state, and target KB
- `weknora_sync_items`: per-article upload checkpoints used for resumable WeKnora sync
- `weknora_sync_log_entries`: queryable sync-specific logs

Archive metadata is stored separately per task and also emitted into `archive-metadata.json` for external inspection.

## Conversion Pipeline

1. Validate task settings and ensure the ZIM file still exists.
2. Query archive metadata and article listing through `zimdump`.
3. Resume from existing article checkpoints if the task already ran before.
4. For each article:
   - fetch HTML through `zimdump`
   - extract the main article body via ranked DOM selectors
   - normalize math markup into Markdown-compatible inline or block formulas
   - rewrite image sources to exported local files
   - rewrite internal article links to local `content.md` targets
   - convert the cleaned HTML fragment into Markdown
   - chunk large Markdown into RAG-ready JSONL
   - write `content.md`, `metadata.json`, `chunks.jsonl`
   - update the SQLite checkpoint row
5. Periodically update the task heartbeat and `task-state.json` snapshot.
6. Mark the task completed, paused, or faulted.

## WeKnora Sync Pipeline

1. Validate the saved base URL, auth mode, access token, and KB selection strategy.
2. Load knowledge bases from the WeKnora API and optionally auto-create a KB when a configured name is missing.
3. Load live `KnowledgeQA`, `Embedding`, and `VLLM` model IDs from `/api/v1/models` and apply them to KB initialization.
4. Walk the completed Markdown exports and upload each article as manual knowledge.
5. Persist per-article sync checkpoints, sync logs, and sync task heartbeat state so pause/resume works the same way as conversion.

## Why `zimdump` Subprocesses

The design intentionally keeps raw archive parsing out of the C# process. `zimdump` is treated as the trusted archive reader because it is purpose-built for ZIM handling and simplifies the desktop application to orchestration, extraction cleanup, and export generation.

## Recovery Semantics

- Pause and resume are article-granular.
- If the app closes or crashes during an article, that article is retried on resume.
- Completed articles are never regenerated unless a new task is created.
- Snapshot files mirror database state so in-progress task status is externally visible in the export directory.

## Known Runtime Assumptions

- `zimdump` output format can vary across versions, so the client uses multiple parsing strategies for article enumeration.
- Some mathematical content arrives as MathML, some as plain Unicode, and some as images. The current implementation preserves text and normalizes MathML best-effort, but formula fidelity still depends on the source article markup.
- The current Windows environment now has the .NET 8 SDK installed, and both source builds and packaged EXE startup checks were revalidated locally.