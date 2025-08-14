from __future__ import annotations

from typing import cast

from agents import Agent, AgentOutputSchema, Runner

from . import llm_tools
from .context import AgentContext
from .models import AgentDiscoveryResults, DiscoveryOutput, RunConfig
from .prompts import SYSTEM_INSTRUCTIONS
from .settings import settings


async def run_discovery_agent(
    doi: str,
    db_path: str,
    tower_base_url: str,
    cfg: RunConfig,
) -> DiscoveryOutput:
    agent = Agent(
        name="DiscoveryAgent",
        instructions=SYSTEM_INSTRUCTIONS,
        tools=[
            llm_tools.get_paper,
            llm_tools.get_textlines,
            llm_tools.grep_text,
            llm_tools.search_web,
            llm_tools.search_site,
            llm_tools.validate_urls,
            llm_tools.fetch_url,
        ],
        model=settings.model,
        output_type=AgentOutputSchema(
            AgentDiscoveryResults, strict_json_schema=False
        ),  # structured output
    )

    # Provide minimal task plus inline paper metadata; admin details are in context only
    paper_meta = await _get_paper_meta_for_output(doi, db_path)
    abstract_snippet = (
        paper_meta.abstract
        if len(paper_meta.abstract) <= 1000
        else paper_meta.abstract[:1000]
    )
    user_input: str = (
        f"Discover bug lists and artifacts for DOI: {doi}. "
        f"Use grep tools on the paper text, then search GitHub and Zenodo if needed.\n\n"
        f"Paper metadata:\n"
        f"- Title: {paper_meta.title}\n"
        f"- Authors: {paper_meta.authors}\n"
        f"- DOI: {paper_meta.doi}\n"
        f"- Conf/Year: {paper_meta.conf} {paper_meta.year}\n"
        f"- URL: {paper_meta.url}\n"
        f"- Abstract: {abstract_snippet}"
    )

    # Create context for tools (not visible to the agent)
    context = AgentContext(doi=doi, db_path=db_path, tower_base_url=tower_base_url)
    result = await Runner.run(
        agent, input=user_input, context=context, max_turns=cfg.max_rounds
    )

    # Collect final output text for now; structured tool results can be added later
    out = DiscoveryOutput(
        doi=doi,
        paper_metadata=paper_meta,
        used_textlines=True,
        results=[],
        notes=[],
    )

    # If the SDK returned structured output, merge it
    try:
        payload = cast(AgentDiscoveryResults | None, result.final_output)
        if isinstance(payload, AgentDiscoveryResults):
            out.bug_items.extend(payload.bug_items)
            out.notes.extend(payload.notes)
        else:
            out.notes.append(str(cast(object, result.final_output)))
    except Exception:
        out.notes.append("Agent output unavailable")

    return out


async def _get_paper_meta_for_output(doi: str, db_path: str):
    from .db_access import fetch_paper_by_doi

    paper, _pid = await fetch_paper_by_doi(db_path, doi)
    if paper is None:
        # Minimal shell
        from .models import PaperRecord

        return PaperRecord(
            doi=doi,
            title="",
            authors="",
            abstract="",
            url="",
            conf="",
            year=0,
        )
    return paper
