from __future__ import annotations

import contextlib
import io
import json
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS))

import gh_pr  # noqa: E402
import gh_review_comments  # noqa: E402
import gh_common  # noqa: E402


class CommonScriptTests(unittest.TestCase):
    def test_json_api_uses_utf8_for_github_output(self) -> None:
        completed = __import__("subprocess").CompletedProcess(
            args=[],
            returncode=0,
            stdout='{"body": "PF2e – review"}',
            stderr="",
        )

        with patch.object(gh_common, "require_gh"), patch.object(
            gh_common.subprocess,
            "run",
            return_value=completed,
        ) as run:
            result = gh_common.gh_api_json("/example")

        self.assertEqual(result["body"], "PF2e – review")
        self.assertEqual(run.call_args.kwargs["encoding"], "utf-8")


class PullRequestScriptTests(unittest.TestCase):
    def test_create_draft_uses_body_file(self) -> None:
        argv = [
            "gh_pr.py",
            "create",
            "--repo",
            "owner/repo",
            "--title",
            "Focused change",
            "--head",
            "task",
            "--base",
            "main",
            "--body-file",
            ".agent-temp/body.md",
            "--draft",
        ]

        with (
            patch.object(sys, "argv", argv),
            patch.object(gh_pr, "read_body", return_value="PR body\n") as read_body,
            patch.object(gh_pr, "gh_api", return_value=0) as api,
        ):
            self.assertEqual(gh_pr.main(), 0)

        read_body.assert_called_once_with(".agent-temp/body.md")
        api.assert_called_once_with(
            "/repos/owner/repo/pulls",
            method="POST",
            payload={
                "title": "Focused change",
                "head": "task",
                "base": "main",
                "body": "PR body\n",
                "draft": True,
            },
            dry_run=False,
        )

    def test_request_review_normalizes_copilot_and_deduplicates(self) -> None:
        argv = [
            "gh_pr.py",
            "request-review",
            "--repo",
            "owner/repo",
            "--pr",
            "42",
            "--reviewer",
            "@copilot",
            "--reviewer",
            "clausman",
            "--reviewer",
            "clausman",
            "--dry-run",
        ]

        with patch.object(sys, "argv", argv), patch.object(gh_pr, "gh_api", return_value=0) as api:
            self.assertEqual(gh_pr.main(), 0)

        api.assert_called_once_with(
            "/repos/owner/repo/pulls/42/requested_reviewers",
            method="POST",
            payload={"reviewers": ["copilot-pull-request-reviewer[bot]", "clausman"]},
            dry_run=True,
        )

    def test_ready_uses_noninteractive_gh_command(self) -> None:
        argv = ["gh_pr.py", "ready", "--repo", "owner/repo", "--pr", "42", "--dry-run"]
        with patch.object(sys, "argv", argv), patch.object(gh_pr, "gh_command", return_value=0) as command:
            self.assertEqual(gh_pr.main(), 0)

        command.assert_called_once_with(
            ["gh", "pr", "ready", "42", "--repo", "owner/repo"],
            dry_run=True,
        )


