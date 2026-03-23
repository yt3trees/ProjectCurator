---
name: commit
description: Create git commits using English commit messages with clear intent and consistent style. Use when the user asks to commit changes, asks to rewrite a commit message in English, or wants commit-message quality checks before running `git commit`.
---

# Commit

## Overview

Create high-quality English commit messages and execute commits safely.
Use a short, imperative subject and keep scope aligned with actual staged changes.

## Workflow

1. Inspect current repository state.
2. Summarize what changed from `git diff` and `git diff --cached`.
3. Decide whether to commit staged files only or stage specific files.
4. Compose an English commit message.
5. Run `git commit` and report commit hash and summary.

## Execution Reliability (PowerShell)

- For shell tool calls in this workflow, default to `login: false` to avoid shell startup overhead (for example, PSReadLine initialization delays).
- Use `timeout_ms: 60000` for `git status`, `git diff`, `git add`, and `git commit` commands unless a longer timeout is clearly needed.
- If a command still times out once, rerun it immediately with the same non-login setting before changing strategy.
- Prefer lightweight commands first (`git status --short`, `git diff --stat`) and only run full diffs when needed.

## Message Rules

- Write in English only.
- Use imperative mood in subject line.
- Keep subject concise (prefer under 72 chars).
- Avoid trailing period in subject.
- Reflect actual changes, not intent alone.
- Prefer one of these prefixes when appropriate:
  - `feat:` for user-visible behavior additions
  - `fix:` for bug fixes
  - `refactor:` for internal restructuring without behavior change
  - `docs:` for documentation-only updates
  - `chore:` for maintenance tasks

## Commit Message Template

Use this format by default:

```text
<type>: <short imperative summary>
```

Add a body only when needed for context:

```text
<type>: <short imperative summary>

- Explain why the change was needed.
- Note key implementation details or tradeoffs.
- Mention verification done (if any).
```

## Safety Checks Before Commit

- Do not include unrelated files.
- If there are mixed concerns, recommend splitting into multiple commits.
- If the index is locked or permissions fail, surface the exact error and retry with appropriate permissions.
- Do not rewrite history unless user explicitly requests it.

## Example Subjects

- `fix: handle null workspace path in setup flow`
- `refactor: centralize markdown template constants`
- `docs: add configuration examples for Asana sync`

## Output Checklist

- Present final commit message in English.
- Provide commit hash after commit succeeds.
- Provide one-line summary of files included.
