---
name: iterative-pr-delivery
description: Deliver a repository task or GitHub issue through implementation, independent agent review, review fixes, draft PR creation, iterative GitHub Copilot review, CI verification, and final human handoff. Use for any change intended for a pull request, when resuming one of those stages, or when deciding whether a PR is ready for human review.
---

# Iterative PR Delivery

Use this quality gate around the domain skill that performs the work. Keep one issue or tightly focused task per branch and PR.

## Required roles

| Stage | Required agent | Rule |
| --- | --- | --- |
| Implement | `gpt-5.6-sol`, medium | Work only in the task worktree and agreed scope. |
| Review | Fresh `gpt-5.6-sol`, xhigh, using built-in `/review` | Review against the intended base; never reuse an implementation or fixer session. |
| Fix | `gpt-5.6-sol`, high | Assess every finding before changing code. |
| PR review | GitHub Copilot code review | Review every pushed head that contains fixes. |
| Human review | `silvertreestudios/pf2e-game` | Request the approved reviewer team only after every agentic gate passes. |

If the environment cannot select the required model, reasoning level, or fresh session, write the handoff and stop at that gate. Never silently substitute a configuration or claim the gate passed.

## Handoff evidence

Keep uncommitted handoffs under `.agent-temp/delivery/<branch>/`. Record the task URL and criteria, worktree, branch, base/head SHAs, verification commands/results, next role and model, and blockers.

Give a fresh reviewer only authoritative inputs: task, repository instructions, base/head SHAs, complete diff, and test evidence. Do not leak implementation rationale, suspected defects, or prior findings before its independent full pass. On re-review, inspect the whole diff first, then verify prior dispositions.

## Workflow

### 1. Establish scope

1. Read `AGENTS.md`, the entire task, linked specifications, relevant skills, and current code/tests.
2. Confirm the task worktree, intended base, and base SHA before editing.
3. Identify acceptance criteria, invariants, exclusions, required tests, and visual evidence.
4. Stop only when a missing decision would materially change scope; continue safe in-scope work otherwise.

### 2. Implement

1. Use applicable domain skills with `gpt-5.6-sol` medium.
2. Preserve unrelated work and avoid opportunistic cleanup.
3. Add tests proportional to risk; run narrow checks, then all suites required by the task and `AGENTS.md`.
4. Inspect status, the complete base-to-head diff, generated files, and Unity serialized changes.
5. Commit a coherent implementation and record its SHA and test evidence.

Do not create a PR until local independent review reaches zero actionable findings.

### 3. Review and fix locally

1. Start a fresh `gpt-5.6-sol` xhigh session and launch Codex's built-in `/review` with **Review against a base branch**. For non-interactive automation, use `codex review --base <base>`.
2. Follow `.agents/review/code_review.md`, review the entire base-to-head change, and record findings against the exact head SHA.
3. In a `gpt-5.6-sol` high fix session, classify each finding:
   - `accepted`: correct and in scope; fix and test it;
   - `rejected`: incorrect or harmful; preserve concise evidence;
   - `deferred`: legitimate, independently shippable, and out of scope; if the PR exists, use `github-workflow` to leave a comment on the applicable code for the approved reviewer team. Before the PR exists, record the finding, file/line, and evidence in the handoff for posting immediately after draft creation. Never create a follow-up issue without explicit human approval.
4. Never follow a requested change blindly. Re-read the code, prove the concern, assess regressions and scope, and make the smallest complete correction.
5. After any code change, verify and commit, then start another fresh xhigh full review.
6. Repeat until the current head has zero actionable findings.

A clean review applies only to its recorded SHA. Any later code change invalidates it.

### 4. Open the draft PR

1. Push the locally reviewed head and create a draft PR with `github-workflow`.
2. Link the issue or originating task. Include scope, verification, local review rounds/SHA, limitations, and required real screenshots.
3. Verify the remote PR head equals the reviewed local head.
4. Post every queued deferred finding with `github-workflow`: use an inline comment on the applicable diff line when possible, otherwise use a PR conversation comment that identifies the file and line. Leave issue creation to the approved reviewer team.

### 5. Iterate with Copilot

1. Request `@copilot` review on the draft using `github-workflow`.
2. Wait for a Copilot review on the current head; Copilot comments do not count as approval.
3. Fetch review summaries, inline comments, and threads. Apply the same accepted/rejected/deferred triage.
4. Use `gpt-5.6-sol` high for accepted fixes, verify, commit, and push them.
5. Re-request Copilot unless automatic review of new pushes is proven enabled.
6. Audit all comments and threads again. Resolve repeated comments by evidence, not duplicate changes, and repeat the Copilot review/fix loop until the current head has no actionable Copilot findings.

Replies to Copilot comments are for human readers; Copilot does not converse through them. Resolve a thread only after its disposition and current code are verified.

### 6. Hand off to the human

Verify current local and remote evidence proves:

- the focused PR head matches the final local commit;
- the pre-PR local review gate reached zero actionable findings;
- Copilot reviewed that SHA and no actionable thread remains;
- required local tests and CI pass, or unavoidable limitations are explicit;
- the PR body, task link, screenshots, and verification are current;
- no temporary, generated, or unrelated files are included.

Then mark the PR ready and request `silvertreestudios/pf2e-game` using `github-workflow`. Never merge, enable auto-merge, or substitute an agent review for human approval.

## GitHub operations

Use `github-workflow` for draft PR creation, review requests, review/check retrieval, inline deferred comments, thread replies/resolution, readiness, and PR body updates. Dry-run mutations first unless the exact payload was already reviewed.
