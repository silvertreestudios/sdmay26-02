---
name: github-workflow
description: Work with GitHub issues, native sub-issues, draft pull requests, PR descriptions, review requests, checks, submitted reviews, inline review threads, replies, thread resolution, and gists through safer Python wrappers around the gh CLI. Use when Codex needs to create or update GitHub work, run the iterative PR review workflow, or avoid PowerShell quoting and JSON corruption.
---

# GitHub Workflow

Use this skill for GitHub operations where multiline text, JSON payloads, review-thread replies, or PowerShell quoting could corrupt the request. Prefer the bundled Python scripts over direct `gh` calls for comments, PR bodies, issue bodies, and review replies.

## Rules

- Use markdown body files for all comment, issue, and PR body text. Do not pass multiline bodies inline through PowerShell.
- Put temporary body files and generated workflow artifacts under `.agent-temp/` at the checkout root. Create it on demand and delete task-specific files when done.
- Run mutating commands with `--dry-run` first unless the user explicitly asks for the write and the payload is already reviewed.
- Keep authentication delegated to `gh`. If `GH_ACCOUNT_COPILOT_PERM` is set, `request-review` obtains that account's existing credential with `gh auth token`, scopes it to the Copilot-only API subprocess, and leaves all other requests on the default `gh` identity. Never print or persist the token.
- Distinguish comment types:
  - Issue comments and PR conversation comments use `/repos/{owner}/{repo}/issues/{number}/comments`.
  - Inline PR review comments use `/repos/{owner}/{repo}/pulls/{pull_number}/comments`.
  - Replies to inline review comments use `/repos/{owner}/{repo}/pulls/{pull_number}/comments/{comment_id}/replies`.
  - Editing inline review comments uses `/repos/{owner}/{repo}/pulls/comments/{comment_id}`.
- Use `gh-issue-capture` after a human explicitly approves creating a durable follow-up issue that should follow this repo's issue schema.

## Scripts

All scripts live in `scripts/` and accept `--repo owner/name`. Most scripts print GitHub API output for live calls and a structured plan for `--dry-run` calls; `gh_create_issue.py` prints a concise created issue/link summary.

### Issues

```powershell
New-Item -ItemType Directory -Path .agent-temp -Force | Out-Null
python .agents/skills/github-workflow/scripts/gh_create_issue.py --repo silvertreestudios/sdmay26-02 --title "[Area] Title" --body-file .agent-temp/issue.md --label "type: bug" --dry-run
python .agents/skills/github-workflow/scripts/gh_create_issue.py --repo silvertreestudios/sdmay26-02 --title "[Area] Child title" --body-file .agent-temp/child.md --label "type: bug" --parent-issue 88 --dry-run
python .agents/skills/github-workflow/scripts/gh_issue.py create --repo silvertreestudios/sdmay26-02 --title "[Area] Title" --body-file .agent-temp/issue.md --label "type: bug" --dry-run
python .agents/skills/github-workflow/scripts/gh_issue.py update --repo silvertreestudios/sdmay26-02 --issue 76 --body-file .agent-temp/issue.md --dry-run
python .agents/skills/github-workflow/scripts/gh_issue.py comment --repo silvertreestudios/sdmay26-02 --issue 76 --body-file .agent-temp/comment.md --dry-run
python .agents/skills/github-workflow/scripts/gh_issue.py list-comments --repo silvertreestudios/sdmay26-02 --issue 76
```

### Pull Requests

