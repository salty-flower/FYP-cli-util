import re
from functools import wraps
from typing import Any, cast

import anyio
import httpx
import markdownify
import readability
from agents import RunContextWrapper
from agents.tool import FunctionTool, ToolFunction, function_tool
from pydantic import BaseModel

from .context import AgentContext
from .db_access import fetch_paper_by_doi, fetch_textlines_by_doi
from .models import PaperRecord, PaperText, UrlValidationResult
from .search_client import TowerClient
from .search_contracts.core import SearchEngine, Spell, SpellComponents, SpellJobStatus


def json_tool(func: ToolFunction[...]) -> FunctionTool:
    """Decorator that automatically converts Pydantic model returns to JSON strings."""

    @wraps(func)
    async def wrapper(*args: Any, **kwargs: Any) -> str:  # pyright: ignore[reportAny,reportExplicitAny]
        result = await func(*args, **kwargs)  # pyright: ignore[reportAny]
        if isinstance(result, BaseModel):
            return result.model_dump_json()
        return str(
            result  # pyright: ignore[reportAny]
        )  # Fallback for non-BaseModel returns

    return function_tool(wrapper)


class GetPaperResult(BaseModel):
    paper: PaperRecord | None
    has_text: bool


@json_tool
async def get_paper(ctx: RunContextWrapper[AgentContext]) -> GetPaperResult:  # type: ignore[no-untyped-def]
    context = ctx.context
    doi = context.doi or ""
    db_path = context.db_path or ""
    paper, _paper_id = await fetch_paper_by_doi(db_path, doi)
    if paper is None:
        return GetPaperResult(paper=None, has_text=False)
    text = await fetch_textlines_by_doi(db_path, doi)
    return GetPaperResult(paper=paper, has_text=len(text.text_lines) > 0)


class GetTextLinesResult(BaseModel):
    text_lines: list[str]


@json_tool
async def get_textlines(  # type: ignore[no-untyped-def]
    ctx: RunContextWrapper[AgentContext], offset: int = 0, limit: int = 1000
) -> GetTextLinesResult:
    context = ctx.context
    doi = context.doi or ""
    db_path = context.db_path or ""
    text: PaperText = await fetch_textlines_by_doi(db_path, doi)
    if offset < 0:
        offset = 0
    if limit <= 0:
        limit = 1000
    return GetTextLinesResult(text_lines=text.text_lines[offset : offset + limit])


class GrepHit(BaseModel):
    line_no: int
    match_text: str
    context_before: str | None = None
    context_after: str | None = None


class GrepResult(BaseModel):
    hits: list[GrepHit]


@json_tool
async def grep_text(
    ctx: RunContextWrapper[AgentContext],
    pattern: str,
    regex: bool = True,
    max_hits: int = 20,
    context_lines: int = 2,
) -> GrepResult:
    context = ctx.context
    doi = context.doi or ""
    db_path = context.db_path or ""
    text = await fetch_textlines_by_doi(db_path, doi)
    lines = text.text_lines
    hits: list[GrepHit] = []
    if regex:
        try:
            compiled = re.compile(pattern, flags=re.IGNORECASE)
        except re.error:
            compiled = re.compile(re.escape(pattern), flags=re.IGNORECASE)
        for idx, line in enumerate(lines):
            if compiled.search(line):
                start = max(0, idx - context_lines)
                end = min(len(lines), idx + context_lines + 1)
                hits.append(
                    GrepHit(
                        line_no=idx + 1,
                        match_text=line,
                        context_before="\n".join(lines[start:idx])
                        if start < idx
                        else None,
                        context_after="\n".join(lines[idx + 1 : end])
                        if idx + 1 < end
                        else None,
                    )
                )
                if len(hits) >= max_hits:
                    break
    else:
        needle = pattern.lower()
        for idx, line in enumerate(lines):
            if needle in line.lower():
                start = max(0, idx - context_lines)
                end = min(len(lines), idx + context_lines + 1)
                hits.append(
                    GrepHit(
                        line_no=idx + 1,
                        match_text=line,
                        context_before="\n".join(lines[start:idx])
                        if start < idx
                        else None,
                        context_after="\n".join(lines[idx + 1 : end])
                        if idx + 1 < end
                        else None,
                    )
                )
                if len(hits) >= max_hits:
                    break
    return GrepResult(hits=hits)


class WebResult(BaseModel):
    title: str
    url: str
    snippet: str
    position: int


class WebSearchResultList(BaseModel):
    results: list[WebResult]


async def _tower_search(
    client: TowerClient, query: str, total_results: int
) -> list[WebResult]:
    req = Spell(
        spell=SearchEngine.GOOGLE, components=SpellComponents(query=query, pages=1)
    )
    # Fallback to distributed if larger results are needed in the future.
    submission = await client.submit_spell(req)
    # Poll until completion
    for _ in range(60):
        job = await client.get_spell_job(submission.job_id)
        if job.status == SpellJobStatus.COMPLETED:
            raw = job.results or []
            out: list[WebResult] = []
            for r in raw[:total_results]:
                out.append(
                    WebResult(
                        title=r.title, url=r.url, snippet=r.snippet, position=r.position
                    )
                )
            return out
        if job.status == SpellJobStatus.FAILED:
            return []
        await anyio.sleep(0.5)
    return []


