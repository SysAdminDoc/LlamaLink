# LlamaLink

## Overview
GUI frontend for llama.cpp (llama-server). C# WPF/.NET 9, Catppuccin Mocha dark theme.

## Tech Stack
- C# WPF, .NET 9, single-file self-contained exe (win-x64)
- HttpClient for SSE streaming chat + HuggingFace API
- System.Text.Json for serialization
- Settings stored as JSON in `~/.llamalink/settings.json`
- Chat history stored in `~/.llamalink/chats/`

## Build
```bash
cd src
dotnet build                           # debug build
dotnet publish -c Release -o ../dist   # release single-file exe
```

## Project Structure
```
src/
  LlamaLink.csproj    - .NET 9 WPF project
  App.xaml             - Catppuccin Mocha theme (colors, brushes, control styles)
  App.xaml.cs          - Application entry point
  MainWindow.xaml      - UI layout (3-column: config | splitter | tabs)
  MainWindow.xaml.cs   - All application logic (~700 lines)
assets/
  llamalink.ico        - App icon (multi-size)
.github/workflows/
  build.yml            - Windows-only CI/CD (dotnet publish)
```

## Key Architecture
- Server process managed directly via `System.Diagnostics.Process`
- Server stdout/stderr read on background thread, output relayed via `Dispatcher.Invoke`
- Chat streaming via `HttpClient.SendAsync` with `ResponseHeadersRead` + SSE line parsing
- 30fps `DispatcherTimer` batches stream display updates (not per-token)
- HuggingFace search/files/download all async with CancellationToken support
- Downloads support resume via Range headers + .part files
- ObservableCollection<ChatMessageVM> for chat display via ItemsControl + DataTemplate

## Features
- Two modes: launch llama-server locally OR connect to external server
- llama-server.exe auto-detected from PATH / common locations
- Model folder scanning for .gguf files with size display
- HuggingFace model browser: search, sort, browse GGUF files, download with progress
- Chat with SSE streaming, tokens/sec display
- Parameter controls: temperature, top_p, top_k, repeat penalty, max tokens
- Server params: context size, GPU layers, threads, flash attention, mlock
- Presets: Default/Creative/Precise/Code/Roleplay
- Chat history: auto-save, load, export (MD/JSON/TXT), delete
- Server log tab

## Version
- v0.4.0 - C# WPF/.NET 9 rewrite (was Python/PyQt6)
- v0.3.0 - HuggingFace model browser + download with progress/resume
- v0.2.0 - Streaming perf, external server, markdown, chat history
- v0.1.0 - Initial release

## Gotchas
- Server readiness detection looks for "listening" in stdout/stderr
- WPF is Windows-only (no macOS/Linux builds)
- HF API file sizes come from siblings array; some repos may not report sizes
- Quant type parsed from filename via regex (Q4_K_M, IQ3_S, etc.)
- External server mode uses /health endpoint for connection check + periodic polling
- Chat display uses plain TextBlock (no markdown rendering) - simpler than Python version's HTML
