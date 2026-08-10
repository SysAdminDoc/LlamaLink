# Changelog

All notable changes to LlamaLink will be documented in this file.

## [v0.5.1] - 2026-08-09

- Synchronized application, packaging, and documentation version metadata for the maintenance release.

## [v0.5.0] - 2026-08-03

- Added an auto-quant recommender that compares local GGUF variants against available VRAM and RAM.
- Added focused recommender tests and guarded WPF startup initialization.
- Added saved server profiles for one-click model and inference-parameter switching.
- Added detachable chat context metadata so loaded conversations can continue on another profile or endpoint.
- Added a llama.cpp release updater with hardware-aware Windows x64 asset selection and safe ZIP downloads.
- Added backend adapters for Ollama, KoboldCpp, and text-generation-webui with automatic endpoint and stream translation.
- Added opt-in, confirmation-gated safe tools for confined file reads, arithmetic, and restricted Python expressions.
- Added a shareable system prompt library with curated domains, custom JSON persistence, import, and export.
- Added persistent conversation branching from any message while retaining the parent chat for comparison.
- Added guarded last-response regeneration using the current temperature and top-p settings.
- Added a multi-turn few-shot editor for revising assistant examples in saved conversations.
- Added local PDF/Markdown/text RAG indexing with chunked embeddings, persisted sources, drag-and-drop ingestion, and prompt retrieval.
- Added a selectable RAG excerpt viewer with source, chunk, and relevance highlighting.
- Added persisted folder watching with debounced re-indexing and removal of deleted document sources.
- Added opt-in, confirmation-gated web search through DuckDuckGo or a configured SearxNG proxy.
- Added vision chat attachments with drag/drop image previews and multimodal OpenAI/Ollama payloads.
- Added optional ffmpeg, whisper.cpp, and Piper speech adapters for hold-to-record transcription and WAV synthesis.
- Added an optional `/image <prompt>` stable-diffusion.cpp command adapter with bounded local PNG output.
- Added selectable JSON, regex, code-only, and custom GBNF output constraints with backend-specific payload hints.
- Added an opt-in token-probability viewer with streamed top-K alternatives for compatible OpenAI-style backends.
- Added a prompt inspector with exact request JSON, role transcript, server-template guidance, and approximate token preview.
- Added an inline Hugging Face model-card viewer with front-matter metadata and bounded Markdown rendering.
- Completed the legacy PyInstaller one-file path with `freeze_support()`, a Qt runtime hook, and bundled branding assets.
- Added validated Winget and Chocolatey package metadata for the portable Windows release.
- Added an overridable ARM64 Windows runtime target with a self-contained publish path.
- Added guarded speculative decoding controls for a llama.cpp draft model, draft GPU layers, and draft context.
- Added a validated visual GBNF rule builder that round-trips with the custom grammar editor.
- Added measured and forecast token-energy estimates using configurable power draw and electricity rate.
- Added a confirmation-gated model pruning tool that protects active and saved model references.
- Added a cancellable LoRA fine-tune kickoff for the local llama.cpp finetune executable.

## [v0.4.0] - 2026-03-15

- v0.4.0 - C# WPF/.NET 9 rewrite with premium UI
- Added: Add README, icon, PyInstaller build, CI/CD workflow
- v0.3.0 - HuggingFace model browser and download
- v0.2.0 - Streaming perf, external server, markdown, chat history
- Initial release - LlamaLink v0.1.0

## Roadmap archive — 2026-08-10 — ROADMAP.md

<details>
<summary>Original roadmap snapshot</summary>