class ReviewCommentScriptTests(unittest.TestCase):
    @staticmethod
    def page(nodes: list[dict[str, object]], has_next: bool, cursor: str | None) -> dict[str, object]:
        return {
            "data": {
                "repository": {
                    "pullRequest": {
                        "reviewThreads": {
                            "nodes": nodes,
                            "pageInfo": {
                                "hasNextPage": has_next,
                                "endCursor": cursor,
                            },
                        }
                    }
                }
            }
        }

    def test_list_threads_paginates_and_prints_one_array(self) -> None:
        pages = [
            self.page(
                [
                    {
                        "id": "thread-1",
                        "isResolved": False,
                        "comments": {
                            "nodes": [],
                            "pageInfo": {"hasNextPage": False, "endCursor": None},
                        },
                    }
                ],
                True,
                "next",
            ),
            self.page(
                [
                    {
                        "id": "thread-2",
                        "isResolved": True,
                        "comments": {
                            "nodes": [],
                            "pageInfo": {"hasNextPage": False, "endCursor": None},
                        },
                    }
                ],
                False,
                None,
            ),
        ]
        output = io.StringIO()

        with patch.object(gh_review_comments, "gh_api_json", side_effect=pages) as api:
            with contextlib.redirect_stdout(output):
                self.assertEqual(gh_review_comments.list_review_threads("owner/repo", 42), 0)

        self.assertEqual([thread["id"] for thread in json.loads(output.getvalue())], ["thread-1", "thread-2"])
        self.assertEqual(api.call_count, 2)
        first_variables = api.call_args_list[0].kwargs["payload"]["variables"]
        second_variables = api.call_args_list[1].kwargs["payload"]["variables"]
        self.assertIsNone(first_variables["cursor"])
        self.assertEqual(second_variables["cursor"], "next")

    def test_list_threads_paginates_each_comment_connection(self) -> None:
        first_comment = {"databaseId": 1}
        second_comment = {"databaseId": 2}
        pages = [
            self.page(
                [
                    {
                        "id": "thread-1",
                        "comments": {
                            "nodes": [first_comment],
                            "pageInfo": {"hasNextPage": True, "endCursor": "comments-next"},
                        },
                    }
                ],
                False,
                None,
            ),
            {
                "data": {
                    "node": {
                        "comments": {
                            "nodes": [second_comment],
                            "pageInfo": {"hasNextPage": False, "endCursor": None},
                        }
                    }
                }
            },
        ]
        output = io.StringIO()

        with patch.object(gh_review_comments, "gh_api_json", side_effect=pages) as api:
            with contextlib.redirect_stdout(output):
                self.assertEqual(gh_review_comments.list_review_threads("owner/repo", 42), 0)

        comments = json.loads(output.getvalue())[0]["comments"]["nodes"]
        self.assertEqual([comment["databaseId"] for comment in comments], [1, 2])
        self.assertEqual(api.call_count, 2)
        variables = api.call_args_list[1].kwargs["payload"]["variables"]
        self.assertEqual(variables, {"threadId": "thread-1", "cursor": "comments-next"})

    def test_resolve_thread_dry_run_declares_scope_without_mutating(self) -> None:
        argv = [
            "gh_review_comments.py",
            "resolve-thread",
            "--repo",
            "owner/repo",
            "--pr",
            "42",
            "--thread-id",
            "PRRT_example",
            "--dry-run",
        ]
        output = io.StringIO()

        with (
            patch.object(sys, "argv", argv),
            patch.object(gh_review_comments, "gh_api") as api,
            patch.object(gh_review_comments, "gh_api_json") as api_json,
            contextlib.redirect_stdout(output),
        ):
            self.assertEqual(gh_review_comments.main(), 0)

        plan = json.loads(output.getvalue())
        self.assertEqual(
            plan["verification"],
            {"repository": "owner/repo", "pull_request": 42, "thread_id": "PRRT_example"},
        )
        api.assert_not_called()
        api_json.assert_not_called()

    def test_resolve_thread_verifies_scope_before_mutating(self) -> None:
        scope_result = {
            "data": {
                "node": {
                    "__typename": "PullRequestReviewThread",
                    "id": "PRRT_example",
                    "repository": {"nameWithOwner": "Owner/Repo"},
                    "pullRequest": {"number": 42},
                }
            }
        }
        with (
            patch.object(gh_review_comments, "gh_api_json", return_value=scope_result) as api_json,
            patch.object(gh_review_comments, "gh_api", return_value=0) as api,
        ):
            self.assertEqual(
                gh_review_comments.resolve_review_thread(
                    "owner/repo",
                    42,
                    "PRRT_example",
                    dry_run=False,
                ),
                0,
            )

        api_json.assert_called_once_with(
            "graphql",
            method="POST",
            payload={
                "query": gh_review_comments.REVIEW_THREAD_SCOPE_QUERY,
                "variables": {"threadId": "PRRT_example"},
            },
        )
        api.assert_called_once_with(
            "graphql",
            method="POST",
            payload={
                "query": gh_review_comments.RESOLVE_THREAD_MUTATION,
                "variables": {"threadId": "PRRT_example"},
            },
        )

    def test_resolve_thread_rejects_wrong_scope(self) -> None:
        scope_result = {
            "data": {
                "node": {
                    "__typename": "PullRequestReviewThread",
                    "id": "PRRT_example",
                    "repository": {"nameWithOwner": "owner/other"},
                    "pullRequest": {"number": 99},
                }
            }
        }
        with (
            patch.object(gh_review_comments, "gh_api_json", return_value=scope_result),
            patch.object(gh_review_comments, "gh_api") as api,
            self.assertRaisesRegex(SystemExit, "belongs to owner/other#99"),
        ):
            gh_review_comments.resolve_review_thread(
                "owner/repo",
                42,
                "PRRT_example",
                dry_run=False,
            )

        api.assert_not_called()


if __name__ == "__main__":
    unittest.main()
