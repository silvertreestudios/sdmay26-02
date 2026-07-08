from __future__ import annotations

import argparse
import json
import sys

sys.dont_write_bytecode = True

from gh_common import add_body_file_argument, add_dry_run_argument, add_repo_argument, gh_api_json, read_body, repo_path


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Create a GitHub issue and optionally link it as a native sub-issue.")
    add_repo_argument(parser)
    parser.add_argument("--title", required=True)
    add_body_file_argument(parser)
    parser.add_argument("--label", action="append", default=[], help="Label to apply; may be repeated.")
    parser.add_argument("--assignee", action="append", default=[], help="Assignee login; may be repeated.")
    parser.add_argument("--parent-issue", type=int, help="Parent issue number to attach the new issue under.")
    add_dry_run_argument(parser)
    return parser


def print_dry_run(base: str, create_payload: dict[str, object], parent_issue: int | None) -> int:
    steps: list[dict[str, object]] = [
        {
            "method": "POST",
            "endpoint": f"{base}/issues",
            "payload": create_payload,
        }
    ]

    if parent_issue is not None:
        steps.append(
            {
                "method": "POST",
                "endpoint": f"{base}/issues/{parent_issue}/sub_issues",
                "payload": {"sub_issue_id": "<created issue id>"},
            }
        )

    print(json.dumps({"dry_run": True, "steps": steps}, indent=2, ensure_ascii=False))
    return 0


def summarize_issue(issue: object) -> dict[str, object]:
    if not isinstance(issue, dict):
        return {"raw": issue}

    summary: dict[str, object] = {}
    for key in ("number", "id", "title", "html_url", "state", "sub_issues_summary"):
        value = issue.get(key)
        if value is not None:
            summary[key] = value
    return summary


def main() -> int:
    args = build_parser().parse_args()
    base = repo_path(args.repo)

    create_payload: dict[str, object] = {"title": args.title, "body": read_body(args.body_file)}
    if args.label:
        create_payload["labels"] = args.label
    if args.assignee:
        create_payload["assignees"] = args.assignee

    if args.dry_run:
        return print_dry_run(base, create_payload, args.parent_issue)

    issue = gh_api_json(f"{base}/issues", method="POST", payload=create_payload)
    result: dict[str, object] = {"issue": summarize_issue(issue)}

    if args.parent_issue is not None:
        issue_id = issue.get("id") if isinstance(issue, dict) else None
        if not isinstance(issue_id, int):
            raise SystemExit("Created issue response did not include a numeric id; cannot create sub-issue link.")

        parent = gh_api_json(
            f"{base}/issues/{args.parent_issue}/sub_issues",
            method="POST",
            payload={"sub_issue_id": issue_id},
        )
        result["parent"] = summarize_issue(parent)

    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
