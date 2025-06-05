New feature: automatic discovery of bug-lists of a paper.

Libraries to use:

- Semantic Kernel
- <https://github.com/nickclyde/duckduckgo-mcp-server>
- <https://github.com/jae-jae/g-search-mcp>

Input:

- $env:JobName
  - this will decide input file path. It's already widely used in the codebase; refer to DumpCommands and ReplCommands.
- a list of DOIs

Output:

- for each paper
  - a list of bug-lists, if found. Format should be links to some issue-tracking system, e.g. GitHub, jira, bugzilla, etc.
  - an artifact repository, if found

Scenarios:

- A paper may present its bug-list INSIDE the PDF text, by recording issue numbers in a table or list. Repository name or link may or may not be explicitly mentioned, but rather scattered in the text.
- A paper may have an associated artifact repository, or project webpage. This repository:
  - may be mentioned in the text, via a link like "Our code and data are available at.."
  - may not be explicitly supplied. In this case, we have to search the web for it.
    - We should let the LLM to propose its own query, iteratively
    - Search via MCP servers (with respect to their rate limits)
    - After a few attempts, we should give up and mark the paper as "no artifact found"

Non-functional requirements:

- The feature should be implemented as a new command.
- Cleanly integrate with the existing codebase, putting models, services, etc. into respective folders
