"""Job-related models for SearchMancer Tower."""

from datetime import UTC, datetime
from enum import StrEnum
from typing import Annotated
from uuid import UUID, uuid4

from pydantic import BaseModel, Field

from .core import SearchResult, Spell, SpellJobStatus


class JobResultStatus(StrEnum):
    """Status response for job result submission."""

    SUCCESS = "success"
    ERROR = "error"


class SpellResult(BaseModel):
    """Result of a spell job."""

    job_id: UUID
    results: list[SearchResult] | None = None
    ai_summary: str | None = None
    error: str | None = None
    completed_at: datetime | None = None


class SpellJob(BaseModel):
    """A spell job with full details."""

    job_id: UUID = Field(default_factory=uuid4)
    spell: Spell
    status: SpellJobStatus = SpellJobStatus.PENDING
    results: list[SearchResult] | None = None
    ai_summary: str | None = None
    error: str | None = None
    created_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    updated_at: datetime = Field(default_factory=lambda: datetime.now(UTC))
    completed_at: datetime | None = None
    familiar_id: str | None = None

    def mark_assigned(self, familiar_id: str) -> None:
        """Mark job as assigned to a familiar."""
        self.status = SpellJobStatus.ASSIGNED
        self.familiar_id = familiar_id
        self.updated_at = datetime.now(UTC)

    def mark_processing(self, familiar_id: str) -> None:
        """Mark job as being processed."""
        self.status = SpellJobStatus.PROCESSING
        self.familiar_id = familiar_id
        self.updated_at = datetime.now(UTC)

    def mark_completed(
        self,
        results: list[SearchResult],
        ai_summary: str | None = None,
    ) -> None:
        """Mark job as completed with results."""
        self.status = SpellJobStatus.COMPLETED
        self.results = results
        self.ai_summary = ai_summary
        self.completed_at = datetime.now(UTC)
        self.updated_at = datetime.now(UTC)

    def mark_failed(self, error: str) -> None:
        """Mark job as failed with error."""
        self.status = SpellJobStatus.FAILED
        self.error = error
        self.completed_at = datetime.now(UTC)
        self.updated_at = datetime.now(UTC)

    def to_result(self) -> SpellResult:
        """Convert job to result."""
        return SpellResult(
            job_id=self.job_id,
            results=self.results,
            ai_summary=self.ai_summary,
            error=self.error,
            completed_at=self.completed_at,
        )


class SpellSubmission(BaseModel):
    """Response after submitting a spell."""

    job_id: Annotated[UUID, Field(examples=["550e8400-e29b-41d4-a716-446655440000"])]
    status: Annotated[SpellJobStatus, Field(examples=["pending"])]
    message: Annotated[str, Field(examples=["Spell queued for processing"])]


class JobResultsResponse(BaseModel):
    """Response from /spells/{job_id}/results endpoint."""

    status: JobResultStatus
    message: str


class JobAcceptRequest(BaseModel):
    """Request to accept a job assignment."""

    familiar_id: str


class JobRejectRequest(BaseModel):
    """Request to reject a job assignment."""

    familiar_id: str
    reason: str = "busy"


class JobActionResponse(BaseModel):
    """Response for job accept/reject actions."""

    success: bool
    message: str
    job_id: UUID
