# Kiwix Converter

Kiwix Converter is a WinForms + SQLite desktop application for exporting kiwix-desktop downloaded ZIM archives into article-level Markdown and RAG-ready JSON artifacts.

## Language Editions

- English: [README.md](README.md)
- 简体中文: [README.zh-CN.md](README.zh-CN.md)
- 日本語: [README.ja.md](README.ja.md)
- Español: [README.es.md](README.es.md)
- العربية: [README.ar.md](README.ar.md)

## What It Does

- Scans a configured kiwix-desktop download directory and syncs available ZIM files into the application database.
- Uses `zimdump` as the archive access layer for metadata, article listing, HTML extraction, and resource export.
- Extracts only the main article body, rewrites internal links to local Markdown paths, exports images into article-specific folders, and preserves UTF-8 text for CJK and Arabic content.
- Writes article-level checkpoint state into SQLite so paused or interrupted tasks resume from the last article boundary instead of restarting the full archive.
- Produces Markdown, per-article metadata JSON, chunked JSONL for RAG ingestion, and root-level archive/task snapshot files.

## Desktop Localization

- The WinForms app now auto-detects the current Windows UI language on startup.
- Supported desktop UI languages match the README and wiki set: English, Simplified Chinese, Japanese, Spanish, and Arabic.
- When the system language is not one of the supported languages, the app falls back to English.
- Arabic uses a right-to-left shell layout automatically.

## Solution Layout

```text
KiwixConverter.sln
src/
  KiwixConverter.Core/
    Conversion/
    Infrastructure/
    Models/
    Services/
  KiwixConverter.WinForms/
docs/
  architecture.md
```

## Architecture

- `KiwixConverter.WinForms` owns the desktop shell, settings entry, task grids, status surfaces, and operator workflows.
- `KiwixConverter.Core` owns scanning, conversion, WeKnora sync, and SQLite-backed persistence so the UI stays thin.
- `zimdump` is the archive access boundary for ZIM metadata, article lists, HTML, and resources.
- The WeKnora HTTP API is the RAG sync boundary for knowledge base discovery, model loading, KB creation, and article upload.
- Long-running work is modeled as persisted tasks with article-level checkpoints, so restarts do not force a full archive reconversion.

## Technical Flow

1. Directory scanning upserts the local ZIM inventory into SQLite before any conversion begins.
2. Conversion calls `zimdump` for metadata and article HTML, extracts the main content, rewrites links, exports images, and emits Markdown plus JSON artifacts.
3. Each article writes `content.md`, `metadata.json`, `chunks.jsonl`, and checkpoint state so a failure only retries the local slice that actually failed.
4. WeKnora sync reads completed exports, loads live model IDs from `/api/v1/models`, resolves or creates knowledge bases with the configured chunk settings, and uploads per-article Markdown as resumable manual knowledge.

## Build Requirements

- Windows
- .NET 8 Desktop Runtime for running the packaged app
- .NET 8 SDK if you want to build the project from source
- `zimdump` available either on `PATH` or via the configured executable path in the UI

## Beginner Quick Start

If you only want to use the app, take the GitHub release zip and install the .NET 8 Desktop Runtime. You only need the full .NET 8 SDK when you plan to open the solution in Visual Studio or run `dotnet build` yourself.

### 1. Install .NET

- To run the packaged desktop app: install the .NET 8 Desktop Runtime for Windows x64 from the official .NET download page.
- To build from source: install the .NET 8 SDK from the same download page.
- After installation, reopen any terminals so the new `dotnet` command is visible on `PATH`.

### 2. Install `zimdump`

Kiwix Converter does not read ZIM files directly. It shells out to `zimdump`, which is part of the Kiwix tooling.

Typical Windows setup:

