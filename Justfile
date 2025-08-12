set shell := ["zsh", "-c"]
unexport VIRTUAL_ENV

format:
    uv run ruff check --select I --fix src/agentss
    uv run ruff format src/agentss


type-check:
    # uv run mypy src/agents
    uv run basedpyright src/agentss
    uv run ruff check src/agentss

clean-pycache:
    find . -type d -name "__pycache__" -exec rm -rf {} + 2>/dev/null || true
