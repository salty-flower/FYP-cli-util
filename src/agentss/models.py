from __future__ import annotations

from enum import StrEnum

from pydantic import BaseModel, Field, computed_field


class DiscoveryTaskType(StrEnum):
    bug_list = "bug_list"
    artifact_repo = "artifact_repo"
    vulnerability = "vulnerability"
    documentation = "documentation"


class PaperRecord(BaseModel):
    doi: str
    title: str
    authors: str
    abstract: str
    url: str
    conf: str
    year: int

    @computed_field  # type: ignore[misc]
    @property
    def sanitized_doi(self) -> str:
        return self.doi.replace("/", "-")


class PaperText(BaseModel):
    has_pdf: bool = False
    text_lines: list[str] = Field(default_factory=list)


class DiscoveryContext(BaseModel):
    source_location: str | None = None
    surrounding_text: list[str] = Field(default_factory=list)
    section_title: str | None = None
    page_number: int | None = None
    related_links: list[str] = Field(default_factory=list)


class DiscoveryResult(BaseModel):
    type: str
    url: str
    title: str
    description: str | None = None
    confidence: float = 0.0
    extracted_keywords: list[str] = Field(default_factory=list)
    metadata: dict[str, str] = Field(default_factory=dict)
    context: DiscoveryContext = Field(default_factory=DiscoveryContext)


class BugPlatform(StrEnum):
    github = "github"
    gitlab = "gitlab"
    jira = "jira"
    bugzilla = "bugzilla"
    sourceforge = "sourceforge"
    website = "website"
    zenodo = "zenodo"
    figshare = "figshare"
    other = "other"


class BugItemFormat(StrEnum):
    bug_report = "bug_report"
    test_case = "test_case"
    csv_row = "csv_row"
    json_entry = "json_entry"
    sqlite_row = "sqlite_row"
    text_snippet = "text_snippet"
    other = "other"


class BugSource(StrEnum):
    in_paper = "in_paper"
    artifact_repo = "artifact_repo"
    project_site = "project_site"
    web_search = "web_search"


class BugItem(BaseModel):
    url: str
    platform: BugPlatform
    format: BugItemFormat
    source: BugSource
    container_url: str | None = None
    item_path: str | None = None
    title: str | None = None
    description: str | None = None
    confidence: float = 0.0
    repo_owner: str | None = None
    repo_name: str | None = None
    issue_number: int | None = None
    context_snippets: list[str] = Field(default_factory=list)
    tool_trace: list[str] = Field(default_factory=list)
    metadata: dict[str, str] = Field(default_factory=dict)


class UrlValidationResult(BaseModel):
    url: str
    accessible: bool
    status_code: int | None = None
    title: str | None = None
    description: str | None = None
    content_type: str | None = None
    error: str | None = None


class RunConfig(BaseModel):
    max_rounds: int = 50
    min_confidence_to_succeed: float = 0.6
    max_results: int = 50
    request_timeout_s: float = 30.0


class DiscoveryOutput(BaseModel):
    doi: str
    paper_metadata: PaperRecord
    used_textlines: bool = False
    results: list[DiscoveryResult] = Field(default_factory=list)
    bug_items: list[BugItem] = Field(default_factory=list)
    notes: list[str] = Field(default_factory=list)

    def model_dump_for_file(self) -> dict[str, object]:
        return self.model_dump()


class AgentDiscoveryResults(BaseModel):
    bug_items: list[BugItem] = Field(default_factory=list)
    notes: list[str] = Field(default_factory=list)
