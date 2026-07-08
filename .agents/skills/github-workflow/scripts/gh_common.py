from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any

API_VERSION = "2022-11-28"


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


def print_plan(method: str, endpoint: str, payload: dict[str, Any] | None, paginate: bool = False) -> int:
    plan = {
        "dry_run": True,
        "method": method,
        "endpoint": endpoint,
        "paginate": paginate,
        "payload": payload,
    }
    print(json.dumps(plan, indent=2, ensure_ascii=False))
    return 0


def gh_api(
    endpoint: str,
    *,
    method: str = "GET",
    payload: dict[str, Any] | None = None,
    paginate: bool = False,
    dry_run: bool = False,
) -> int:
    method = method.upper()
    if dry_run:
        return print_plan(method, endpoint, payload, paginate)

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

    completed = subprocess.run(args, input=stdin, text=True, check=False)
    return completed.returncode


def gh_api_json(
    endpoint: str,
    *,
    method: str = "GET",
    payload: dict[str, Any] | None = None,
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

    completed = subprocess.run(args, input=stdin, text=True, capture_output=True, check=False)
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
    completed = subprocess.run(args, text=True, check=False)
    return completed.returncode
