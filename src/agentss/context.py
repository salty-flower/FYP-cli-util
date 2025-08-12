from __future__ import annotations

from contextvars import ContextVar
from dataclasses import dataclass


@dataclass
class AgentContext:
    doi: str | None = None
    db_path: str | None = None
    tower_base_url: str | None = None


_ctx: ContextVar[AgentContext] = ContextVar("agent_context", default=AgentContext())


def set_context(
    doi: str | None, db_path: str | None, tower_base_url: str | None
) -> None:
    current = _ctx.get()
    current.doi = doi
    current.db_path = db_path
    current.tower_base_url = tower_base_url
    _ctx.set(current)


def clear_context() -> None:
    current = _ctx.get()
    current.doi = None
    current.db_path = None
    current.tower_base_url = None
    _ctx.set(current)


def get_context() -> AgentContext:
    return _ctx.get()
