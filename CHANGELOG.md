# Changelog

All notable changes to LlamaLink will be documented in this file.

## Unreleased

- Added an auto-quant recommender that compares local GGUF variants against available VRAM and RAM.
- Added focused recommender tests and guarded WPF startup initialization.
- Added saved server profiles for one-click model and inference-parameter switching.
- Added detachable chat context metadata so loaded conversations can continue on another profile or endpoint.

## [v0.4.0] - %Y->- (HEAD -> master, origin/master)

- v0.4.0 - C# WPF/.NET 9 rewrite with premium UI
- Added: Add README, icon, PyInstaller build, CI/CD workflow
- v0.3.0 - HuggingFace model browser and download
- v0.2.0 - Streaming perf, external server, markdown, chat history
- Initial release - LlamaLink v0.1.0
