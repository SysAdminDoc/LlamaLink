# LlamaLink

## Overview
GUI frontend for llama.cpp (llama-server). Single-file Python/PyQt6 app.

## Tech Stack
- Python 3, PyQt6, requests
- Auto-bootstraps deps via `_bootstrap()`
- Communicates with llama-server's OpenAI-compatible API (`/v1/chat/completions`)
- HuggingFace API for model search/download (`https://huggingface.co/api/models`)
- Catppuccin Mocha dark theme

## Run
```bash
python llamalink.py
```

## Key Architecture
- `ServerManager(QThread)` - Spawns and manages llama-server subprocess, captures stdout
- `ChatWorker(QThread)` - Streams SSE responses from the API
- `HFSearchWorker(QThread)` - Searches HuggingFace for GGUF models
- `HFFilesWorker(QThread)` - Fetches file list for a specific HF repo
- `HFDownloadWorker(QThread)` - Downloads GGUF files with progress, resume support
- `LlamaLinkWindow(QMainWindow)` - Main UI with splitter layout (config left, tabs right)
- Settings persisted via `QSettings` (Windows registry)
- GPU auto-detection via `nvidia-smi`
- Streaming batched at 30fps via QTimer (not per-token rebuild)

## Features
- Two modes: launch llama-server locally OR connect to an existing server
- Browse for llama-server.exe (auto-detected from PATH / common locations)
- Model folder scanning for .gguf files with size display
- **HuggingFace model browser**: search, sort (downloads/likes/date/trending), browse GGUF files, download with progress
- Downloads support resume (partial .part files preserved)
- Chat interface with streaming responses and markdown rendering
- Code blocks, inline code, bold, italic rendered in chat
- Parameter controls: temperature, top_p, top_k, repeat penalty, max tokens
- Server params: context size, GPU layers, threads, flash attention, mlock
- System prompt, parameter presets (Default/Creative/Precise/Code/Roleplay)
- Server log tab with 5000 line cap
- Chat history: auto-save, load, export (MD/JSON/TXT), delete
- Tokens/sec speed display during and after generation
- Window geometry saved/restored

## Chat History
- Stored in `~/.llamalink/chats/` as JSON files
- Auto-saved after each assistant response and on window close

## HuggingFace Integration
- Uses public API (no token required for public models)
- Supports `HF_TOKEN` env var for private/gated models
- Searches filter to `gguf` tag automatically
- Sort: downloads, likes, lastModified, trending_score
- Downloads go to configured model folder, auto-refresh model list on completion
- Quant type parsed from filename (Q4_K_M, IQ3_S, etc.)

## Version
- v0.3.0 - HuggingFace model browser + download with progress/resume
- v0.2.0 - Streaming perf, external server, markdown, chat history
- v0.1.0 - Initial release

## Gotchas
- Server readiness detection looks for "listening" in stdout
- `CREATE_NO_WINDOW` flag used to hide server console on Windows
- External server mode uses /health endpoint for connection check + periodic polling
- Markdown rendering is regex-based - not a full parser
- HF API file sizes come from siblings array; some repos may not report sizes
