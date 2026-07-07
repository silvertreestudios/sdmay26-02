from __future__ import annotations

import argparse

from gh_common import add_body_file_argument, add_dry_run_argument, add_repo_argument, gh_api, read_body, repo_path


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Safe GitHub issue operations through gh api.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    create = subparsers.add_parser("create", help="Create a GitHub issue.")
    add_repo_argument(create)
    create.add_argument("--title", required=True)
    add_body_file_argument(create)
    create.add_argument("--label", action="append", default=[], help="Label to apply; may be repeated.")
    create.add_argument("--assignee", action="append", default=[], help="Assignee login; may be repeated.")
    add_dry_run_argument(create)

    update = subparsers.add_parser("update", help="Update a GitHub issue.")
    add_repo_argument(update)
    update.add_argument("--issue", required=True, type=int)
    update.add_argument("--title")
    update.add_argument("--body-file")
    update.add_argument("--state", choices=["open", "closed"])
    update.add_argument("--label", action="append", default=[], help="Replace labels with this list; may be repeated.")
    add_dry_run_argument(update)

    comment = subparsers.add_parser("comment", help="Post a comment on an issue or PR conversation.")
    add_repo_argument(comment)
    comment.add_argument("--issue", required=True, type=int)
    add_body_file_argument(comment)
    add_dry_run_argument(comment)

    list_comments = subparsers.add_parser("list-comments", help="List comments on an issue or PR conversation.")
    add_repo_argument(list_comments)
    list_comments.add_argument("--issue", required=True, type=int)

    return parser


def main() -> int:
    args = build_parser().parse_args()
    base = repo_path(args.repo)

    if args.command == "create":
        payload = {"title": args.title, "body": read_body(args.body_file)}
        if args.label:
            payload["labels"] = args.label
        if args.assignee:
            payload["assignees"] = args.assignee
        return gh_api(f"{base}/issues", method="POST", payload=payload, dry_run=args.dry_run)

    if args.command == "update":
        payload = {}
        if args.title is not None:
            payload["title"] = args.title
        if args.body_file is not None:
            payload["body"] = read_body(args.body_file)
        if args.state is not None:
            payload["state"] = args.state
        if args.label:
            payload["labels"] = args.label
        if not payload:
            raise SystemExit("update requires at least one of --title, --body-file, --state, or --label")
        return gh_api(f"{base}/issues/{args.issue}", method="PATCH", payload=payload, dry_run=args.dry_run)

    if args.command == "comment":
        payload = {"body": read_body(args.body_file)}
        return gh_api(f"{base}/issues/{args.issue}/comments", method="POST", payload=payload, dry_run=args.dry_run)

    if args.command == "list-comments":
        return gh_api(f"{base}/issues/{args.issue}/comments", paginate=True)

    raise AssertionError(args.command)


if __name__ == "__main__":
    raise SystemExit(main())
