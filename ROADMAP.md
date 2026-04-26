# LlamaLink Roadmap

Python/PyQt6 GUI frontend for llama.cpp — search/download GGUF models from HuggingFace, launch or connect to llama-server, chat with streaming. v0.4.0. Roadmap targets local-LLM power-user features: tool calling, RAG, multimodal, and first-class Ollama/LM-Studio interop.

## Planned Features

### Model & Server
- **Auto-quant recommender** — given VRAM + RAM, recommend the highest-quality quant that fits (Q8 > Q6_K > Q5_K_M > Q4_K_M > IQ3_S)
- **Multi-server profile** — saved server configurations (model, ctx, ngl, threads), one-click switch
- **Server-swap chat continuation** — detach conversation from one server, re-attach to another without losing history
- **llama-server build updater** — detect installed version, offer download of latest release matching CPU features (AVX2/AVX512/CUDA/Vulkan/ROCm)
- **Kobold.cpp / text-generation-webui / Ollama backend support** — unify under one OpenAI-compatible interface, auto-translate endpoint paths

### Chat Features
- **Tool calling UI** — visualize function-call JSON, allow the user to confirm before executing; ship a library of safe tools (file read, calculator, Python eval in sandbox)
- **System prompt library** — shareable JSON of curated system prompts per domain (code / roleplay / summarize / translate)
- **Conversation branching** — fork a chat at any message, keep both branches side-by-side
- **Regenerate with different params** — re-roll last response with adjusted temp/top-p without editing history
- **Multi-turn few-shot editor** — edit past assistant turns to steer behavior (RLHF-style teaching)

### RAG & Context
- **Local RAG** — drop PDFs/MD/txt, chunk + embed via `fastembed`, store in lancedb, inject relevant chunks into prompts
- **Document viewer pane** — show which chunks were retrieved, with source highlight
- **Chat-over-folder** — point at a folder, keep it synced to the index, ask questions
- **Web-search tool** — optional DuckDuckGo/SearxNG-proxied web lookup for fresh info

### Multimodal
- **Vision support** — llama.cpp multimodal (llava, bakllava, moondream); drag-image-into-chat to ask about it
- **Whisper integration** — voice input (press-to-talk) + TTS output via Piper or XTTS
- **Image generation tool** — optional SD.cpp / stable-diffusion.cpp backend for `/image <prompt>` commands

### Dev UX
- **JSON mode / grammar enforcement** — surface llama.cpp's GBNF grammar, offer templates (JSON / regex / code-only)
- **Token probabilities viewer** — show top-K alternatives per token for prompt debugging
- **Prompt formatting inspector** — visualize final prompt sent (tokenized, with chat template applied)
- **Model card viewer** — pull model README from HF, render inline

### Distribution
- **PyInstaller one-file with `freeze_support()`** — already following global rules, ensure runtime_hook is wired (see CLAUDE.md)
- **Winget + Chocolatey packages**
- **ARM64 Windows build**

## Competitive Research
- **LM Studio** — closed-source but best-in-class model browser + chat UI; borrow the UX (model cards, download queue, server start/stop panel).
- **Ollama** — CLI-first with REST API; complementary. LlamaLink should offer "connect to Ollama" as a first-class backend.
- **Jan.ai** — OSS all-in-one with model hub + threads; similar ambition. Track for feature parity.
- **Open WebUI (formerly Ollama WebUI)** — web-based; LlamaLink is the desktop story.
- **GPT4All** — simpler, opinionated; good reference for newbie-friendly onboarding.

## Nice-to-Haves
- Built-in speculative decoding UI (draft model + main model side-by-side)
- Grammar builder visual editor for GBNF
- Cost estimator (tokens + power draw) for long runs
- Model file pruning tool — drop unused models, reclaim disk with confirmation
- Fine-tune kickoff button (LoRA via llama.cpp's `finetune` example)
- Marketplace of community system prompts rated by users

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
