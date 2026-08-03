# Changelog

All notable changes to LlamaLink will be documented in this file.

## Unreleased

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

## [v0.4.0] - %Y->- (HEAD -> master, origin/master)

- v0.4.0 - C# WPF/.NET 9 rewrite with premium UI
- Added: Add README, icon, PyInstaller build, CI/CD workflow
- v0.3.0 - HuggingFace model browser and download
- v0.2.0 - Streaming perf, external server, markdown, chat history
- Initial release - LlamaLink v0.1.0
