from __future__ import annotations

import argparse

from gh_common import add_body_file_argument, add_dry_run_argument, add_repo_argument, gh_api, read_body, repo_path


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Safe GitHub inline PR review comment operations through gh api.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    list_parser = subparsers.add_parser("list", help="List inline review comments on a PR.")
    add_repo_argument(list_parser)
    list_parser.add_argument("--pr", required=True, type=int)

    get = subparsers.add_parser("get", help="Get one inline review comment by comment id.")
    add_repo_argument(get)
    get.add_argument("--comment-id", required=True, type=int)

    reply = subparsers.add_parser("reply", help="Reply to a top-level inline review comment.")
    add_repo_argument(reply)
    reply.add_argument("--pr", required=True, type=int)
    reply.add_argument("--comment-id", required=True, type=int)
    add_body_file_argument(reply)
    add_dry_run_argument(reply)

    update = subparsers.add_parser("update", help="Edit an existing inline review comment or reply.")
    add_repo_argument(update)
    update.add_argument("--comment-id", required=True, type=int)
    add_body_file_argument(update)
    add_dry_run_argument(update)

    return parser


def main() -> int:
    args = build_parser().parse_args()
    base = repo_path(args.repo)

    if args.command == "list":
        return gh_api(f"{base}/pulls/{args.pr}/comments", paginate=True)

    if args.command == "get":
        return gh_api(f"{base}/pulls/comments/{args.comment_id}")

    if args.command == "reply":
        payload = {"body": read_body(args.body_file)}
        endpoint = f"{base}/pulls/{args.pr}/comments/{args.comment_id}/replies"
        return gh_api(endpoint, method="POST", payload=payload, dry_run=args.dry_run)

    if args.command == "update":
        payload = {"body": read_body(args.body_file)}
        return gh_api(f"{base}/pulls/comments/{args.comment_id}", method="PATCH", payload=payload, dry_run=args.dry_run)

    raise AssertionError(args.command)


if __name__ == "__main__":
    raise SystemExit(main())