@json_tool
async def search_web(
    ctx: RunContextWrapper[AgentContext], query: str, total_results: int = 50
) -> WebSearchResultList:
    base_url = ctx.context.tower_base_url or ""
    async with TowerClient(base_url=base_url) as client:
        items = await _tower_search(client, query, total_results)
        return WebSearchResultList(results=items)


@json_tool
async def search_site(
    ctx: RunContextWrapper[AgentContext], site: str, terms: str, total_results: int = 50
) -> WebSearchResultList:
    q = f"site:{site} {terms}".strip()
    base_url = ctx.context.tower_base_url or ""
    async with TowerClient(base_url=base_url) as client:
        items = await _tower_search(client, q, total_results)
        return WebSearchResultList(results=items)


class ValidateUrlsResult(BaseModel):
    results: list[UrlValidationResult]


@json_tool
async def validate_urls(  # type: ignore[no-untyped-def]
    _ctx: RunContextWrapper[AgentContext],
    urls: list[str],
    timeout_s: float = 10.0,
    max_urls: int = 10,
) -> ValidateUrlsResult:
    async with httpx.AsyncClient(timeout=timeout_s) as client:
        sem = anyio.Semaphore(5)
        results: list[UrlValidationResult] = []

        async def _one(u: str) -> None:
            async with sem:
                try:
                    resp = await client.get(u)
                    ok = resp.status_code < 400
                    ctype: str | None = cast(
                        str | None, resp.headers.get("content-type")
                    )
                    title = None
                    desc = None
                    if ok and ctype is not None and ctype.startswith("text/html"):
                        text = resp.text
                        m = re.search(r"<title[^>]*>([^<]+)</title>", text, flags=re.I)
                        if m:
                            title = m.group(1).strip()
                        m2 = re.search(
                            r"<meta[^>]*name=[\"']description[\"'][^>]*content=[\"']([^\"']+)[\"']",
                            text,
                            flags=re.I,
                        )
                        if m2:
                            desc = m2.group(1).strip()
                    result = UrlValidationResult(
                        url=u,
                        accessible=ok,
                        status_code=resp.status_code,
                        title=title,
                        description=desc,
                        content_type=ctype,
                    )
                except Exception as exc:  # noqa: BLE001
                    result = UrlValidationResult(
                        url=u, accessible=False, error=str(exc)
                    )
                results.append(result)

        urls_capped = urls[:max_urls]
        async with anyio.create_task_group() as tg:
            for u in urls_capped:
                tg.start_soon(_one, u)
        return ValidateUrlsResult(results=results)


# -------- Fetch URL + optional markdown conversion --------


class FetchUrlResult(BaseModel):
    url: str
    final_url: str
    status_code: int
    content_type: str | None
    mode: str
    content: str
    title: str | None = None


def _html_to_markdown(html: str) -> tuple[str, str | None]:
    # Prefer readability-lxml + markdownify if available, import dynamically to avoid hard deps in type-check
    try:
        doc = readability.Document(html)
        title_val = getattr(doc, "short_title", None)
        title_str: str | None
        if callable(title_val):
            try:
                title_str = str(title_val())
            except Exception:
                title_str = None
        else:
            title_str = None
        content_html = cast(str, doc.summary(html_partial=True))
        try:
            md_text = markdownify.markdownify(content_html or "")
        except Exception:
            md_text = re.sub(r"<[^>]+>", " ", content_html or "")
        return md_text.strip(), title_str
    except Exception:
        # Minimal fallback: strip tags
        title = None
        m = re.search(r"<title[^>]*>([^<]+)</title>", html, flags=re.I)
        if m:
            title = m.group(1).strip()
        text = re.sub(r"<script[\s\S]*?</script>", " ", html, flags=re.I)
        text = re.sub(r"<style[\s\S]*?</style>", " ", text, flags=re.I)
        text = re.sub(r"<[^>]+>", " ", text)
        text = re.sub(r"\s+", " ", text)
        return text.strip(), title


@json_tool
async def fetch_url(  # type: ignore[no-untyped-def]
    _ctx: RunContextWrapper[AgentContext], url: str, timeout_s: float = 20.0
) -> FetchUrlResult:
    async with httpx.AsyncClient(timeout=timeout_s, follow_redirects=True) as client:
        resp = await client.get(url)
        ctype: str | None = cast(str | None, resp.headers.get("content-type"))
        final_url = str(resp.url)
        if ctype and "text/html" in ctype.lower():
            html = resp.text
            content, title = _html_to_markdown(html)

        elif ctype and "text/" in ctype.lower():
            content = resp.text
            title = None
        else:
            content = ""
            title = None
        return FetchUrlResult(
            url=url,
            final_url=final_url,
            status_code=resp.status_code,
            content_type=ctype,
            mode="markdown",
            content=content,
            title=title,
        )
