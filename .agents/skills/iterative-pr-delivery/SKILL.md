---
name: iterative-pr-delivery
description: Deliver a repository task or GitHub issue through implementation, a one-time independent local review gate, direct post-gate fixes, draft PR creation, iterative automated review, CI verification, and final human handoff. Use for any change intended for a pull request, when resuming one of those stages, or when deciding whether a PR is ready for human review.
---

# Iterative PR Delivery

Use this quality gate around the domain skill that performs the work. Keep one issue or tightly focused task per branch and PR.

## Required roles

| Stage | Required agent | Rule |
| --- | --- | --- |
| Implement | `gpt-5.6-sol`, medium | Work only in the task worktree and agreed scope. |
| Pre-PR review | Fresh `gpt-5.6-sol`, xhigh, using built-in `/review` | Review against the intended base; never reuse an implementation or fixer session. |
| Pre-PR review fix | `gpt-5.6-sol`, high | Assess every finding while the one-time local review gate is still open. |
| Post-gate change | Active delivery agent | Make and verify CI, automated-review, or human-requested fixes directly without reopening local review. |
| PR review | Configured automated reviewer | Review every pushed head that contains fixes. |
| Human review | Approved human reviewer | Request human review only after every agentic gate passes. |

If the environment cannot select the required model, reasoning level, or fresh session, write the handoff and stop at that gate. Never silently substitute a configuration or claim the gate passed. These model and fresh-session requirements apply only while their listed stage is active; after the local review gate passes, they must not block direct post-gate changes.

## Handoff evidence

Keep uncommitted handoffs under `.agent-temp/delivery/<branch>/`. Record the task URL and criteria, worktree, branch, base/head SHAs, verification commands/results, next role and model, and blockers. Record the SHA that passed the one-time local review separately from the current PR head when later changes make them differ.

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

### 3. Complete the one-time local review gate

1. Start a fresh `gpt-5.6-sol` xhigh session and launch Codex's built-in `/review` with **Review against a base branch**. For non-interactive automation, use `codex review --base <base>`.
2. Follow `.agents/review/code_review.md`, review the entire base-to-head change, and record findings against the exact head SHA.
3. In a `gpt-5.6-sol` high fix session, classify each finding:
   - `accepted`: correct and in scope; fix and test it;
   - `rejected`: incorrect or harmful; preserve concise evidence;
   - `deferred`: legitimate, independently shippable, and out of scope; if the PR exists, leave a comment on the applicable code for the approved reviewer. Before the PR exists, record the finding, file/line, and evidence in the handoff for posting immediately after draft creation. Never create a follow-up issue without explicit human approval.
4. Never follow a requested change blindly. Re-read the code, prove the concern, assess regressions and scope, and make the smallest complete correction.
5. After any code change, verify and commit, then start another fresh xhigh full review.
6. Repeat until the current head has zero actionable findings.

Stage 3 is a one-time gate. It completes when a fresh full review reports zero actionable findings; record that reviewed SHA. Once complete, the gate remains complete for the lifetime of that PR. No later code, test, documentation, CI fix, base sync, automated-review change, or human-requested change reopens Stage 3, even if the change occurs before the first automated review.

The clean local review is evidence only for its recorded SHA; do not claim that it reviewed later heads. Gate completion and exact-SHA coverage are distinct: later changes require proportionate verification and the applicable remote gates, but never another local `/review`, fresh local reviewer, or local review-fix loop.

### 4. Open the draft PR

1. After the one-time local gate passes, push the current head and create a draft PR.
2. Link the issue or originating task. Include scope, verification, the one-time local review rounds/SHA, any later verified changes, limitations, and required real screenshots.
3. Verify the remote PR head equals the current local head.
4. Post every queued deferred finding: use an inline comment on the applicable diff line when possible, otherwise use a PR conversation comment that identifies the file and line. Leave issue creation to the approved reviewer.

### 5. Iterate after the local gate

1. Request the configured automated review on the draft.
2. Wait for an automated review on the current head; automated-review comments do not count as approval.
3. Fetch review summaries, inline comments, and threads. Apply the same accepted/rejected/deferred triage.
4. Make accepted automated-review fixes and any CI repairs directly in the active delivery session. Verify, commit, and push them without starting a local review or local review-fix loop.
5. Re-request automated review unless review of new pushes is proven enabled.
6. Audit all comments and threads again. Resolve repeated comments by evidence, not duplicate changes, and repeat the automated review/fix loop until the current head has no actionable findings.

Replies to automated-review comments are for human readers unless the configured reviewer supports follow-up conversation. Resolve a thread only after its disposition and current code are verified.

Apply the same rule to every post-gate change, including CI fixes, base synchronization, and changes requested during human review: make the smallest correct change, verify it proportionately, and continue the applicable CI, automated-review, and human-review gates. Never return to Stage 3.

### 6. Hand off to the human

Verify current local and remote evidence proves:

- the focused PR head matches the final local commit;
- the one-time local review gate reached zero actionable findings at its recorded SHA;
- the configured automated reviewer reviewed the current PR head and no actionable thread remains;
- required local tests and CI pass, or unavoidable limitations are explicit;
- the PR body, task link, screenshots, and verification are current;
- no temporary, generated, or unrelated files are included.

Then mark the PR ready and request the approved human reviewer. Never merge, enable auto-merge, or substitute an agent review for human approval.

If a human reviewer requests changes, implement and verify them directly without local review. Push the new head, rerun required CI, obtain any required automated re-review, and return it to the human reviewer.
