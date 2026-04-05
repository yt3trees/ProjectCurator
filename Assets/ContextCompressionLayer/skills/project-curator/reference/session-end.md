# Session End Detection

AI behavioral guideline: detect natural session boundaries and propose
updates to `current_focus.md`.

## Detection Patterns

Propose an update when detecting:
- Thank-you or wrap-up phrases ("Thanks", "That's all for now", "Let's stop here")
- Completion of a multi-step task

Do NOT propose after: short Q&A exchanges.

## Procedure

1. Summarize AI-assisted work: what was done, what was decided, what remains
2. If `tasks.md` exists, cross-reference with in-progress tasks
3. Present the proposal:

```
Update current_focus.md?

[Recent Updates] add:
  + Created 5 CRUD API endpoints
  + Added SQL indexes

[Next Actions] add:
  + Write E2E tests

(yes / edit / skip)
```

4. If significant decisions were made, also propose a Decision Log entry (1 line)
5. If new unresolved issues emerged, propose adding to `open_issues.md`
6. If existing `open_issues.md` items were resolved, propose removing them

## Rules

- Only propose additions for AI-assisted work
- Preserve all human-written lines
- Keep proposals to 3-5 lines
- Never overwrite entire `current_focus.md`
- Never edit or delete human-written lines
- Never write without user approval