```powershell
python .agents/skills/github-workflow/scripts/gh_pr.py create --repo silvertreestudios/sdmay26-02 --title "[Agent] Add workflow" --head task-branch --base main --body-file .agent-temp/pr-body.md --draft --dry-run
python .agents/skills/github-workflow/scripts/gh_pr.py get --repo silvertreestudios/sdmay26-02 --pr 123
python .agents/skills/github-workflow/scripts/gh_pr.py update-body --repo silvertreestudios/sdmay26-02 --pr 123 --body-file .agent-temp/pr-body.md --dry-run
python .agents/skills/github-workflow/scripts/gh_pr.py comment --repo silvertreestudios/sdmay26-02 --pr 123 --body-file .agent-temp/comment.md --dry-run
python .agents/skills/github-workflow/scripts/gh_pr.py list-comments --repo silvertreestudios/sdmay26-02 --pr 123
python .agents/skills/github-workflow/scripts/gh_pr.py request-review --repo silvertreestudios/sdmay26-02 --pr 123 --reviewer "@copilot" --dry-run
python .agents/skills/github-workflow/scripts/gh_pr.py request-review --repo silvertreestudios/sdmay26-02 --pr 123 --reviewer "silvertreestudios/pf2e-game" --dry-run
python .agents/skills/github-workflow/scripts/gh_pr.py list-reviews --repo silvertreestudios/sdmay26-02 --pr 123
python .agents/skills/github-workflow/scripts/gh_pr.py list-review-requests --repo silvertreestudios/sdmay26-02 --pr 123
python .agents/skills/github-workflow/scripts/gh_pr.py checks --repo silvertreestudios/sdmay26-02 --pr 123
python .agents/skills/github-workflow/scripts/gh_pr.py ready --repo silvertreestudios/sdmay26-02 --pr 123 --dry-run
```

`request-review` accepts repeated `--reviewer` values. It normalizes `@copilot` to GitHub's Copilot code-review bot account and an `owner/team-slug` value to GitHub's `team_reviewers` payload after verifying that the owner matches the repository. Re-request Copilot after each fix push unless automatic review of new pushes is proven enabled.

When the default `gh` account cannot assign Copilot but another authenticated account can, set `GH_ACCOUNT_COPILOT_PERM` to that account login. The helper splits mixed reviewer requests so only Copilot assignment uses the alternate account.

### Inline PR Review Comments

```powershell
python .agents/skills/github-workflow/scripts/gh_review_comments.py list --repo silvertreestudios/sdmay26-02 --pr 123
python .agents/skills/github-workflow/scripts/gh_review_comments.py list-threads --repo silvertreestudios/sdmay26-02 --pr 123
python .agents/skills/github-workflow/scripts/gh_review_comments.py create --repo silvertreestudios/sdmay26-02 --pr 123 --commit-id HEAD_SHA --path Assets/Scripts/Example.cs --line 42 --side RIGHT --body-file .agent-temp/deferred.md --dry-run
python .agents/skills/github-workflow/scripts/gh_review_comments.py reply --repo silvertreestudios/sdmay26-02 --pr 123 --comment-id 456789 --body-file .agent-temp/reply.md --dry-run
python .agents/skills/github-workflow/scripts/gh_review_comments.py update --repo silvertreestudios/sdmay26-02 --comment-id 456789 --body-file .agent-temp/reply.md --dry-run
python .agents/skills/github-workflow/scripts/gh_review_comments.py resolve-thread --repo silvertreestudios/sdmay26-02 --pr 123 --thread-id PRRT_kwDOExample --dry-run
```

Use `create` to surface deferred work on an applicable diff line for the approved reviewer team; this does not authorize creating a follow-up issue. It verifies that `--commit-id` is the current PR head before a live mutation. Use `list-threads` to inspect `isResolved` and the complete paginated conversation before disposition. `resolve-thread` verifies that the GraphQL thread belongs to the declared repository and PR before a live mutation. Resolve only a thread ID returned by `list-threads`, and only after the current code and response justify resolution.

### Gists

Use gists for temporary review artifacts that do not belong in the PR branch. Prefer committed files or PR screenshots when the artifact is part of the work product.

```powershell
python .agents/skills/github-workflow/scripts/gh_gist.py create --description "PR 123 screenshots" --file .agent-temp/screen.png --dry-run
```

## Verification

When changing this skill, run:

```powershell
python C:\Users\Josh\.codex\skills\.system\skill-creator\scripts\quick_validate.py .agents/skills/github-workflow
python -c "from pathlib import Path; [compile(p.read_text(encoding='utf-8'), str(p), 'exec') for p in Path('.agents/skills/github-workflow/scripts').glob('*.py')]"
python -m unittest discover .agents/skills/github-workflow/tests -v
python .agents/skills/github-workflow/scripts/gh_create_issue.py --help
python .agents/skills/github-workflow/scripts/gh_issue.py --help
python .agents/skills/github-workflow/scripts/gh_pr.py --help
python .agents/skills/github-workflow/scripts/gh_review_comments.py --help
python .agents/skills/github-workflow/scripts/gh_gist.py --help
```
