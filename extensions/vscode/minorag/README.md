# Minorag – Local RAG for Codebases

Minorag is a **local-first retrieval-augmented generation (RAG)** tool for large codebases.

This VS Code extension provides an interactive **chat panel** and **doctor diagnostics** backed by a locally running Minorag API.

---

## Features

- 💬 **Chat with your codebase**
  - Ask natural-language questions about repositories
  - Streams responses incrementally
- 🩺 **Doctor command**
  - Inspect index health, repositories, and configuration
- 🔒 **Local-first**
  - No code leaves your machine
  - Works with self-hosted models

---

## Requirements

- Minorag API running locally  
  Default: `http://localhost:9999`

---

## Extension Settings

This extension contributes the following settings:

- `minorag.apiBaseUrl`  
  Base URL of the running Minorag API.

---

## Commands

- **Minorag: Chat** — Open the chat panel
- **Minorag: Ask** — Ask a one-off question
- **Minorag: Doctor** — Inspect index & configuration

---

## Known Issues

- Requires a running Minorag API instance
- Large responses may stream for several seconds

---

## Release Notes

### 0.0.1
- Initial release
- Chat panel
- Doctor diagnostics