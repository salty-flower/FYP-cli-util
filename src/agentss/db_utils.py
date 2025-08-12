def sanitize_doi_for_filename(doi: str) -> str:
    return doi.replace("/", "-") + ".pdf"
