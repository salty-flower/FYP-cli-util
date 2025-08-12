"""Shared error models for API consistency."""

from pydantic import BaseModel


# Shared error models for API consistency
class ValidationErrorItem(BaseModel):
    """Individual validation error item - used for external IO."""

    loc: tuple[str | int, ...]
    msg: str
    type: str


class TowerErrorResponse(BaseModel):
    """Standardized Tower API error response - used for external IO."""

    detail: str | list[ValidationErrorItem]
