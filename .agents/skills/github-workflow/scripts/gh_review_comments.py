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
            pageInfo { hasNextPage endCursor }
          }
        }
        pageInfo { hasNextPage endCursor }
      }
    }
  }
}
"""

REVIEW_THREAD_COMMENTS_QUERY = """
query($threadId: ID!, $cursor: String!) {
  node(id: $threadId) {
    ... on PullRequestReviewThread {
      comments(first: 100, after: $cursor) {
        nodes {
          databaseId
          author { login }
          body
          url
          createdAt
        }
        pageInfo { hasNextPage endCursor }
      }
    }
  }
}
"""

REVIEW_THREAD_SCOPE_QUERY = """
query($threadId: ID!) {
  node(id: $threadId) {
    __typename
    ... on PullRequestReviewThread {
      id
      repository { nameWithOwner }
      pullRequest { number }
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

    create = subparsers.add_parser(
        "create",
        help="Create a top-level inline review comment on the current PR head.",
    )
    add_repo_argument(create)
    create.add_argument("--pr", required=True, type=int)
    create.add_argument("--commit-id", required=True, help="Expected current PR head SHA.")
    create.add_argument("--path", required=True, help="Repository-relative path in the PR diff.")
    create.add_argument("--line", required=True, type=int, help="Line number in the diff blob.")
    create.add_argument("--side", choices=("LEFT", "RIGHT"), default="RIGHT")
    add_body_file_argument(create)
    add_dry_run_argument(create)

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
    resolve.add_argument("--pr", required=True, type=int)
    resolve.add_argument("--thread-id", required=True)
    add_dry_run_argument(resolve)

    return parser


def create_review_comment(
    repo: str,
    pr: int,
    commit_id: str,
    path: str,
    line: int,
    side: str,
    body: str,
    *,
    dry_run: bool,
) -> int:
    if line < 1:
        raise SystemExit("--line must be a positive integer")

    base = repo_path(repo)
    payload = {
        "body": body,
        "commit_id": commit_id,
        "path": path,
        "line": line,
        "side": side,
        "subject_type": "line",
    }
    endpoint = f"{base}/pulls/{pr}/comments"
    if dry_run:
        plan = {
            "dry_run": True,
            "verification": {
                "repository": repo,
                "pull_request": pr,
                "expected_head": commit_id,
            },
            "method": "POST",
            "endpoint": endpoint,
            "payload": payload,
        }
        print(json.dumps(plan, indent=2, ensure_ascii=False))
        return 0

    pull_request = gh_api_json(f"{base}/pulls/{pr}")
    actual_head = pull_request["head"]["sha"]
    if actual_head != commit_id:
        raise SystemExit(
            f"pull request {repo}#{pr} head is {actual_head}, not {commit_id}"
        )
    return gh_api(endpoint, method="POST", payload=payload)


def complete_thread_comments(thread: dict[str, object]) -> None:
    comments = thread["comments"]
    if not isinstance(comments, dict):
        raise SystemExit(f"review thread {thread['id']} returned invalid comments data")

    while comments["pageInfo"]["hasNextPage"]:
        cursor = comments["pageInfo"]["endCursor"]
        if not cursor:
            raise SystemExit(f"review thread {thread['id']} has no comment pagination cursor")

        payload = {
            "query": REVIEW_THREAD_COMMENTS_QUERY,
            "variables": {"threadId": thread["id"], "cursor": cursor},
        }
        result = gh_api_json("graphql", method="POST", payload=payload)
        node = result["data"]["node"]
        if node is None:
            raise SystemExit(f"review thread {thread['id']} was not found while paginating comments")

        next_comments = node["comments"]
        comments["nodes"].extend(next_comments["nodes"])
        comments["pageInfo"] = next_comments["pageInfo"]


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
        for thread in connection["nodes"]:
            complete_thread_comments(thread)
            threads.append(thread)
        page_info = connection["pageInfo"]
        if not page_info["hasNextPage"]:
            break
        cursor = page_info["endCursor"]

    print(json.dumps(threads, indent=2, ensure_ascii=False))
    return 0


def verify_thread_scope(repo: str, pr: int, thread_id: str) -> None:
    payload = {
        "query": REVIEW_THREAD_SCOPE_QUERY,
        "variables": {"threadId": thread_id},
    }
    result = gh_api_json("graphql", method="POST", payload=payload)
    thread = result["data"]["node"]
    if thread is None or thread.get("__typename") != "PullRequestReviewThread":
        raise SystemExit(f"review thread {thread_id} was not found")

    actual_repo = thread["repository"]["nameWithOwner"]
    actual_pr = thread["pullRequest"]["number"]
    if actual_repo.casefold() != repo.casefold() or actual_pr != pr:
        raise SystemExit(
            f"review thread {thread_id} belongs to {actual_repo}#{actual_pr}, not {repo}#{pr}"
        )


def resolve_review_thread(repo: str, pr: int, thread_id: str, *, dry_run: bool) -> int:
    payload = {
        "query": RESOLVE_THREAD_MUTATION,
        "variables": {"threadId": thread_id},
    }
    if dry_run:
        plan = {
            "dry_run": True,
            "verification": {
                "repository": repo,
                "pull_request": pr,
                "thread_id": thread_id,
            },
            "method": "POST",
            "endpoint": "graphql",
            "payload": payload,
        }
        print(json.dumps(plan, indent=2, ensure_ascii=False))
        return 0

    verify_thread_scope(repo, pr, thread_id)
    return gh_api("graphql", method="POST", payload=payload)


def main() -> int:
    args = build_parser().parse_args()
    base = repo_path(args.repo)

    if args.command == "list":
        return gh_api(f"{base}/pulls/{args.pr}/comments", paginate=True)

    if args.command == "list-threads":
        return list_review_threads(args.repo, args.pr)

    if args.command == "create":
        return create_review_comment(
            args.repo,
            args.pr,
            args.commit_id,
            args.path,
            args.line,
            args.side,
            read_body(args.body_file),
            dry_run=args.dry_run,
        )

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
        return resolve_review_thread(
            args.repo,
            args.pr,
            args.thread_id,
            dry_run=args.dry_run,
        )

    raise AssertionError(args.command)


if __name__ == "__main__":
    raise SystemExit(main())
