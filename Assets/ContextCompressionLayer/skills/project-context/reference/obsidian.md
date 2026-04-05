# Obsidian Knowledge Integration

AI behavioral guideline: read Obsidian notes for context enrichment and
propose writing structured notes back to the vault.

## Reading

Session start (optional):
- Check Index file (`00_{ProjectName}-Index.md`) if it exists
- Do not scan the entire vault

Topic-driven:
- When a conversation topic likely has related notes, search with Grep
  in `{paths.obsidianNotes}/meetings/`, etc.

Explicit request:
- When the user asks to check meeting notes, specs, etc.

## Writing Locations

| Folder | Content | When |
|---|---|---|
| daily/ | Session summary | After substantial work session |
| meetings/ | Structured meeting notes | When user shares meeting content |
| notes/ | Technical findings | When reusable knowledge is discovered |
| specs/ | Design proposals | When design decisions are documented |
| troubleshooting/ | Error resolutions | When bugs/errors are resolved |

## Note Format

```markdown
---
author: ai
created: YYYY-MM-DD
type: session-summary | meeting | note | spec
tags: [ai-memory, relevant-tag]
---

# {Title}

{Content using Obsidian syntax}

## Related

- [[related-note]]
- [[decision_log/YYYY-MM-DD_topic]]
```

## Global Knowledge Routing

Project-specific → write to `{paths.obsidianNotes}/` subdirectories

Cross-project → write to `{obsidianVaultRoot}/ai-context/`:
- `tech-patterns/`: Reusable code/design patterns across projects
- `lessons-learned/`: Failures and learnings (tag with project name)

Global save proposal format:
```
Save to global knowledge? -> ai-context/tech-patterns/{filename}.md
  {one-line summary}
```

## Proposal Rules

- Format: `Save to Obsidian? -> {folder}/{filename}`
- Max 2 proposals per session
- If declined, do not propose again in the session
