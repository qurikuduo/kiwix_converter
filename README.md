# Kiwix Converter

Kiwix Converter is a WinForms + SQLite desktop application for exporting kiwix-desktop downloaded ZIM archives into article-level Markdown and RAG-ready JSON artifacts.

## What It Does

- Scans a configured kiwix-desktop download directory and syncs available ZIM files into the application database.
- Uses `zimdump` as the archive access layer for metadata, article listing, HTML extraction, and resource export.
- Extracts only the main article body, rewrites internal links to local Markdown paths, exports images into article-specific folders, and preserves UTF-8 text for CJK and Arabic content.
- Writes article-level checkpoint state into SQLite so paused or interrupted tasks resume from the last article boundary instead of restarting the full archive.
- Produces Markdown, per-article metadata JSON, chunked JSONL for RAG ingestion, and root-level archive/task snapshot files.

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

## Build Requirements

- Windows
- .NET 8 SDK
- `zimdump` available either on `PATH` or via the configured executable path in the UI

## GitHub Automation

- `.github/workflows/ci.yml` builds the solution on pushes and pull requests to `main`, then uploads a packaged Windows artifact.
- `.github/workflows/release.yml` publishes a packaged release whenever a semantic version tag such as `v0.1.0` is pushed.
- Release assets are named with the conventional `vX.Y.Z` tag format and include a SHA-256 checksum file.

## Package Dependencies

- `Microsoft.Data.Sqlite`
- `HtmlAgilityPack`
- `ReverseMarkdown`

## Running The App

1. Install the .NET 8 SDK on the target machine.
2. Restore dependencies and build the solution.
3. Launch the WinForms application.
4. On first startup, choose:
   - the `kiwix-desktop` directory
   - the default output directory
   - optionally, the `zimdump` executable path if it is not on `PATH`
5. Click `Scan ZIM Files` to sync downloaded archives.
6. Select a ZIM file from `Downloaded ZIM Files` and start conversion.

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