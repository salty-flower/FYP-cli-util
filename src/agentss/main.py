import json
from pathlib import Path
from typing import override

import anyio
from pydantic import field_validator
from pydantic_settings import CliPositionalArg

from .common_cli_settings import CommonCliSettings
from .llm_agents import run_discovery_agent
from .models import RunConfig
from .settings import settings


class Cli(CommonCliSettings):
    dois: CliPositionalArg[list[str]]
    output_dir: Path

    # Optional overrides
    model: str | None = None
    db_path: str | None = None
    tower_url: str | None = None
    max_rounds: int | None = 30
    min_confidence: float | None = None
    max_concurrency: int | None = None

    @field_validator("output_dir")
    @classmethod
    def _ensure_dir(cls, v: Path) -> Path:
        v.mkdir(parents=True, exist_ok=True)
        return v

    @override
    async def cli_cmd(self) -> None:  # type: ignore[override]
        # Apply overrides
        if self.model:
            settings.model = self.model
        if self.db_path:
            settings.db_path = self.db_path
        if self.tower_url:
            settings.tower_base_url = self.tower_url
        if self.max_rounds is not None:
            settings.max_rounds = self.max_rounds
        if self.min_confidence is not None:
            settings.min_confidence_to_succeed = self.min_confidence
        if self.max_concurrency is not None:
            settings.max_concurrency = self.max_concurrency

        sem = anyio.Semaphore(settings.max_concurrency)

        async def _one(doi: str) -> None:
            async with sem:
                out = await run_discovery_agent(
                    doi=doi,
                    db_path=settings.db_path,
                    tower_base_url=settings.tower_base_url,
                    cfg=RunConfig(
                        max_rounds=settings.max_rounds,
                        min_confidence_to_succeed=settings.min_confidence_to_succeed,
                        max_results=50,
                        request_timeout_s=settings.request_timeout_s,
                    ),
                )
                out_path = (
                    self.output_dir
                    / f"{out.paper_metadata.sanitized_doi}.{settings.output_extension}"
                )
                _ = out_path.write_text(
                    json.dumps(out.model_dump_for_file(), ensure_ascii=False, indent=2),
                    encoding="utf-8",
                )
                self.logger.info("Wrote %s", out_path)

        async with anyio.create_task_group() as tg:
            for doi in self.dois:
                tg.start_soon(_one, doi)


if __name__ == "__main__":
    _ = Cli.run_anyio()
