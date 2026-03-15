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

## Features
- Browse for llama-server.exe and model folders (.gguf scanning)
- Start/stop server with configurable port, context size, GPU layers, threads
- Chat interface with streaming responses
- Parameter controls: temperature, top_p, top_k, repeat penalty, max tokens
- System prompt, parameter presets (Default/Creative/Precise/Code/Roleplay)
- Server log tab

## Version
- v0.1.0 - Initial release

## Gotchas
- Server readiness detection looks for "listening" or "http" in stdout
- Streaming rebuilds entire chat HTML on each token (could optimize for very long chats)
- `CREATE_NO_WINDOW` flag used to hide server console on Windows
