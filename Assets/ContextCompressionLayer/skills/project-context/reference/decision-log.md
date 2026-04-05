# Decision Detection and Logging

AI behavioral guideline: autonomously detect decisions during conversation
and propose structured logging.

## Detection Patterns

Detect and propose logging when these patterns appear:

| Pattern | Example |
|---|---|
| "Let's use X" / "We'll go with X" | "Let's use PostgreSQL for the DB" |
| "X is decided" / "We decided on X" | "We decided on cursor-based pagination" |
| "Drop X" / "X is rejected" | "Let's drop Redis for now" |
| Conclusion after comparison | "B is better than A" |
| Tentative decision | "Let's go with SQLite for now" |

Do NOT detect: trivial choices (variable names, indentation), hypotheticals,
or confirmations of known facts.

## Proposal Format

```
Decision Log entry? -> {summary of the decision}
```

- Max 3 proposals per session
- If declined, back off
- Keep it to one line — do not interrupt workflow

## Two Recording Patterns

Pattern A — Decided during AI session (auto-detected):
Extract context from the conversation and draft the entry.

Pattern B — Decided elsewhere (user reports):
Ask supplementary questions:
- What other options were considered?
- Why was this chosen?
- Under what conditions should it be revisited?
(User may skip any question.)

## File Naming

`{paths.decisions}/YYYY-MM-DD_{topic}.md`

- topic: English snake_case (e.g., `db_schema_choice`)
- Same day, multiple entries: append `_a`, `_b`

## Entry Template

```markdown
# Decision: {title}

> Date: YYYY-MM-DD
> Status: Confirmed / Tentative
> Origin: AI session / Meeting / Solo judgment

## Context

{2-3 sentences}

## Options

### Option A: {name}
- Pros:
- Cons:

### Option B: {name}
- Pros:
- Cons:

## Chosen

-> Option {X}: {name}

## Why

{2-4 sentences}

## Risk

-

## Revisit Trigger

-
```

## Quality Criteria

- Options: at least 2 (if only 1, it's a fact, not a decision)
- Why: concrete rationale, not "AI recommended it"
- Revisit Trigger: measurable condition
- Missing info is OK (mark as "Unknown")

## Post-Save Actions

- Propose adding to `project_summary.md` decisions table
- If a `open_issues.md` item is resolved by this decision, propose removing it
