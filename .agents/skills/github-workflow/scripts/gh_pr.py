from __future__ import annotations

import argparse

from gh_common import (
    add_body_file_argument,
    add_dry_run_argument,
    add_repo_argument,
    gh_api,
    gh_command,
    read_body,
    repo_path,
)

COPILOT_REVIEWER = "copilot-pull-request-reviewer[bot]"


def normalize_reviewer(value: str) -> str:
    reviewer = value.strip().lstrip("@")
    if reviewer.lower() == "copilot":
        return COPILOT_REVIEWER
    if not reviewer:
        raise SystemExit("--reviewer values cannot be empty")
    return reviewer


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Safe GitHub pull request operations through gh.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    create = subparsers.add_parser("create", help="Create a pull request.")
    add_repo_argument(create)
    create.add_argument("--title", required=True)
    create.add_argument("--head", required=True, help="Head branch name.")
    create.add_argument("--base", required=True, help="Base branch name.")
    create.add_argument("--draft", action="store_true")
    add_body_file_argument(create)
    add_dry_run_argument(create)

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

    request_review = subparsers.add_parser(
        "request-review",
        help="Request or re-request PR reviewers.",
    )
    add_repo_argument(request_review)
    request_review.add_argument("--pr", required=True, type=int)
    request_review.add_argument(
        "--reviewer",
        required=True,
        action="append",
        help="Repeat for multiple reviewers; use @copilot for Copilot code review.",
    )
    add_dry_run_argument(request_review)

    list_reviews = subparsers.add_parser("list-reviews", help="List submitted pull request reviews.")
    add_repo_argument(list_reviews)
    list_reviews.add_argument("--pr", required=True, type=int)

    list_requests = subparsers.add_parser("list-review-requests", help="List currently requested reviewers.")
    add_repo_argument(list_requests)
    list_requests.add_argument("--pr", required=True, type=int)

    ready = subparsers.add_parser("ready", help="Mark a draft pull request ready for review.")
    add_repo_argument(ready)
    ready.add_argument("--pr", required=True, type=int)
    add_dry_run_argument(ready)

    checks = subparsers.add_parser("checks", help="Show checks for a pull request.")
    add_repo_argument(checks)
    checks.add_argument("--pr", required=True, type=int)

    return parser


def main() -> int:
    args = build_parser().parse_args()
    base = repo_path(args.repo)

    if args.command == "create":
        payload = {
            "title": args.title,
            "head": args.head,
            "base": args.base,
            "body": read_body(args.body_file),
            "draft": args.draft,
        }
        return gh_api(f"{base}/pulls", method="POST", payload=payload, dry_run=args.dry_run)

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

    if args.command == "request-review":
        reviewers = list(dict.fromkeys(normalize_reviewer(value) for value in args.reviewer))
        payload = {"reviewers": reviewers}
        endpoint = f"{base}/pulls/{args.pr}/requested_reviewers"
        return gh_api(endpoint, method="POST", payload=payload, dry_run=args.dry_run)

    if args.command == "list-reviews":
        return gh_api(f"{base}/pulls/{args.pr}/reviews", paginate=True)

    if args.command == "list-review-requests":
        return gh_api(f"{base}/pulls/{args.pr}/requested_reviewers")

    if args.command == "ready":
        command = ["gh", "pr", "ready", str(args.pr), "--repo", args.repo]
        return gh_command(command, dry_run=args.dry_run)

    if args.command == "checks":
        command = ["gh", "pr", "checks", str(args.pr), "--repo", args.repo]
        return gh_command(command)

    raise AssertionError(args.command)


if __name__ == "__main__":
    raise SystemExit(main())
