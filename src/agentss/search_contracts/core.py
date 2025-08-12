"""Core models for SearchMancer Tower."""

from enum import StrEnum
from typing import Annotated

from annotated_types import Ge, Le, MinLen
from pydantic import BaseModel, Field


class SearchEngine(StrEnum):
    """Supported search engines."""

    GOOGLE = "google"
    BING = "bing"
    DUCKDUCKGO = "duckduckgo"
    YANDEX = "yandex"


class SpellJobStatus(StrEnum):
    """Status of a spell job."""

    PENDING = "pending"
    ASSIGNED = "assigned"
    PROCESSING = "processing"
    COMPLETED = "completed"
    FAILED = "failed"


class SpellComponents(BaseModel):
    """Components of a search spell."""

    query: Annotated[str, MinLen(1)] = Field(
        examples=["python fastapi tutorial", "searchmancer magic"],
    )
    pages: Annotated[int, Ge(1), Le(10)] = Field(
        1,
        examples=[1, 2, 3],
    )
    # Distributed pagination fields
    page: Annotated[int | None, Ge(1)] = Field(
        default=None,
        description="Specific page number for distributed pagination",
        examples=[1, 2, 3],
    )
    total_pages: Annotated[int | None, Ge(1)] = Field(
        default=None,
        description="Total pages in distributed search",
        examples=[3, 5, 10],
    )
    total_results_wanted: Annotated[int | None, Ge(1), Le(1000)] = Field(
        default=None,
        examples=[30, 50, 100],
    )
    language: Annotated[
        str | None, Field(description="Language code (e.g., 'en', 'fr')")
    ] = None
    region: Annotated[
        str | None, Field(description="Region code (e.g., 'us', 'uk')")
    ] = None
    safe_search: bool = False


class Spell(BaseModel):
    """A search spell request."""

    spell: SearchEngine = SearchEngine.GOOGLE
    components: Annotated[
        SpellComponents,
        Field(
            examples=[
                SpellComponents(query="SearchMancer magic", pages=1),  # type: ignore[call-arg]
            ],
        ),
    ]


class SearchResult(BaseModel):
    """A single search result."""

    title: Annotated[str, Field(examples=["FastAPI tutorial"])]
    url: Annotated[str, Field(examples=["https://fastapi.tiangolo.com/tutorial/"])]
    snippet: Annotated[
        str,
        Field(
            examples=["Learn how to use FastAPI..."],
        ),
    ]
    position: Annotated[int, Ge(1)] = Field(examples=[1])
