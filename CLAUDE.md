# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build System and Commands

This is a hybrid .NET/Python research project for academic paper data collection and analysis.

### .NET C# Application

The main application is built with .NET 10.0 (preview) using ConsoleAppFramework:

```bash
# Build the solution
dotnet build

# Run the CLI application
dotnet run --project src/DataCollection.Presentation.Cli

# Restore packages
dotnet restore

# Publish for release
dotnet publish -c Release
```

### Python Environment

The project includes Python components for data processing:

```bash
# Initialize Python environment (using micromamba)
micromamba create -p ./.venv python=3.12
uv sync --python .venv

# Activate environment
# On Windows: .venv\Scripts\activate
# On Unix: source .venv/bin/activate
```

## Architecture Overview

This project follows Clean Architecture patterns with the following layers:

### Core Projects

- **DataCollection.Core**: Domain models and entities (Papers, Issues, PDF data)
- **DataCollection.Common**: Shared utilities, extensions, and logging helpers

### Application Layer

- **DataCollection.Application**: Business logic, services, and features
  - **Features/BugDiscovery**: GitHub repository analysis and bug list discovery
  - **Features/IssueAnalysis**: Issue processing with LLM-based criteria evaluation
  - **Features/PaperAnalysis**: PDF content analysis and extraction
  - **Features/SemanticAgents**: AI agents using Semantic Kernel for discovery tasks
  - **Features/PatternMatching**: Text pattern matching and configuration

### Infrastructure Layer

- **DataCollection.Infrastructure**: External concerns (database, HTTP clients, APIs)
  - **Clients/ACM**: ACM Digital Library scraping strategies
  - **Clients/IssueTrackers**: GitHub API integration with caching
  - **Persistence**: Entity Framework database context and migrations

### Presentation Layer

- **DataCollection.Presentation.Cli**: Console application with commands for:
  - Bug list discovery from GitHub repositories
  - PDF scraping and analysis from research conferences
  - Issue processing and batch analysis
  - Interactive REPL for data exploration
  - Export functionality for analysis results

## Key Technologies

- **.NET 10.0 (preview)** with C# preview language features
- **ConsoleAppFramework** for CLI command structure
- **Entity Framework Core** with SQLite database
- **Semantic Kernel** for LLM integration and AI agents
- **Python.NET** for Python integration (NLTK, PDF processing)
- **Spectre.Console** for rich console output
- **Serilog** for structured logging

## Configuration

The application uses:

- `appsettings.json` for base configuration
- `appsettings.local.json` for local overrides (not tracked in git)
- Environment variables for additional configuration

Key configuration sections include credentials, LLM options, parallelism settings, and file paths.

## Data Storage

- **SQLite database** (`data-collection.db`) stores analysis results, papers, and issue data
- **JSON exports** in `data/` directory for conference-specific analyses
- **PDF cache** for downloaded research papers

## Development Patterns

- **Primary constructors** preferred for simple classes
- **Expression-bodied members** for simple methods/properties
- **Pattern matching** encouraged over traditional conditionals
- **Dependency injection** throughout the application layers
- **Async/await** patterns for I/O operations
- **Structured logging** with contextual information

## Ablation Study Mode

The `issue process-batch` command accepts a `--use-naive-prompt` flag to toggle a raw HTML baseline for the subjective status
analysis. When enabled, the CLI routes requests through `IssueSubjectiveStatusNaiveBaseline`, which fetches the GitHub issue HTML
directly and applies a minimal system prompt. The default structured prompt remains active when the flag is omitted unless
`LLMOptions.UseNaivePromptForIssueAnalysis` is set to `true` in configuration.

Examples:

```bash
# Structured prompt (default)
dotnet run --project src/DataCollection.Presentation.Cli -- issue process-batch \
  --input-file issues.txt --output-path structured.jsonl

# Naive baseline prompt
dotnet run --project src/DataCollection.Presentation.Cli -- issue process-batch \
  --input-file issues.txt --output-path naive.jsonl --use-naive-prompt true
```
