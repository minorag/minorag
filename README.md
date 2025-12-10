# Minorag — Local Codebase RAG for Developers [![GitHub stars](https://img.shields.io/github/stars/minorag/minorag?style=social)](https://github.com/minorag/minorag/stargazers)

Minorag is a lightweight, offline-friendly RAG CLI that indexes your codebase, computes embeddings locally (via Ollama), and lets you ask natural-language questions about your source code.

- Fully local (no cloud calls)
- Fast SQLite embedding store
- Multi-repository indexing
- Natural language Q&A using your local LLM
- No telemetry, no tracking

Think *ripgrep + ChatGPT*, but fully offline.

---

## Demo

![Minorag demo](./minorag.gif)

---

## Quickstart

```bash
# 1. Install Ollama
curl -fsSL https://ollama.com/install.sh | sh
ollama serve

# 2. Pull recommended models
ollama pull mxbai-embed-large
ollama pull gpt-oss:20b

# 3. Install Minorag (from repo root)
dotnet tool install --global Minorag.Cli --add-source ./src/Minorag.Cli/bin/Release

# 4. Index & ask
cd ~/dev/my-project
minorag index
minorag ask "Where is authentication handled?"
```

---

# Features

- Local embeddings via Ollama (mxbai-embed-large, nomic-embed-text, etc.)
- Local chat answering using any Ollama chat model
- Chunk-based indexing for multi-language repos
- Safe re-indexing support (keep your index fresh as code changes)
- Multi-repository indexing into a single SQLite DB
- Cosine similarity search
- Pretty CLI output (Spectre.Console)
- Zero network requirements beyond Ollama
- Simple .NET global tool installation

---

# Prerequisites

## 1. Install Ollama

Minorag requires **Ollama** for embeddings and LLM chat.

👉 **Download / learn more:** https://ollama.com


Install Ollama with a shell script:

```bash
curl -fsSL https://ollama.com/install.sh | sh
```

Start Ollama:

```bash
ollama serve
```

## 2. Pull recommended models

```bash
ollama pull mxbai-embed-large
ollama pull gpt-oss:20b
```

---

# Installation

## Pack the .NET tool

```bash
dotnet pack -c Release
```

## Install globally

From inside `src/`:

```bash
dotnet tool install --global Minorag.Cli --add-source ./Minorag.Cli/bin/Release
```

## Uninstall

```bash
dotnet tool uninstall --global minorag.cli
```

---

# Configuration

Minorag automatically loads:

- `appsettings.json`
- `appsettings.local.json`
- Environment variables with prefix `MINORAG_`

Example `appsettings.json`:

```json
{
  "Ollama": {
    "Host": "http://127.0.0.1:11434",
    "EmbeddingModel": "mxbai-embed-large",
    "ChatModel": "gpt-oss:20b",
    "Temperature": 0.1
  },
  "Database": {
    "Path": "~/.minorag/index.db"
  }
}
```

---

# Usage

## Commands overview

| Command              | Description                                           |
|----------------------|-------------------------------------------------------|
| `minorag index`      | Index a repo/folder into the local SQLite DB          |
| `minorag ask`        | Ask natural language questions over indexed repos     |
| `minorag prompt`     | Generate a ChatGPT-ready prompt (no LLM call)         |
| `minorag db-path`    | Show the path to the SQLite database                  |
| `minorag config`     | Show and manage configuration                         |
| `minorag repos`      | List repositories stored in the index                 |
| `minorag version`    | Show CLI version                                      |
| `minorag doctor `    | Run health checks                                     |

## Index the current repo

```bash
minorag index
```

Index a specific folder:

```bash
minorag index --repo ~/dev/project
```

## Ask questions

```bash
minorag ask "Where is the authentication handled?"
```

## Print the database path

```bash
minorag db-path
```

## Generate a ChatGPT-ready prompt (NO local LLM call)

This is ideal for pasting into ChatGPT, Claude, or any cloud LLM.

```bash
minorag prompt "How does the authentication middleware work?"
```

Copy to clipboard

```bash
minorag prompt "How does the authentication middleware work?" | pbcopy # Mac
minorag prompt "How does the authentication middleware work?" | xclip -selection clipboard # Linux xclip
minorag prompt "How does the authentication middleware work?" | xsel --clipboard --input # Linux xsel
minorag prompt "How does the authentication middleware work?" | wl-copy # Linux wl-copy
minorag prompt "How does the authentication middleware work?" | clip # Windows
```

---

# Why Minorag?

- Incremental adoption: index one repo or your whole dev folder
- Built for local-first workflows with Ollama

| Feature | Minorag | VS Code Extensions | Cloud ChatGPT | Cody |
|--------|---------|--------------------|----------------|------|
| Fully local | ✅ | ❌ | ❌ | ❌ |
| Offline | ✅ | ❌ | ❌ | ❌ |
| Multi-repo RAG | ✅ | ❌ | ❌ | ✅ |
| Customizable models | ✅ | ❌ | ❌ | ❌ |
| No telemetry | ✅ | ❌ | ❌ | ❌ |

Minorag is ideal for:

- Developers who **cannot upload code to the cloud**
- Private/internal repos
- Offline environments
- DevOps workflows (CI/CD assistants)
- Laptops with local LLM setup (Ollama, LM Studio, etc.)

---

# Roadmap

- [ ] Additional embedding providers
- [ ] Additional LLM chat clients (OpenAI, LM Studio, LocalAI)
- [ ] Web UI for browsing + RAG chat
- [ ] Language-aware symbol extraction
- [ ] Multi-threaded indexing
- [ ] Automatic repo discovery

---

# Contributing

1. Fork repo  
2. Create feature branch  
3. Submit PR  
4. Get a virtual high-five 🎉

---

## Triggering new release pipeline

```bash
./tag-release.sh
```

---

## License

This project is licensed under the **Apache License 2.0**.

You may:

- Use the code commercially  
- Modify and distribute it  
- Build derivative works  
- File issues and contribute  

Under the condition that you retain:

- The LICENSE file  
- The NOTICE file  
- Copyright attribution  

See the full license text in the [LICENSE](LICENSE) file.