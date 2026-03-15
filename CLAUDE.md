# LlamaLink

## Overview
GUI frontend for llama.cpp (llama-server). Single-file Python/PyQt6 app.

## Tech Stack
- Python 3, PyQt6, requests
- Auto-bootstraps deps via `_bootstrap()`
- Communicates with llama-server's OpenAI-compatible API (`/v1/chat/completions`)
- Catppuccin Mocha dark theme

## Run
```
python llamalink.py
```

## Key Architecture
- `ServerManager(QThread)` - Spawns and manages llama-server subprocess, captures stdout
- `ChatWorker(QThread)` - Streams SSE responses from the API
- `LlamaLinkWindow(QMainWindow)` - Main UI with splitter layout (config left, chat right)
- Settings persisted via `QSettings` (Windows registry)
- GPU auto-detection via `nvidia-smi`
- Streaming batched at 30fps via QTimer (not per-token rebuild)

## Features
- Two modes: launch llama-server locally OR connect to an existing server
- Browse for llama-server.exe (auto-detected from PATH / common locations)
- Model folder scanning for .gguf files with size display
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
- Named from timestamp + first user message slug

## Version
- v0.2.0 - Major improvements: streaming perf, external server, markdown, chat history, auto-detect, speed display

## Gotchas
- Server readiness detection looks for "listening" in stdout
- `CREATE_NO_WINDOW` flag used to hide server console on Windows
- External server mode uses /health endpoint for connection check + periodic polling
- Markdown rendering is regex-based (code blocks, inline code, bold, italic) - not a full parser
