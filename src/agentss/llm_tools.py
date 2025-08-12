from __future__ import annotations

import re
from enum import StrEnum

import anyio
import httpx
from agents import function_tool
from pydantic import BaseModel

from .context import get_context
from .db_access import fetch_paper_by_doi, fetch_textlines_by_doi
from .models import PaperRecord, PaperText, UrlValidationResult
from .search_client import TowerClient
from .search_contracts.core import SearchEngine, Spell, SpellComponents, SpellJobStatus


class GetPaperResult(BaseModel):
    paper: PaperRecord | None
    has_text: bool


@function_tool
async def get_paper() -> GetPaperResult:  # type: ignore[no-untyped-def]
    ctx = get_context()
    doi = ctx.doi or ""
    db_path = ctx.db_path or ""
    paper, _paper_id = await fetch_paper_by_doi(db_path, doi)
    if paper is None:
        return GetPaperResult(paper=None, has_text=False)
    text = await fetch_textlines_by_doi(db_path, doi)
    return GetPaperResult(paper=paper, has_text=len(text.text_lines) > 0)


class GetTextLinesResult(BaseModel):
    text_lines: list[str]


@function_tool
async def get_textlines(  # type: ignore[no-untyped-def]
    offset: int = 0, limit: int = 1000
) -> GetTextLinesResult:
    ctx = get_context()
    doi = ctx.doi or ""
    db_path = ctx.db_path or ""
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


@function_tool
async def grep_text(  # type: ignore[no-untyped-def]
    pattern: str,
    regex: bool = True,
    max_hits: int = 20,
    context_lines: int = 2,
) -> GrepResult:
    ctx = get_context()
    doi = ctx.doi or ""
    db_path = ctx.db_path or ""
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


@function_tool
async def search_web(  # type: ignore[no-untyped-def]
    query: str, total_results: int = 50
) -> WebSearchResultList:
    base_url = get_context().tower_base_url or ""
    async with TowerClient(base_url=base_url) as client:
        items = await _tower_search(client, query, total_results)
        return WebSearchResultList(results=items)


@function_tool
async def search_site(  # type: ignore[no-untyped-def]
    site: str, terms: str, total_results: int = 50
) -> WebSearchResultList:
    q = f"site:{site} {terms}".strip()
    base_url = get_context().tower_base_url or ""
    async with TowerClient(base_url=base_url) as client:
        items = await _tower_search(client, q, total_results)
        return WebSearchResultList(results=items)


class ValidateUrlsResult(BaseModel):
    results: list[UrlValidationResult]


@function_tool
async def validate_urls(  # type: ignore[no-untyped-def]
    urls: list[str], timeout_s: float = 10.0, max_urls: int = 10
) -> ValidateUrlsResult:
    async with httpx.AsyncClient(timeout=timeout_s) as client:
        sem = anyio.Semaphore(5)
        results: list[UrlValidationResult] = []

        async def _one(u: str) -> None:
            async with sem:
                try:
                    resp = await client.get(u)
                    ok = resp.status_code < 400
                    ctype: str | None = resp.headers.get("content-type")
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
        import importlib

        readability = importlib.import_module("readability")
        Document = getattr(readability, "Document")  # type: ignore[reportAttributeAccessIssue]
        doc = Document(html)
        title_val = getattr(doc, "short_title", None)
        title_str: str | None
        if callable(title_val):
            try:
                title_str = str(title_val())
            except Exception:
                title_str = None
        else:
            title_str = None
        content_html = doc.summary(html_partial=True)  # type: ignore[reportUnknownMemberType]
        try:
            markdownify = importlib.import_module("markdownify")
            md_func = getattr(markdownify, "markdownify", None)
            md_text = md_func(content_html or "") if callable(md_func) else None
            if not isinstance(md_text, str):
                md_text = re.sub(r"<[^>]+>", " ", content_html or "")
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


class FetchModes(StrEnum):
    MARKDOWN = "markdown"
    RAW = "raw"


@function_tool
async def fetch_url(  # type: ignore[no-untyped-def]
    url: str, timeout_s: float = 20.0
) -> FetchUrlResult:
    async with httpx.AsyncClient(timeout=timeout_s, follow_redirects=True) as client:
        mode = FetchModes.MARKDOWN
        resp = await client.get(url)
        ctype: str | None = resp.headers.get("content-type")
        final_url = str(resp.url)
        if ctype and "text/html" in ctype.lower():
            html = resp.text
            if mode == FetchModes.RAW:
                content = html
                title = None
                m = re.search(r"<title[^>]*>([^<]+)</title>", html, flags=re.I)
                if m:
                    title = m.group(1).strip()
            else:
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
            mode=mode,
            content=content,
            title=title,
        )
