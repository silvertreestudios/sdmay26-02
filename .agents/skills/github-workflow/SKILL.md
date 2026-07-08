---
name: github-workflow
description: Work with GitHub issues, native sub-issues, pull requests, PR descriptions, issue comments, inline PR review comments, review-comment replies, and gists through safer Python wrappers around the gh CLI. Use when Codex needs to create or update issues, post or edit comments, reply to PR review comments, update PR bodies, fetch comments, or upload temporary review assets while avoiding PowerShell quoting, JSON, and Hashtable string-conversion problems.
---

# GitHub Workflow

Use this skill for GitHub operations where multiline text, JSON payloads, review-thread replies, or PowerShell quoting could corrupt the request. Prefer the bundled Python scripts over direct `gh` calls for comments, PR bodies, issue bodies, and review replies.

## Rules

- Use markdown body files for all comment, issue, and PR body text. Do not pass multiline bodies inline through PowerShell.
- Put temporary body files and generated workflow artifacts under `.agent-temp/` at the checkout root. Create it on demand and delete task-specific files when done.
- Run mutating commands with `--dry-run` first unless the user explicitly asks for the write and the payload is already reviewed.
- Keep authentication delegated to `gh`; these scripts call `gh api` and do not manage tokens.
- Distinguish comment types:
  - Issue comments and PR conversation comments use `/repos/{owner}/{repo}/issues/{number}/comments`.
  - Inline PR review comments use `/repos/{owner}/{repo}/pulls/{pull_number}/comments`.
  - Replies to inline review comments use `/repos/{owner}/{repo}/pulls/{pull_number}/comments/{comment_id}/replies`.
  - Editing inline review comments uses `/repos/{owner}/{repo}/pulls/comments/{comment_id}`.
- Use `gh-issue-capture` as well when the task is to create a durable follow-up issue that should follow this repo's issue schema.

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
python .agents/skills/github-workflow/scripts/gh_pr.py update-body --repo silvertreestudios/sdmay26-02 --pr 123 --body-file .agent-temp/pr-body.md --dry-run
python .agents/skills/github-workflow/scripts/gh_pr.py comment --repo silvertreestudios/sdmay26-02 --pr 123 --body-file .agent-temp/comment.md --dry-run
python .agents/skills/github-workflow/scripts/gh_pr.py list-comments --repo silvertreestudios/sdmay26-02 --pr 123
```

### Inline PR Review Comments

```powershell
python .agents/skills/github-workflow/scripts/gh_review_comments.py list --repo silvertreestudios/sdmay26-02 --pr 123
python .agents/skills/github-workflow/scripts/gh_review_comments.py reply --repo silvertreestudios/sdmay26-02 --pr 123 --comment-id 456789 --body-file .agent-temp/reply.md --dry-run
python .agents/skills/github-workflow/scripts/gh_review_comments.py update --repo silvertreestudios/sdmay26-02 --comment-id 456789 --body-file .agent-temp/reply.md --dry-run
```

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
python .agents/skills/github-workflow/scripts/gh_create_issue.py --help
python .agents/skills/github-workflow/scripts/gh_issue.py --help
python .agents/skills/github-workflow/scripts/gh_pr.py --help
python .agents/skills/github-workflow/scripts/gh_review_comments.py --help
python .agents/skills/github-workflow/scripts/gh_gist.py --help
```
