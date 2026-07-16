from __future__ import annotations

import argparse
import json

from gh_common import (
    add_body_file_argument,
    add_dry_run_argument,
    add_repo_argument,
    gh_api,
    gh_api_json,
    parse_repo,
    read_body,
    repo_path,
)

REVIEW_THREADS_QUERY = """
query($owner: String!, $name: String!, $number: Int!, $cursor: String) {
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      reviewThreads(first: 100, after: $cursor) {
        nodes {
          id
          isResolved
          isOutdated
          path
          line
          originalLine
          comments(first: 100) {
            nodes {
              databaseId
              author { login }
              body
              url
              createdAt
            }
          }
        }
        pageInfo { hasNextPage endCursor }
      }
    }
  }
}
"""

RESOLVE_THREAD_MUTATION = """
mutation($threadId: ID!) {
  resolveReviewThread(input: {threadId: $threadId}) {
    thread { id isResolved }
  }
}
"""


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Safe GitHub inline PR review comment operations.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    list_parser = subparsers.add_parser("list", help="List inline review comments on a PR.")
    add_repo_argument(list_parser)
    list_parser.add_argument("--pr", required=True, type=int)

    list_threads = subparsers.add_parser(
        "list-threads",
        help="List PR review threads with resolution state.",
    )
    add_repo_argument(list_threads)
    list_threads.add_argument("--pr", required=True, type=int)

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

    resolve = subparsers.add_parser(
        "resolve-thread",
        help="Resolve a verified PR review thread by GraphQL node id.",
    )
    add_repo_argument(resolve)
    resolve.add_argument("--thread-id", required=True)
    add_dry_run_argument(resolve)

    return parser


def list_review_threads(repo: str, pr: int) -> int:
    owner, name = parse_repo(repo)
    cursor: str | None = None
    threads: list[object] = []

    while True:
        variables = {"owner": owner, "name": name, "number": pr, "cursor": cursor}
        payload = {"query": REVIEW_THREADS_QUERY, "variables": variables}
        result = gh_api_json("graphql", method="POST", payload=payload)
        pull_request = result["data"]["repository"]["pullRequest"]
        if pull_request is None:
            raise SystemExit(f"pull request {repo}#{pr} was not found")

        connection = pull_request["reviewThreads"]
        threads.extend(connection["nodes"])
        page_info = connection["pageInfo"]
        if not page_info["hasNextPage"]:
            break
        cursor = page_info["endCursor"]

    print(json.dumps(threads, indent=2, ensure_ascii=False))
    return 0


def main() -> int:
    args = build_parser().parse_args()
    base = repo_path(args.repo)

    if args.command == "list":
        return gh_api(f"{base}/pulls/{args.pr}/comments", paginate=True)

    if args.command == "list-threads":
        return list_review_threads(args.repo, args.pr)

    if args.command == "get":
        return gh_api(f"{base}/pulls/comments/{args.comment_id}")

    if args.command == "reply":
        payload = {"body": read_body(args.body_file)}
        endpoint = f"{base}/pulls/{args.pr}/comments/{args.comment_id}/replies"
        return gh_api(endpoint, method="POST", payload=payload, dry_run=args.dry_run)

    if args.command == "update":
        payload = {"body": read_body(args.body_file)}
        return gh_api(
            f"{base}/pulls/comments/{args.comment_id}",
            method="PATCH",
            payload=payload,
            dry_run=args.dry_run,
        )

    if args.command == "resolve-thread":
        payload = {
            "query": RESOLVE_THREAD_MUTATION,
            "variables": {"threadId": args.thread_id},
        }
        return gh_api("graphql", method="POST", payload=payload, dry_run=args.dry_run)

    raise AssertionError(args.command)


if __name__ == "__main__":
    raise SystemExit(main())
