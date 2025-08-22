from enum import StrEnum
from typing import Annotated, Any, Literal, cast, override

from pydantic import BaseModel, Field, ValidationError
from pydantic_settings import CliApp, CliPositionalArg, CliSubCommand


class ConfYearPapers(BaseModel):
    conference: str
    year: Annotated[int, Field(ge=1990, le=2025)]
    papers: list["PaperTraces"]


class PaperTraces(BaseModel):
    doi: str
    title: str | None
    bugs: list["Bug"]
    total_claimed_bugs: int | None = None
    claimed_bugs_by_status: dict[str, int] | None = None
    artifact_repos: list["ArtifactRepoTraces"] | None


class ArtifactRepoTraces(BaseModel):
    platform: "ArtifactPlatformName"
    found_via: Literal["paper", "web_search"]
    url: str


class Bug(BaseModel):
    claimed_status: str | None = None
    id_in_paper: str | int | None = None
    title: str | None = None
    description: str | None = None
    list_on_platforms: list["ArtifactPlatformName"] | None = None
    list_location_in_paper: str | None = None
    list_url: str | list[str] | None = None
    report: "BugReportMetadata | None" = None


class ArtifactPlatformName(StrEnum):
    github = "github"
    zenodo = "zenodo"
    figshare = "figshare"
    google_sites = "google_sites"


class BugReportPlatforms(StrEnum):
    github = "github"
    gitlab = "gitlab"
    jira = "jira"
    bugzilla = "bugzilla"
    sourceforge = "sourceforge"
    cve = "cve"
    chromium = "chromium"


class BugReportMetadata(BaseModel):
    title: str | None = None
    url: str | None = None
    platform: "BugReportPlatforms"
    platform_specific_id: str | None = None


_all_models: list[type[BaseModel]] = [
    ConfYearPapers,
    PaperTraces,
    Bug,
    ArtifactRepoTraces,
    BugReportMetadata,
]

if __name__ == "__main__":
    import json
    from pathlib import Path

    import anyio
    import rich
    from pydantic_settings import BaseSettings

    async def dump_schema(model: type[BaseModel]) -> None:
        schema_str = json.dumps(model.model_json_schema(), indent=2)
        async with await anyio.open_file(
            Path(__file__).parent / f"{model.__name__}.json", "w", encoding="utf-8"
        ) as f:
            _ = await f.write(schema_str)

    class DumpCommand(BaseSettings):
        to_dump: list[type[BaseModel]] = [ConfYearPapers, PaperTraces]

        async def cli_cmd(self) -> None:
            async with anyio.create_task_group() as tg:
                for model in self.to_dump:
                    tg.start_soon(dump_schema, model)
            rich.print("Schemas dumped: " + ", ".join(i.__name__ for i in self.to_dump))

    class ValidateCommand(BaseSettings):
        input_json_path: CliPositionalArg[Path]

        async def cli_cmd(self) -> None:
            # 1. load and parse the json, look for $schema
            # 2. parse the schema path, take out the leaf "filename.json"
            # 3. find the model with the same name, and validate the json against it
            if not self.input_json_path.exists():
                raise FileNotFoundError(f"File not found: {self.input_json_path}")
            async with await anyio.open_file(
                self.input_json_path, "r", encoding="utf-8"
            ) as f:
                json_data = cast(dict[str, Any], json.loads(await f.read()))  # pyright: ignore[reportExplicitAny]
            if "$schema" not in json_data:
                raise ValueError("JSON file does not contain $schema")
            schema_name = Path(cast(str, json_data["$schema"])).stem
            model = next((i for i in _all_models if i.__name__ == schema_name), None)
            if not model:
                raise ValueError(f"Schema not found: {schema_name}")
            try:
                _ = model.model_validate(json_data)
                rich.print(
                    f"JSON file {self.input_json_path} is valid against {schema_name}"
                )
            except ValidationError as e:
                rich.print(
                    f"JSON file {self.input_json_path} is invalid against {schema_name}: {e.errors()}"
                )

    class Cli(BaseSettings):
        dump: CliSubCommand[DumpCommand]
        validate_json: CliSubCommand[ValidateCommand]

        @override
        def model_post_init(self, context: Any, /) -> None:  # pyright: ignore[reportExplicitAny,reportAny]
            for i in _all_models:
                _ = i.model_rebuild()

        def cli_cmd(self) -> None:
            _ = CliApp.run_subcommand(self)

    _ = CliApp.run(Cli)
