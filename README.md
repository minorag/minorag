# Minorag
Minorag is a lightweight, offline-friendly RAG CLI for developers. It indexes your codebase across multiple repositories, computes embeddings locally (Ollama or any provider), and enables natural-language search and Q&amp;A over your source code. No cloud, no telemetry, fully local, fully yours.

## Pack dotnet tool
```bash
dotnet pack -c Release
```

## Install dotnet tool
```bash
cd src
dotnet tool install --global Minorag.Cli --add-source ./Minorag.Cli/bin/Release
```

## Remove
```bash
dotnet tool uninstall --global minorag.cli   
```

## Index repository

```bash
minorag index
```

## Ask 

```bash
minorag ask "What can minorag do?"
```