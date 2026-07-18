from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

API_VERSION = "2022-11-28"


def configure_utf8_stdio() -> None:
    for stream_name in ("stdin", "stdout", "stderr"):
        stream = getattr(sys, stream_name)
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(encoding="utf-8")


configure_utf8_stdio()


def add_repo_argument(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--repo", required=True, help="Repository in owner/name form.")


def add_body_file_argument(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--body-file",
        required=True,
        help="Path to a UTF-8 markdown/text file. Use '-' to read from stdin.",
    )


def add_dry_run_argument(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--dry-run", action="store_true", help="Print the planned request without calling GitHub.")


def parse_repo(repo: str) -> tuple[str, str]:
    parts = repo.split("/", 1)
    if len(parts) != 2 or not parts[0] or not parts[1]:
        raise SystemExit("--repo must be in owner/name form")
    return parts[0], parts[1]


def repo_path(repo: str) -> str:
    owner, name = parse_repo(repo)
    return f"/repos/{owner}/{name}"


def read_body(path: str) -> str:
    if path == "-":
        return sys.stdin.read()
    return Path(path).read_text(encoding="utf-8")


def require_gh() -> None:
    if shutil.which("gh") is None:
        raise SystemExit("GitHub CLI 'gh' was not found on PATH")


def print_plan(
    method: str,
    endpoint: str,
    payload: dict[str, Any] | None,
    paginate: bool = False,
    auth_account: str | None = None,
) -> int:
    plan = {
        "dry_run": True,
        "method": method,
        "endpoint": endpoint,
        "paginate": paginate,
        "payload": payload,
    }
    if auth_account is not None:
        plan["auth_account"] = auth_account
    print(json.dumps(plan, indent=2, ensure_ascii=False))
    return 0


def gh_auth_environment(account: str | None = None) -> dict[str, str]:
    if account is not None:
        account = account.strip()
        if not account:
            raise SystemExit("GitHub authentication account cannot be empty")

    require_gh()
    token_lookup_environment = os.environ.copy()
    token_lookup_environment.pop("GH_TOKEN", None)
    token_lookup_environment.pop("GITHUB_TOKEN", None)
    token_command = ["gh", "auth", "token", "--hostname", "github.com"]
    if account is not None:
        token_command.extend(["--user", account])
    completed = subprocess.run(
        token_command,
        text=True,
        encoding="utf-8",
        capture_output=True,
        check=False,
        env=token_lookup_environment,
    )
    if completed.returncode != 0:
        if completed.stderr:
            print(completed.stderr, end="", file=sys.stderr)
        raise SystemExit(completed.returncode)

    token = completed.stdout.strip()
    if not token:
        account_description = f"account {account}" if account is not None else "the active account"
        raise SystemExit(f"GitHub CLI returned no token for {account_description}")

    environment = os.environ.copy()
    environment.pop("GITHUB_TOKEN", None)
    environment["GH_TOKEN"] = token
    return environment


def gh_api(
    endpoint: str,
    *,
    method: str = "GET",
    payload: dict[str, Any] | None = None,
    paginate: bool = False,
    dry_run: bool = False,
    auth_account: str | None = None,
) -> int:
    method = method.upper()
    if dry_run:
        return print_plan(method, endpoint, payload, paginate, auth_account)

    require_gh()
    args = [
        "gh",
        "api",
        endpoint,
        "--method",
        method,
        "--header",
        "Accept: application/vnd.github+json",
        "--header",
        f"X-GitHub-Api-Version: {API_VERSION}",
    ]
    if paginate:
        args.append("--paginate")

    stdin = None
    if payload is not None:
        args.extend(["--input", "-"])
        stdin = json.dumps(payload, ensure_ascii=False)

    environment = gh_auth_environment(auth_account) if auth_account is not None else None

    completed = subprocess.run(
        args,
        input=stdin,
        text=True,
        encoding="utf-8",
        check=False,
        env=environment,
    )
    return completed.returncode


def gh_api_json(
    endpoint: str,
    *,
    method: str = "GET",
    payload: dict[str, Any] | None = None,
    environment: dict[str, str] | None = None,
) -> Any:
    method = method.upper()
    require_gh()
    args = [
        "gh",
        "api",
        endpoint,
        "--method",
        method,
        "--header",
        "Accept: application/vnd.github+json",
        "--header",
        f"X-GitHub-Api-Version: {API_VERSION}",
    ]

    stdin = None
    if payload is not None:
        args.extend(["--input", "-"])
        stdin = json.dumps(payload, ensure_ascii=False)

    completed = subprocess.run(
        args,
        input=stdin,
        text=True,
        encoding="utf-8",
        capture_output=True,
        check=False,
        env=environment,
    )
    if completed.returncode != 0:
        if completed.stdout:
            print(completed.stdout, end="")
        if completed.stderr:
            print(completed.stderr, end="", file=sys.stderr)
        raise SystemExit(completed.returncode)

    if not completed.stdout.strip():
        return None

    try:
        return json.loads(completed.stdout)
    except json.JSONDecodeError as exc:
        print(completed.stdout, end="")
        raise SystemExit(f"GitHub API returned non-JSON output: {exc}") from exc


def gh_command(args: list[str], *, dry_run: bool = False) -> int:
    if dry_run:
        print(json.dumps({"dry_run": True, "command": args}, indent=2, ensure_ascii=False))
        return 0

    require_gh()
    completed = subprocess.run(args, text=True, encoding="utf-8", check=False)
    return completed.returncode
