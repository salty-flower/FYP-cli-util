import json
from collections.abc import Iterable
from typing import cast

import aiosqlite

from .db_utils import sanitize_doi_for_filename
from .models import PaperRecord, PaperText


def _row_to_paper(row: Iterable[object]) -> PaperRecord:
    (_id_unused, title, authors, abstract, url, doi, conf, year) = row  # noqa: N806
    return PaperRecord(
        doi=str(doi),
        title=str(title),
        authors=str(authors),
        abstract=str(abstract),
        url=str(url),
        conf=str(conf),
        year=int(year if isinstance(year, (int, str)) else 0),
    )


async def fetch_paper_by_doi(
    db_path: str, doi: str
) -> tuple[PaperRecord | None, int | None]:
    query = "SELECT Id, Title, Authors, Abstract, Url, Doi, Conf, Year FROM Papers WHERE Doi = ?"
    async with aiosqlite.connect(db_path) as db:
        db.row_factory = aiosqlite.Row
        async with db.execute(query, (doi,)) as cur:
            row = await cur.fetchone()
            if row is None:
                return None, None
            paper = _row_to_paper(
                (
                    row["Id"],
                    row["Title"],
                    row["Authors"],
                    row["Abstract"],
                    row["Url"],
                    row["Doi"],
                    row["Conf"],
                    int(row["Year"]) if row["Year"] is not None else 0,
                )
            )
            return paper, int(row["Id"])  # type: ignore[return-value]


async def fetch_textlines_by_doi(db_path: str, doi: str) -> PaperText:
    query = "SELECT Texts FROM PdfData WHERE FileName = ?"
    async with aiosqlite.connect(db_path) as db:
        db.row_factory = aiosqlite.Row
        async with db.execute(query, (sanitize_doi_for_filename(doi),)) as cur:
            row = await cur.fetchone()
            if row is None:
                return PaperText(has_pdf=False, text_lines=[])
            text_lines = cast(list[str], json.loads(row["Texts"]))  # pyright: ignore[reportAny]
            text_lines = [line for text in text_lines for line in text.split("\n")]
            return PaperText(has_pdf=True, text_lines=text_lines)
