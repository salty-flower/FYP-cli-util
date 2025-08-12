"""Familiar management models for SearchMancer Tower."""

from datetime import datetime
from uuid import UUID

from pydantic import BaseModel


class FamiliarListItem(BaseModel):
    """Individual familiar data in list response."""

    familiar_id: str
    status: str
    engine: str
    last_heartbeat: datetime
    current_job_id: UUID | None
    connection_id: int
    assigned_jobs_count: int


class FamiliarListResponse(BaseModel):
    """Response for listing all familiars."""

    familiars: list[FamiliarListItem]
    stats: dict[str, int]
    total_count: int
