from __future__ import annotations

from dataclasses import dataclass


@dataclass
class AgentContext:
    doi: str | None = None
    db_path: str | None = None
    tower_base_url: str | None = None
