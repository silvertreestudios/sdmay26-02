from __future__ import annotations

import contextlib
import io
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import call, patch

SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS))

import gh_pr  # noqa: E402
import gh_review_comments  # noqa: E402
import gh_common  # noqa: E402
import gh_gist  # noqa: E402


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
            result = gh_common.gh_api_json(
                "/example",
                environment={"GH_TOKEN": "process-token"},
            )

        self.assertEqual(result["body"], "PF2e – review")
        self.assertEqual(run.call_args.kwargs["encoding"], "utf-8")
        self.assertEqual(
            run.call_args.kwargs["env"],
            {"GH_TOKEN": "process-token"},
        )
        self.assertNotIn("process-token", run.call_args.args[0])

    def test_api_scopes_selected_account_token_to_api_process(self) -> None:
        token_result = __import__("subprocess").CompletedProcess(
            args=[],
            returncode=0,
            stdout="secret-token\n",
            stderr="",
        )
        api_result = __import__("subprocess").CompletedProcess(
            args=[],
            returncode=0,
            stdout="",
            stderr="",
        )

        with (
            patch.object(gh_common, "require_gh"),
            patch.dict(
                os.environ,
                {"GH_TOKEN": "ambient-gh-token", "GITHUB_TOKEN": "ambient-github-token"},
            ),
            patch.object(
                gh_common.subprocess,
                "run",
                side_effect=[token_result, api_result],
            ) as run,
        ):
            result = gh_common.gh_api(
                "/example",
                method="POST",
                payload={"reviewers": ["copilot-pull-request-reviewer[bot]"]},
                auth_account="clausman",
            )

        self.assertEqual(result, 0)
        self.assertEqual(
            run.call_args_list[0].args[0],
            ["gh", "auth", "token", "--hostname", "github.com", "--user", "clausman"],
        )
        self.assertNotIn("GH_TOKEN", run.call_args_list[0].kwargs["env"])
        self.assertNotIn("GITHUB_TOKEN", run.call_args_list[0].kwargs["env"])
        api_call = run.call_args_list[1]
        self.assertEqual(api_call.kwargs["env"]["GH_TOKEN"], "secret-token")
        self.assertNotIn("secret-token", api_call.args[0])

    def test_account_scoped_api_dry_run_does_not_read_token(self) -> None:
        output = io.StringIO()
        with patch.object(gh_common.subprocess, "run") as run, contextlib.redirect_stdout(output):
            result = gh_common.gh_api(
                "/example",
                method="POST",
                payload={"reviewers": ["copilot-pull-request-reviewer[bot]"]},
                dry_run=True,
                auth_account="clausman",
            )

        self.assertEqual(result, 0)
        self.assertEqual(json.loads(output.getvalue())["auth_account"], "clausman")
        run.assert_not_called()

    def test_default_auth_environment_uses_active_account_without_persisting_token(self) -> None:
        token_result = __import__("subprocess").CompletedProcess(
            args=[],
            returncode=0,
            stdout="secret-token\n",
            stderr="",
        )

        with (
            patch.object(gh_common, "require_gh"),
            patch.dict(
                os.environ,
                {"GH_TOKEN": "ambient-gh-token", "GITHUB_TOKEN": "ambient-github-token"},
            ),
            patch.object(gh_common.subprocess, "run", return_value=token_result) as run,
        ):
            environment = gh_common.gh_auth_environment()

        self.assertEqual(
            run.call_args.args[0],
            ["gh", "auth", "token", "--hostname", "github.com"],
        )
        self.assertNotIn("GH_TOKEN", run.call_args.kwargs["env"])
        self.assertNotIn("GITHUB_TOKEN", run.call_args.kwargs["env"])
        self.assertEqual(environment["GH_TOKEN"], "secret-token")
        self.assertNotIn("GITHUB_TOKEN", environment)


