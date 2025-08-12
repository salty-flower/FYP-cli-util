"""Statistics and health-related models for SearchMancer Tower."""

from datetime import UTC, datetime
from enum import StrEnum
from typing import Annotated, Literal
from uuid import UUID

from annotated_types import Ge
from pydantic import BaseModel, Field

from .core import SearchEngine, SpellJobStatus
from .jobs import SpellJob, SpellResult


class SSEConnectionType(StrEnum):
    """Type of SSE connection."""

    UI = "ui"
    FAMILIAR = "familiar"


QueueHealthStats = dict[SpellJobStatus, int]


class EnginePaginationInfo(BaseModel):
    """Engine-specific pagination configuration."""

    engine: SearchEngine
    url_page_size: int = 10
    url_parameter: str
    supports_direct_page_access: bool
    max_pages: int = 100
    rate_limit_ms: int = 2000


class QueueStats(BaseModel):
    """Queue statistics."""

    total: Annotated[int, Ge(0)]
    pending: Annotated[int, Ge(0)]
    assigned: Annotated[int, Ge(0)]
    processing: Annotated[int, Ge(0)]
    completed: Annotated[int, Ge(0)]
    failed: Annotated[int, Ge(0)]


EngineStats = dict[SearchEngine, Annotated[int, Ge(0)]]


class StatsResponse(BaseModel):
    """Response from /stats endpoint."""

    overall: QueueStats
    by_engine: EngineStats
    timestamp: datetime


class SSEEvent(BaseModel):
    """Base model for SSE events."""

    timestamp: datetime = Field(default_factory=lambda: datetime.now(UTC))

    def to_sse_format(self) -> str:
        """Convert the event to a string suitable for SSE."""
        return f"data: {self.model_dump_json()}\n\n"


class SSEConnectedEvent(SSEEvent):
    """SSE connected event."""

    type: Literal["connected"] = "connected"
    connection_id: int
    update_interval: int


class SSEStatsUpdateEvent(SSEEvent):
    """SSE stats update event."""

    type: Literal["stats_update"] = "stats_update"
    stats: StatsResponse


class SSEHeartbeatEvent(SSEEvent):
    """SSE heartbeat event."""

    type: Literal["heartbeat"] = "heartbeat"


class SSEErrorEvent(SSEEvent):
    """SSE error event."""

    type: Literal["error"] = "error"
    message: str


class SSEJobCreatedEvent(SSEEvent):
    """SSE event for job creation."""

    type: Literal["job_created"] = "job_created"
    job: SpellJob


class SSEJobAssignedEvent(SSEEvent):
    """SSE event for job assignment."""

    type: Literal["job_assigned"] = "job_assigned"
    job: SpellJob
    familiar_id: str


class SSEJobStatusChangeEvent(SSEEvent):
    """SSE event for job status change."""

    type: Literal["job_status_change"] = "job_status_change"
    job_id: UUID
    status: SpellJobStatus
    familiar_id: str | None = None
    result: SpellResult | None = None


SSEEventUnion = Annotated[
    SSEConnectedEvent
    | SSEStatsUpdateEvent
    | SSEHeartbeatEvent
    | SSEErrorEvent
    | SSEJobCreatedEvent
    | SSEJobAssignedEvent
    | SSEJobStatusChangeEvent,
    Field(discriminator="type"),
]


class HealthResponse(BaseModel):
    """Response from /health endpoint."""

    status: str
    queue: QueueHealthStats


class EnginePaginationInfoResponse(BaseModel):
    """Response from /engines/pagination-info endpoint."""

    engines: dict[str, EnginePaginationInfo]


class ServiceStatsResponse(BaseModel):
    """Response from /stats endpoint with basic service information."""

    service: str
    version: str
    status: str
    endpoints: dict[str, str]
