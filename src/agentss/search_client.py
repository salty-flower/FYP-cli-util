"""HTTP client for communicating with SearchMancer Tower."""

import logging
from types import TracebackType
from typing import Any, TypeVar
from uuid import UUID

import httpx
from pydantic import BaseModel, ValidationError

from .search_contracts.core import Spell, SpellJobStatus
from .search_contracts.distributed import (
    DistributedSearchRequest,
    DistributedSearchResponse,
    SearchResultsResponse,
)
from .search_contracts.errors import TowerErrorResponse
from .search_contracts.jobs import SpellJob, SpellSubmission
from .search_contracts.stats import HealthResponse, StatsResponse

logger = logging.getLogger(__name__)


class SpellJobListParams(BaseModel):
    limit: int
    status: SpellJobStatus | None = None


class TowerClientError(Exception):
    pass


class TowerConnectionError(TowerClientError):
    pass


class TowerAPIError(TowerClientError):
    def __init__(
        self,
        message: str,
        status_code: int | None = None,
        response_data: TowerErrorResponse | None = None,
    ):
        super().__init__(message)
        self.status_code: int | None = status_code
        self.response_data: TowerErrorResponse | None = response_data


T = TypeVar("T", bound=BaseModel)

QueryParams = dict[str, str | int | bool | None]
JSONData = dict[str, Any]  # pyright: ignore[reportExplicitAny]