```markdown
# LlamaLink Roadmap

Python/PyQt6 GUI frontend for llama.cpp — search/download GGUF models from HuggingFace, launch or connect to llama-server, chat with streaming. v0.5.1. Roadmap targets local-LLM power-user features: tool calling, RAG, multimodal, and first-class Ollama/LM-Studio interop.

## Planned Features

### Model & Server
### Chat Features

### RAG & Context

### Multimodal

### Dev UX

### Distribution

## Competitive Research
- **LM Studio** — closed-source but best-in-class model browser + chat UI; borrow the UX (model cards, download queue, server start/stop panel).
- **Ollama** — CLI-first with REST API; complementary. LlamaLink should offer "connect to Ollama" as a first-class backend.
- **Jan.ai** — OSS all-in-one with model hub + threads; similar ambition. Track for feature parity.
- **Open WebUI (formerly Ollama WebUI)** — web-based; LlamaLink is the desktop story.
- **GPT4All** — simpler, opinionated; good reference for newbie-friendly onboarding.

## Nice-to-Haves

## Open-Source Research (Round 2)

### Related OSS Projects
- ollama/ollama — https://github.com/ollama/ollama — the reference local runtime; target backend
- ggml-org/llama.cpp — https://github.com/ggml-org/llama.cpp — lowest-level inference, ships its own WebUI
- open-webui/open-webui — https://github.com/open-webui/open-webui — 90k+ stars, most-used Ollama frontend; built-in RAG + Chroma
- HelgeSverre/ollama-gui — https://github.com/HelgeSverre/ollama-gui — Docker-packaged Ollama + GUI stack
- ivanfioravanti/chatbot-ollama — https://github.com/ivanfioravanti/chatbot-ollama — fork of chatbot-ui, image upload + streaming controls
- sufianetaouil/ollama-chat-desktop — https://github.com/sufianetaouil/ollama-chat-desktop — Electron desktop app, model management
- olegshulyakov/llama.ui — https://github.com/olegshulyakov/llama.ui — multi-backend (llama.cpp/LM Studio/Ollama/vLLM/OpenAI), browser-local
- fmaclen/hollama — https://github.com/fmaclen/hollama — minimal web UI, installers for mac/win/linux
- JamesDudenhoeffer/ChatbotUI fork (mckaywrigley/chatbot-ui) — https://github.com/mckaywrigley/chatbot-ui — Supabase-backed multi-model
- SillyTavern — https://github.com/SillyTavern/SillyTavern — advanced front-end, RP-oriented; reference for prompt-engineering UX

### Features to Borrow
- **Built-in RAG with local vector store** (Open WebUI with Chroma) — drop in docs, chat with them; no external DB setup
- **Per-model default system prompts / templates** (Chatbot Ollama, SillyTavern) — save per-model presets so switching models doesn't lose prompt
- **Image upload for vision-capable models** (Chatbot Ollama) — llava, moondream, etc.; pass base64 through Ollama API
- **Model manager UI** (sufianetaouil/ollama-chat-desktop) — pull/list/delete models from GUI; one of the top friction points for new users
- **Multi-backend adapter layer** (llama.ui) — support Ollama + llama.cpp + LM Studio + OpenAI-compatible endpoints from one client
- **Conversation history with SQLite** (chatbot-ui, Hollama) — local DB; searchable across past chats
- **Streaming stop button + edit-last-message regen** (Chatbot Ollama) — basic but missing in many clones
- **Code-block copy button + syntax highlight** (Chatbot Ollama) — hard default; also language-badge on block
- **System tray + global hotkey** (sibling PromptCompanion pattern) — invoke from anywhere, paste response to active window
- **Token/speed counters** — tokens/sec, total tokens, ETA; helpful when tuning models

### Patterns & Architectures Worth Studying
- Open WebUI's **pipeline plugins** — every request/response flows through a plugin chain (transformers, filters, guards); enables features like auto-title, toxicity filter, retrieval without touching core
- llama.cpp **built-in WebUI** — single-binary deployment, no separate server; architectural option if LlamaLink ever wants to embed inference
- Ollama **/api/chat** streaming NDJSON — reference for client-side stream parse + render; don't buffer whole response
- chatbot-ui's **database-backed multi-user** (Supabase) — overkill for local-first but instructive for eventual shared-team mode
- **MCP (Model Context Protocol)** — emerging standard; designing LlamaLink as an MCP host lets it consume the growing MCP-server ecosystem for tools/resources
```

</details>
