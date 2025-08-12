"""Distributed search models for SearchMancer Tower."""

from datetime import datetime
from enum import StrEnum
from typing import Annotated
from uuid import UUID

from annotated_types import Ge, Le
from pydantic import BaseModel, Field

from .core import SearchEngine, SearchResult, SpellJobStatus


class SearchMetadata(BaseModel):
    """Metadata for distributed search results - used for external IO."""

    pages_searched: int
    search_duration: float | None = None
    familiars_used: int
    average_results_per_page: float


class ProgressInfo(BaseModel):
    """Progress information for distributed search - used for external IO."""

    total_pages: int
    completed_pages: int
    failed_pages: int
    in_progress_pages: int
    completion_percentage: float
    total_results: int


# Distributed Pagination Models
class DistributedSearchStatus(StrEnum):
    """Status of distributed search operation."""

    INITIATED = "initiated"
    DISTRIBUTING = "distributing"
    IN_PROGRESS = "in_progress"
    AGGREGATING = "aggregating"
    COMPLETED = "completed"
    PARTIAL_COMPLETED = "partial_completed"
    FAILED = "failed"


class DistributedSearchRequest(BaseModel):
    """Request for distributed search across multiple pages."""

    query: str
    engine: SearchEngine
    total_results: Annotated[int, Ge(1), Le(1000)]
    include_ai_summary: bool = True
    timeout: Annotated[int, Ge(5000), Le(120000)] = 30000
    max_familiars: Annotated[int, Ge(1), Le(10)] = 5
    fallback_to_sequential: bool = True


class FamiliarAssignment(BaseModel):
    """Assignment of a page to a familiar - used for external IO."""

    page: int
    job_id: UUID
    expected_results: str
    status: str


class DistributedSearchResponse(BaseModel):
    """Response for distributed search initiation."""

    search_id: UUID
    status: DistributedSearchStatus
    total_pages: int
    pages_per_familiar: int
    estimated_completion_time: int | None = None
    familiar_assignments: list[FamiliarAssignment] = []


class PageAssignmentStatus(BaseModel):
    """Status of a page assignment in distributed search."""

    page: int
    status: SpellJobStatus
    familiar_id: str | None = None
    expected_results: Annotated[str, Field(description="Expected result positions")]
    completed_at: datetime | None = None


class DistributedSearchStatusResponse(BaseModel):
    """Response from /distributed-search/{search_id}/status endpoint."""

    search_id: UUID
    status: DistributedSearchStatus
    progress: ProgressInfo
    page_assignments: list[PageAssignmentStatus]
    aggregated_results_count: int
    errors: list[str]
    created_at: datetime
    updated_at: datetime


class SearchResultsResponse(BaseModel):
    """Response from /distributed-search/{search_id}/results endpoint."""

    search_id: UUID
    query: str
    engine: SearchEngine
    status: DistributedSearchStatus
    total_results: int
    results: list[SearchResult]
    ai_summary: str | None = None
    metadata: SearchMetadata
    progress: ProgressInfo