class TowerClient:
    def __init__(
        self,
        base_url: str,
        timeout: float = 30.0,
        api_prefix: str = "/api/v1",
    ):
        self.base_url: str = base_url.rstrip("/")
        self.api_prefix: str = api_prefix.rstrip("/")
        self.timeout: float = timeout
        self.client: httpx.AsyncClient = httpx.AsyncClient(timeout=timeout)

        logger.info(f"Initialized Tower client for {self.base_url}{self.api_prefix}")

    async def __aenter__(self):
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        await self.close()

    async def close(self):
        await self.client.aclose()

    def _get_url(self, path: str) -> str:
        path = path.lstrip("/")
        return f"{self.base_url}{self.api_prefix}/{path}"

    async def _request(
        self,
        method: str,
        path: str,
        json_data: JSONData | None = None,
        params: QueryParams | None = None,
    ) -> JSONData:
        """Make HTTP request to Tower API.

        Args:
            method: HTTP method
            path: API path
            json_data: JSON request body
            params: Query parameters

        Returns:
            Response data as dict

        Raises:
            TowerConnectionError: Connection failed
            TowerAPIError: API returned error
        """
        url = self._get_url(path)

        try:
            logger.debug(f"{method} {url}")
            if json_data:
                logger.debug(f"Request body: {json_data}")

            response = await self.client.request(
                method=method,
                url=url,
                json=json_data,
                params=params,
            )

            logger.debug(f"Response status: {response.status_code}")

            # Handle different response types
            if response.status_code == 204:
                # No content - return empty dict
                return {}

            if response.status_code >= 400:
                try:
                    error_json = response.json()  # pyright: ignore[reportAny]
                    error_data = TowerErrorResponse.model_validate(error_json)
                    detail = (
                        error_data.detail
                        if isinstance(error_data.detail, str)
                        else str(error_data.detail)
                    )
                except Exception:
                    detail = response.text
                    error_data = TowerErrorResponse(detail=detail)

                raise TowerAPIError(
                    f"Tower API error: {response.status_code} - {detail}",
                    status_code=response.status_code,
                    response_data=error_data,
                )

            try:
                return response.json()  # pyright: ignore[reportAny]
            except Exception as e:
                raise TowerAPIError("Invalid JSON response") from e

        except httpx.RequestError as e:
            raise TowerConnectionError("Connection to Tower failed") from e

    async def _request_typed(
        self,
        response_type: type[T],
        method: str,
        path: str,
        json_data: JSONData | None = None,
        params: QueryParams | None = None,
    ) -> T:
        """Generic typed request that handles model validation automatically."""
        data = await self._request(method, path, json_data, params)
        try:
            return response_type.model_validate(data)
        except ValidationError as e:
            raise TowerAPIError(f"Invalid response format: {e.errors()}") from e

    async def _request_typed_list(
        self,
        response_type: type[T],
        method: str,
        path: str,
        json_data: JSONData | None = None,
        params: QueryParams | None = None,
    ) -> list[T]:
        """Generic typed request for list responses."""
        data = await self._request(method, path, json_data, params)
        try:
            return [response_type.model_validate(item) for item in data]
        except ValidationError as e:
            raise TowerAPIError(f"Invalid response format: {e.errors()}") from e

    async def submit_spell(self, spell: Spell) -> SpellSubmission:
        logger.info(f"Submitting spell: {spell.spell.value} - {spell.components.query}")
        return await self._request_typed(
            SpellSubmission, "POST", "spells", json_data=spell.model_dump()
        )

    async def get_spell_job(self, job_id: UUID) -> SpellJob:
        logger.info(f"Getting spell job: {job_id}")
        return await self._request_typed(SpellJob, "GET", f"spells/{job_id}")

    async def list_distributed_searches(self) -> list[DistributedSearchResponse]:
        logger.info("Listing distributed searches")
        return await self._request_typed_list(
            DistributedSearchResponse, "GET", "distributed-search"
        )

    async def list_spell_jobs(
        self,
        status: SpellJobStatus | None = None,
        limit: int = 20,
    ) -> list[SpellJob]:
        logger.info(f"Listing spell jobs (status={status}, limit={limit})")
        query_params = SpellJobListParams(limit=limit, status=status)
        return await self._request_typed_list(
            SpellJob, "GET", "spells", params=query_params.model_dump(exclude_none=True)
        )

    async def get_stats(self) -> StatsResponse:
        logger.info("Getting Tower statistics")
        return await self._request_typed(StatsResponse, "GET", "stats")

    async def get_health(self) -> HealthResponse:
        """Health endpoint uses different URL pattern (no API prefix)."""
        logger.info("Getting Tower health status")

        url = f"{self.base_url}/health"
        try:
            response = await self.client.get(url)

            if response.status_code >= 400:
                try:
                    error_json = response.json()  # pyright: ignore[reportAny]
                    error_data = TowerErrorResponse.model_validate(error_json)
                    detail = (
                        error_data.detail
                        if isinstance(error_data.detail, str)
                        else str(error_data.detail)
                    )
                except Exception:
                    detail = response.text
                    error_data = TowerErrorResponse(detail=detail)

                raise TowerAPIError(
                    f"Tower health check failed: {response.status_code} - {detail}",
                    status_code=response.status_code,
                    response_data=error_data,
                )

            data = response.json()  # pyright: ignore[reportAny]
            return HealthResponse.model_validate(data)

        except httpx.RequestError as e:
            raise TowerConnectionError("Connection to Tower failed") from e
        except ValidationError as e:
            raise TowerAPIError(f"Invalid response format: {e.errors()}") from e

    async def initiate_distributed_search(
        self, request: DistributedSearchRequest
    ) -> DistributedSearchResponse:
        logger.info(
            f"Initiating distributed search: {request.query} on {request.engine} for {request.total_results} results"
        )
        return await self._request_typed(
            DistributedSearchResponse,
            "POST",
            "distributed-search",
            json_data=request.model_dump(),
        )

    async def get_distributed_search_status(
        self, search_id: UUID
    ) -> DistributedSearchResponse:
        logger.info(f"Getting distributed search status: {search_id}")
        return await self._request_typed(
            DistributedSearchResponse, "GET", f"distributed-search/{search_id}/status"
        )

    async def get_distributed_search_results(
        self, search_id: UUID
    ) -> SearchResultsResponse:
        logger.info(f"Getting distributed search results: {search_id}")
        return await self._request_typed(
            SearchResultsResponse, "GET", f"distributed-search/{search_id}/results"
        )

    async def ping(self) -> bool:
        """Returns True if Tower is reachable."""
        try:
            _ = await self.get_health()
            return True
        except Exception as e:
            logger.debug(f"Tower ping failed: {e}")
            return False