class GistScriptTests(unittest.TestCase):
    def test_text_create_keeps_using_gh_gist_create(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "notes.md"
            source.write_text("review notes\n", encoding="utf-8")
            argv = [
                "gh_gist.py",
                "create",
                "--description",
                "Review notes",
                "--file",
                str(source),
                "--dry-run",
            ]
            with (
                patch.object(sys, "argv", argv),
                patch.object(gh_gist, "gh_command", return_value=0) as command,
            ):
                self.assertEqual(gh_gist.main(), 0)

        command.assert_called_once_with(
            ["gh", "gist", "create", str(source), "--desc", "Review notes"],
            dry_run=True,
        )

    def test_binary_create_dry_run_declares_no_persistent_git_changes(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / "screen.png"
            source.write_bytes(b"\x89PNG\r\n\x1a\n\0binary")
            argv = ["gh_gist.py", "create", "--file", str(source), "--dry-run"]
            output = io.StringIO()
            with (
                patch.object(sys, "argv", argv),
                patch.object(gh_gist, "gh_api_json") as api,
                patch.object(gh_gist, "run_git") as git,
                contextlib.redirect_stdout(output),
            ):
                self.assertEqual(gh_gist.main(), 0)

        plan = json.loads(output.getvalue())
        self.assertEqual(plan["mode"], "isolated-binary-gist")
        self.assertFalse(plan["persistent_git_changes"])
        api.assert_not_called()
        git.assert_not_called()

    def test_binary_create_pushes_directly_without_git_remote_or_token_arguments(self) -> None:
        created = {
            "id": "gist-id",
            "html_url": "https://gist.github.com/gist-id",
            "git_pull_url": "https://gist.github.com/gist-id.git",
            "git_push_url": "https://gist.github.com/gist-id.git",
        }
        uploaded = {
            "files": {
                "screen.png": {
                    "raw_url": "https://gist.githubusercontent.com/raw/screen.png"
                }
            }
        }
        output = io.StringIO()

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "screen.png"
            source.write_bytes(b"\x89PNG\r\n\x1a\n\0binary")
            with (
                patch.object(gh_gist.Path, "cwd", return_value=root),
                patch.object(gh_gist, "require_gh"),
                patch.object(gh_gist, "require_git"),
                patch.object(gh_gist, "default_branch", return_value="main"),
                patch.object(gh_gist, "gh_api_json", side_effect=[created, uploaded]) as api,
                patch.object(gh_gist, "gh_auth_environment", return_value={"GH_TOKEN": "secret-token"}),
                patch.object(gh_gist, "run_git") as git,
                contextlib.redirect_stdout(output),
            ):
                self.assertEqual(
                    gh_gist.create_binary_gist(
                        [source],
                        "Screenshot",
                        False,
                        dry_run=False,
                    ),
                    0,
                )

        commands = [entry.args[0] for entry in git.call_args_list]
        self.assertFalse(any(command and command[0] == "remote" for command in commands))
        self.assertFalse(any(command and command[0] == "config" for command in commands))
        push_call = next(
            entry for entry in git.call_args_list if "push" in entry.args[0]
        )
        self.assertIn("https://gist.github.com/gist-id.git", push_call.args[0])
        self.assertNotIn("secret-token", push_call.args[0])
        self.assertEqual(push_call.kwargs["env"]["GH_TOKEN"], "secret-token")
        self.assertEqual(api.call_count, 2)
        for api_call in api.call_args_list:
            self.assertEqual(
                api_call.kwargs["environment"],
                {"GH_TOKEN": "secret-token"},
            )
        result = json.loads(output.getvalue())
        self.assertEqual(
            result["files"]["screen.png"],
            "https://gist.githubusercontent.com/raw/screen.png",
        )

    def test_binary_create_rejects_duplicate_flat_filenames(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            first = root / "first" / "screen.png"
            second = root / "second" / "SCREEN.PNG"
            first.parent.mkdir()
            second.parent.mkdir()
            first.write_bytes(b"\0")
            second.write_bytes(b"\0")
            with self.assertRaisesRegex(SystemExit, "unique basenames"):
                gh_gist.create_binary_gist(
                    [first, second],
                    "Screenshots",
                    False,
                    dry_run=True,
                )


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

    def test_request_review_normalizes_users_teams_and_deduplicates(self) -> None:
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
            "reviewer-one",
            "--reviewer",
            "reviewer-one",
            "--reviewer",
            "owner/pf2e-game",
            "--reviewer",
            "owner/pf2e-game",
            "--dry-run",
        ]

        with (
            patch.object(sys, "argv", argv),
            patch.dict(os.environ, {gh_pr.COPILOT_ACCOUNT_ENV: ""}),
            patch.object(gh_pr, "gh_api", return_value=0) as api,
        ):
            self.assertEqual(gh_pr.main(), 0)

        api.assert_called_once_with(
            "/repos/owner/repo/pulls/42/requested_reviewers",
            method="POST",
            payload={
                "reviewers": ["copilot-pull-request-reviewer[bot]", "reviewer-one"],
                "team_reviewers": ["pf2e-game"],
            },
            dry_run=True,
        )

    def test_request_review_scopes_configured_account_to_copilot_only(self) -> None:
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
            "reviewer-one",
            "--reviewer",
            "owner/pf2e-game",
        ]

        with (
            patch.object(sys, "argv", argv),
            patch.dict(os.environ, {gh_pr.COPILOT_ACCOUNT_ENV: "clausman"}),
            patch.object(gh_pr, "gh_api", return_value=0) as api,
        ):
            self.assertEqual(gh_pr.main(), 0)

        self.assertEqual(
            api.call_args_list,
            [
                call(
                    "/repos/owner/repo/pulls/42/requested_reviewers",
                    method="POST",
                    payload={
                        "reviewers": ["reviewer-one"],
                        "team_reviewers": ["pf2e-game"],
                    },
                    dry_run=False,
                ),
                call(
                    "/repos/owner/repo/pulls/42/requested_reviewers",
                    method="POST",
                    payload={"reviewers": ["copilot-pull-request-reviewer[bot]"]},
                    dry_run=False,
                    auth_account="clausman",
                ),
            ],
        )

    def test_request_review_rejects_team_from_another_owner(self) -> None:
        argv = [
            "gh_pr.py",
            "request-review",
            "--repo",
            "owner/repo",
            "--pr",
            "42",
            "--reviewer",
            "other-owner/pf2e-game",
        ]

        with (
            patch.object(sys, "argv", argv),
            patch.object(gh_pr, "gh_api") as api,
            self.assertRaisesRegex(SystemExit, "must belong to repository owner owner"),
        ):
            gh_pr.main()

        api.assert_not_called()

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

    def test_create_review_comment_dry_run_declares_head_scope(self) -> None:
        argv = [
            "gh_review_comments.py",
            "create",
            "--repo",
            "owner/repo",
            "--pr",
            "42",
            "--commit-id",
            "abc123",
            "--path",
            "Assets/Scripts/Example.cs",
            "--line",
            "17",
            "--side",
            "RIGHT",
            "--body-file",
            ".agent-temp/deferred.md",
            "--dry-run",
        ]
        output = io.StringIO()

        with (
            patch.object(sys, "argv", argv),
            patch.object(gh_review_comments, "read_body", return_value="Deferred finding\n"),
            patch.object(gh_review_comments, "gh_api") as api,
            patch.object(gh_review_comments, "gh_api_json") as api_json,
            contextlib.redirect_stdout(output),
        ):
            self.assertEqual(gh_review_comments.main(), 0)

        plan = json.loads(output.getvalue())
        self.assertEqual(
            plan["verification"],
            {"repository": "owner/repo", "pull_request": 42, "expected_head": "abc123"},
        )
        self.assertEqual(
            plan["payload"],
            {
                "body": "Deferred finding\n",
                "commit_id": "abc123",
                "path": "Assets/Scripts/Example.cs",
                "line": 17,
                "side": "RIGHT",
                "subject_type": "line",
            },
        )
        api.assert_not_called()
        api_json.assert_not_called()

    def test_create_review_comment_rejects_stale_head(self) -> None:
        with (
            patch.object(
                gh_review_comments,
                "gh_api_json",
                return_value={"head": {"sha": "current-head"}},
            ),
            patch.object(gh_review_comments, "gh_api") as api,
            self.assertRaisesRegex(SystemExit, "head is current-head, not stale-head"),
        ):
            gh_review_comments.create_review_comment(
                "owner/repo",
                42,
                "stale-head",
                "Assets/Scripts/Example.cs",
                17,
                "RIGHT",
                "Deferred finding\n",
                dry_run=False,
            )

        api.assert_not_called()

    def test_create_review_comment_verifies_head_before_mutating(self) -> None:
        payload = {
            "body": "Deferred finding\n",
            "commit_id": "current-head",
            "path": "Assets/Scripts/Example.cs",
            "line": 17,
            "side": "RIGHT",
            "subject_type": "line",
        }
        with (
            patch.object(
                gh_review_comments,
                "gh_api_json",
                return_value={"head": {"sha": "current-head"}},
            ) as api_json,
            patch.object(gh_review_comments, "gh_api", return_value=0) as api,
        ):
            self.assertEqual(
                gh_review_comments.create_review_comment(
                    "owner/repo",
                    42,
                    "current-head",
                    "Assets/Scripts/Example.cs",
                    17,
                    "RIGHT",
                    "Deferred finding\n",
                    dry_run=False,
                ),
                0,
            )

        api_json.assert_called_once_with("/repos/owner/repo/pulls/42")
        api.assert_called_once_with(
            "/repos/owner/repo/pulls/42/comments",
            method="POST",
            payload=payload,
        )

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
