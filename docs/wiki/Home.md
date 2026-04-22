# Kiwix Converter Wiki

Welcome to the Kiwix Converter wiki.

## Overview

Kiwix Converter turns kiwix-desktop ZIM archives into article-level Markdown, metadata JSON, and chunked JSONL files for RAG ingestion.

## Main Areas

- Desktop UI: settings, scans, task control, history, and logs
- Conversion engine: `zimdump` integration, main-content extraction, Markdown conversion, image export, and link rewriting
- Persistence: SQLite-backed settings, task checkpoints, archive metadata, and logs
- Release automation: CI build packaging and semantic-version release publishing

## Getting Started

For non-developer users, the easiest route is:

1. Download the latest Windows release zip.
2. Install the .NET 8 Desktop Runtime.
3. Install `zimdump` and either add it to `PATH` or browse to `zimdump.exe` inside the app.
4. Start the app, pick your `kiwix-desktop` directory and output directory, then scan ZIM archives.

If you build from source instead of using a release package, install the .NET 8 SDK.

## Startup Dependency Check

The desktop app now checks for `zimdump` during startup.

- When `zimdump` is available, conversion is ready.
- When `zimdump` is missing, the app warns the user and offers an immediate way to browse for the executable.
- The app can stay open without `zimdump`, but export features remain blocked until the dependency is configured.

## WeKnora Sync

The first built-in RAG sync target is WeKnora.

The current desktop flow supports:

- base URL plus `API Key` or `Bearer Token` authentication
- loading knowledge bases from the WeKnora server
- auto-creating a knowledge base by name when enabled
- configuring `KnowledgeQA`, `Embedding`, and `VLLM` model IDs from `/api/v1/models`
- reapplying the configured chat, embedding, and multimodal models whenever a knowledge base is created or a sync starts
- selecting completed conversion outputs for sync
- sync history, per-task logs, progress, ETA, pause/resume, and resumable checkpoints

## Language Pages

- [Home](Home)
- [首页（简体中文）](Home.zh-CN)
- [ホーム（日本語）](Home.ja)
- [Inicio (Español)](Home.es)
- [الصفحة الرئيسية (العربية)](Home.ar)

## Additional Pages

- [Release Process](Release-Process)
- [发布流程（简体中文）](Release-Process.zh-CN)
- [リリース手順（日本語）](Release-Process.ja)
- [Proceso de release (Español)](Release-Process.es)
- [عملية الإصدار (العربية)](Release-Process.ar)

## Installation Notes

- `zimdump` comes from the Kiwix tooling, not from this repository.
- The packaged Windows release is currently framework-dependent, so installing the .NET 8 Desktop Runtime is recommended before first launch.
- Building locally still requires the .NET 8 SDK.