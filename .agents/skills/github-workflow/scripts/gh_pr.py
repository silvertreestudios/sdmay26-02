from __future__ import annotations

import argparse

from gh_common import add_body_file_argument, add_dry_run_argument, add_repo_argument, gh_api, read_body, repo_path


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Safe GitHub pull request operations through gh api.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    get = subparsers.add_parser("get", help="Get PR metadata.")
    add_repo_argument(get)
    get.add_argument("--pr", required=True, type=int)

    update_body = subparsers.add_parser("update-body", help="Replace a pull request description/body.")
    add_repo_argument(update_body)
    update_body.add_argument("--pr", required=True, type=int)
    add_body_file_argument(update_body)
    add_dry_run_argument(update_body)

    comment = subparsers.add_parser("comment", help="Post a PR conversation comment.")
    add_repo_argument(comment)
    comment.add_argument("--pr", required=True, type=int)
    add_body_file_argument(comment)
    add_dry_run_argument(comment)

    list_comments = subparsers.add_parser("list-comments", help="List PR conversation comments, not inline review comments.")
    add_repo_argument(list_comments)
    list_comments.add_argument("--pr", required=True, type=int)

    return parser


def main() -> int:
    args = build_parser().parse_args()
    base = repo_path(args.repo)

    if args.command == "get":
        return gh_api(f"{base}/pulls/{args.pr}")

    if args.command == "update-body":
        payload = {"body": read_body(args.body_file)}
        return gh_api(f"{base}/pulls/{args.pr}", method="PATCH", payload=payload, dry_run=args.dry_run)

    if args.command == "comment":
        payload = {"body": read_body(args.body_file)}
        return gh_api(f"{base}/issues/{args.pr}/comments", method="POST", payload=payload, dry_run=args.dry_run)

    if args.command == "list-comments":
        return gh_api(f"{base}/issues/{args.pr}/comments", paginate=True)

    raise AssertionError(args.command)


if __name__ == "__main__":
    raise SystemExit(main())
