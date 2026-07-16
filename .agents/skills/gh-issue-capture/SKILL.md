---
name: gh-issue-capture
description: Create, refine, or triage GitHub issues for this Unity tactics project, especially follow-up work discovered during development, code review, testing, playtesting, design review, or agent work. Use when Codex needs to turn an observation into a durable, well-labeled GitHub issue with enough context and acceptance criteria to pick up later.
---

# GitHub Issue Capture

Use this skill to record follow-up work in GitHub Issues with consistent detail and labels.

## Workflow

1. Check for an existing issue before creating a duplicate:
   `gh issue list --search "<keywords> in:title,body" --state all`
2. Classify the work with labels using the schema below.
3. Write an issue body that preserves reproduction context and a clear finish line.
4. Create the issue with `gh issue create --title "<title>" --body-file <file> --label "<label>"`.
5. If user approval is expected before remote writes, draft the issue body and labels first.

When implementation starts from an issue, use `iterative-pr-delivery` and a task-specific Git worktree under `../sdmay26-02-worktrees/`. Delete that worktree after the PR is merged, closed, or abandoned.

## Title

Use a short actionable title:

`[Area] Verb object/result`

Examples:

- `[Combat] Apply multiple attack penalty to follow-up strikes`
- `[UI] Keep HUD action buttons stable during turn changes`
- `[Tests] Add PlayMode coverage for line-of-sight blocking`

## Body Template

```markdown
## Context
Where this was discovered and why it matters.

## Current Behavior
What happens now. Include scene, branch, commit, test command, or playtest notes when known.

## Expected Behavior
What should happen instead.

## Reproduction
1. Concrete step
2. Concrete step
3. Observed result

## Acceptance Criteria
- [ ] Specific, verifiable outcome
- [ ] Test, screenshot, or validation expectation
- [ ] Relevant docs/data updated if applicable

## Notes
Links, files, suspected causes, screenshots, logs, or related issues.
```

Omit sections only when they truly do not apply. For broad cleanup or design tasks, replace `Reproduction` with `Scope`.

## Label Schema

Apply exactly one type label:

- `type: bug`: broken behavior, crash, regression, incorrect rules result.
- `type: enhancement`: new capability, feature request, design improvement.
- `type: cleanup`: refactor, dead code, naming, project hygiene, tech debt.
- `type: docs`: documentation-only work.
- `type: question`: needs investigation or product/design decision before implementation.

Apply exactly one area label:

- `area: combat`
- `area: grid`
- `area: ui`
- `area: data`
- `area: rules`
- `area: levels`
- `area: art`
- `area: audio`
- `area: tests`
- `area: build-ci`
- `area: agent-setup`

Apply one priority label when the priority is clear:

- `priority: p0`: blocks development, corrupts data, or prevents builds/tests from running.
- `priority: p1`: important gameplay or workflow issue needed soon.
- `priority: p2`: normal backlog item.
- `priority: p3`: polish, nice-to-have, or low urgency.

Apply source/status labels when useful:

- `source: playtest`
- `source: code-review`
- `source: dev`
- `status: needs-triage`
- `status: blocked`
- `status: ready`

Do not use legacy labels such as `bug`, `enhancement`, `documentation`, `Clean-Up`, or `in progress` on new issues when the normalized labels exist.

## Good Issue Standards

- Preserve enough context that a future agent can start without asking what happened.
- Include file paths, scene names, branch names, commands, and result snippets when available.
- Use acceptance criteria that can be verified with tests, Unity Editor checks, screenshots, or playtest confirmation.
- Prefer one issue per independently shippable fix. Split mixed issues by area or behavior.
- Avoid assigning a fix before confirming the cause. Put suspected causes in `Notes`.