1. Download a Kiwix tools package that contains `zimdump.exe`.
2. Extract it to a stable folder such as `C:\Kiwix\tools\`.
3. Either:
  - add that folder to your Windows `PATH`, or
  - keep the executable where it is and point the app to `zimdump.exe` on first launch.

To add `zimdump` to `PATH` on Windows:

1. Open `System Properties`.
2. Open `Environment Variables`.
3. Edit the user or system `Path` variable.
4. Add the folder that contains `zimdump.exe`.
5. Close and reopen the app.

### 3. First Launch Checks

On startup, the app now verifies whether `zimdump` is available.

- If `zimdump` is found, the app continues normally.
- If `zimdump` is missing, the app shows a warning and lets you browse to `zimdump.exe` immediately.
- You can still keep the app open without `zimdump`, but conversion and metadata extraction will remain unavailable until the dependency is configured.

### 4. Configure WeKnora Sync

The first RAG sync target in the app is WeKnora.

In the new `WeKnora Sync Configuration` section, configure:

- the WeKnora base URL
- the auth mode: `API Key` or `Bearer Token`
- the access token
- a knowledge base ID or knowledge base name
- optional `KnowledgeQA`, `Embedding`, and `VLLM` model IDs from `/api/v1/models`
- whether the app may auto-create the knowledge base if the name does not exist

The sync UI lets you:

- load available knowledge bases from the server
- test the connection before starting sync
- apply the configured chat, embedding, and multimodal models when a knowledge base is created or a sync starts
- select completed conversion outputs to sync
- monitor sync history, logs, progress, ETA, and pause/resume state

## GitHub Automation

- `.github/workflows/ci.yml` builds the solution on pushes and pull requests to `main`, then uploads a packaged Windows artifact.
- `.github/workflows/release.yml` now publishes a packaged GitHub release automatically on every push to `main` by calculating the next semantic patch version from the latest release tag.
- The same release workflow still supports `workflow_dispatch` if you want to override the version manually.
- Release assets are named with the conventional `vX.Y.Z` tag format and include a SHA-256 checksum file.
- `.github/release.yml` defines the automatic release note structure so generated notes read like a conventional release summary instead of a raw commit list.

### Local Validate-And-Sync Script

- Use `scripts/validate-and-sync.ps1 -CommitMessage "your message"` to run a release build, create a local commit, and sync the result to GitHub.
- The script now prefers the `gh api` path by default because this environment can reach GitHub's API even when direct `git push` / `git fetch` calls to `github.com:443` fail.
- Use `-TryGitPush` only if you explicitly want the script to attempt a normal `git push` before falling back to the GitHub API path.
- Use `-SkipBuild -SkipCommit` only on a clean working tree when you want to re-run the sync step without rebuilding or committing again.

## Wiki Sources

- Multilingual wiki source pages are stored under `docs/wiki/` and can be published to the GitHub wiki.
- The current wiki set includes multilingual home pages and release process pages in English, Chinese, Japanese, Spanish, and Arabic.

## Package Dependencies

- `Microsoft.Data.Sqlite`
- `HtmlAgilityPack`
- `ReverseMarkdown`

## Running The App

1. Install the .NET 8 Desktop Runtime if you are using the packaged release, or the .NET 8 SDK if you are running from source.
2. Ensure `zimdump` is installed.
3. Launch the WinForms application.
4. On first startup, choose:
   - the `kiwix-desktop` directory
   - the default output directory
  - optionally, the `zimdump` executable path if it is not on `PATH`
5. If the startup check reports that `zimdump` is missing, browse to `zimdump.exe` or fix `PATH` before converting archives.
6. Click `Scan ZIM Files` to sync downloaded archives.
7. Select a ZIM file from `Downloaded ZIM Files` and start conversion.
8. To send exported articles to WeKnora, open `WeKnora Sync`, select one or more completed conversion outputs, and start a sync task.

## Output Layout

```text
<output-root>/<archive-key>/
  archive-metadata.json
  task-state.json
  <article-path>/
    content.md
    chunks.jsonl
    metadata.json
    images/
```

Each article path is derived from the article URL and normalized into a stable folder name so internal article links resolve to local Markdown targets.

## Persistence And Recovery

- SQLite stores application settings, scanned ZIM files, conversion tasks, article checkpoints, archive metadata, and logs.
- Running tasks write periodic `task-state.json` snapshots alongside database heartbeats.
- Closing the application pauses active tasks and preserves resumable state.
- Recovery operates at article granularity: an interrupted article is retried, but already completed articles remain completed.

## Error Recovery Strategy

For each article:

1. Fetch HTML with `zimdump`.
2. Attempt main-content extraction using ranked DOM selectors and cleanup rules.
3. If that fails, retry with a raw-body fallback strategy.
4. If both fail, skip the article, log the failure, and continue.

Image export failures are also logged and skipped without stopping the task.

## Verification Notes

This workspace was created in an environment that has .NET runtimes installed but no .NET SDK. Because of that, the solution structure and source code were statically validated with editor diagnostics, but `dotnet restore` and `dotnet build` could not be executed here.