import os
from pathlib import Path
from typing import Any, ClassVar, override

import logfire
from langfuse import Langfuse
from pydantic import computed_field
from pydantic_settings import BaseSettings, SettingsConfigDict


class AgentSettings(BaseSettings):
    model_config: ClassVar[SettingsConfigDict] = SettingsConfigDict(
        env_file=f"{Path(__file__).parent}/.env", env_file_encoding="utf-8"
    )

    # Observability and auth
    langfuse_public_key: str
    langfuse_secret_key: str
    langfuse_host: str | None = None
    openai_api_key: str

    # LLM model
    model: str = "o4-mini"

    # Data paths
    db_path: str = f"{Path(__file__).parent.parent.parent}/data/data-collection.db"
    pdf_root: str = f"{Path(__file__).parent.parent.parent}/data/paper-PDFs"

    # Tower client
    tower_base_url: str = "http://localhost:1237"
    tower_api_prefix: str = "/api/v1"
    http_timeout_s: float = 30.0

    # Concurrency and timeouts
    max_concurrency: int = 8
    request_timeout_s: float = 45.0

    # Discovery thresholds
    max_rounds: int = 3
    min_confidence_to_succeed: float = 0.6

    # Output
    output_extension: str = "json"

    @override
    def model_post_init(self, _context: Any) -> None:  # pyright: ignore[reportAny,reportExplicitAny]
        self.init_logfire()
        self.export_oai_env_vars()
        _ = self.langfuse_client  # construct once to apply

    def export_oai_env_vars(self) -> None:
        os.environ["OPENAI_API_KEY"] = self.openai_api_key

    def init_logfire(self) -> None:
        _ = logfire.configure(
            service_name="agent",
            send_to_logfire=False,
        )
        logfire.instrument_openai_agents()

    @computed_field
    @property
    def langfuse_client(self) -> Langfuse:
        return Langfuse(
            public_key=self.langfuse_public_key,
            secret_key=self.langfuse_secret_key,
            host=self.langfuse_host,
        )


# Singleton settings instance for convenience
def load_settings() -> AgentSettings:
    return AgentSettings()  # pyright: ignore[reportCallIssue]


settings = load_settings()
