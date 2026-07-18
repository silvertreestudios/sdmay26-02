from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from gh_common import (
    add_dry_run_argument,
    gh_api_json,
    gh_auth_environment,
    gh_command,
    require_gh,
)

PLACEHOLDER_FILENAME = "gist-upload-placeholder.md"
GIST_COMMIT_MESSAGE = "Add Gist files"
GIST_COMMIT_NAME = "GitHub Gist Helper"
GIST_COMMIT_EMAIL = "noreply@github.com"
ONE_SHOT_CREDENTIAL_HELPER = (
    '!f() { test "$1" = get || exit 0; '
    'printf "%s\\n" "username=x-access-token" "password=$GH_TOKEN"; }; f'
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Create text or binary GitHub gists without changing repository Git settings."
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    create = subparsers.add_parser("create", help="Create a secret gist by default.")
    create.add_argument("--description", default="", help="Gist description.")
    create.add_argument("--file", action="append", required=True, help="File to upload; may be repeated.")
    create.add_argument("--public", action="store_true", help="Create a public gist. Default is secret.")
    add_dry_run_argument(create)

    return parser


def is_binary_file(path: Path) -> bool:
    with path.open("rb") as source:
        sample = source.read(8192)

    if b"\0" in sample:
        return True
    try:
        sample.decode("utf-8")
    except UnicodeDecodeError:
        return True
    return False


def validate_flat_filenames(files: list[Path]) -> None:
    by_name: dict[str, Path] = {}
    for path in files:
        key = path.name.casefold()
        if key == PLACEHOLDER_FILENAME.casefold():
            raise SystemExit(f"Reserved Gist helper filename: {path.name}")
        if key in by_name:
            raise SystemExit(
                "Gist files must have unique basenames: "
                f"{by_name[key]} and {path} both map to {path.name}"
            )
        by_name[key] = path


def require_git() -> None:
    if shutil.which("git") is None:
        raise SystemExit("Git was not found on PATH")


def run_git(
    args: list[str],
    *,
    cwd: Path | None = None,
    env: dict[str, str] | None = None,
    capture_output: bool = False,
) -> subprocess.CompletedProcess[str]:
    completed = subprocess.run(
        ["git", *args],
        cwd=cwd,
        env=env,
        text=True,
        encoding="utf-8",
        capture_output=capture_output,
        check=False,
    )
    if completed.returncode == 0:
        return completed
    if capture_output:
        if completed.stdout:
            print(completed.stdout, end="")
        if completed.stderr:
            print(completed.stderr, end="", file=sys.stderr)
    raise SystemExit(completed.returncode)


def default_branch(git_pull_url: str) -> str:
    completed = run_git(
        ["ls-remote", "--symref", git_pull_url, "HEAD"],
        capture_output=True,
    )
    prefix = "ref: refs/heads/"
    for line in completed.stdout.splitlines():
        if line.startswith(prefix) and line.endswith("\tHEAD"):
            return line[len(prefix) : -len("\tHEAD")]
    raise SystemExit("GitHub did not report the Gist's default branch")


def print_binary_plan(
    files: list[Path],
    description: str,
    public: bool,
) -> int:
    plan = {
        "dry_run": True,
        "mode": "isolated-binary-gist",
        "description": description,
        "public": public,
        "files": [str(path) for path in files],
        "workspace": str(Path.cwd() / ".agent-temp" / "gist-upload-*"),
        "persistent_git_changes": False,
        "operations": [
            "create a placeholder Gist through the GitHub API",
            "copy files into an isolated temporary repository",
            "push directly to the Gist URL with one-shot credentials",
            "remove the temporary repository",
        ],
    }
    print(json.dumps(plan, indent=2, ensure_ascii=False))
    return 0


def create_binary_gist(
    files: list[Path],
    description: str,
    public: bool,
    *,
    dry_run: bool,
) -> int:
    validate_flat_filenames(files)
    if dry_run:
        return print_binary_plan(files, description, public)

    require_gh()
    require_git()
    authentication = gh_auth_environment()
    created = gh_api_json(
        "/gists",
        method="POST",
        payload={
            "description": description,
            "public": public,
            "files": {
                PLACEHOLDER_FILENAME: {
                    "content": "Binary files are being added through the Gist Git repository.\n"
                }
            },
        },
        environment=authentication,
    )
    if not isinstance(created, dict):
        raise SystemExit("GitHub returned no Gist metadata")

    gist_id = created.get("id")
    html_url = created.get("html_url")
    git_pull_url = created.get("git_pull_url")
    git_push_url = created.get("git_push_url")
    if not all(
        isinstance(value, str) and value
        for value in (gist_id, html_url, git_pull_url, git_push_url)
    ):
        raise SystemExit("GitHub returned incomplete Gist Git metadata")

    workflow_root = Path.cwd() / ".agent-temp"
    workflow_root.mkdir(parents=True, exist_ok=True)
    try:
        with tempfile.TemporaryDirectory(prefix="gist-upload-", dir=workflow_root) as temporary:
            repository = Path(temporary) / "repository"
            repository.mkdir()
            branch = default_branch(git_pull_url)
            run_git(["init", f"--initial-branch={branch}"], cwd=repository)
            run_git(
                ["fetch", "--depth=1", git_pull_url, f"refs/heads/{branch}"],
                cwd=repository,
            )
            run_git(["checkout", "-B", branch, "FETCH_HEAD"], cwd=repository)

            placeholder = repository / PLACEHOLDER_FILENAME
            if placeholder.exists():
                placeholder.unlink()
            for source in files:
                shutil.copy2(source, repository / source.name)

            run_git(["add", "--all"], cwd=repository)
            run_git(
                [
                    "-c",
                    f"user.name={GIST_COMMIT_NAME}",
                    "-c",
                    f"user.email={GIST_COMMIT_EMAIL}",
                    "commit",
                    "-m",
                    GIST_COMMIT_MESSAGE,
                ],
                cwd=repository,
            )
            run_git(
                [
                    "-c",
                    "credential.helper=",
                    "-c",
                    f"credential.helper={ONE_SHOT_CREDENTIAL_HELPER}",
                    "push",
                    git_push_url,
                    f"HEAD:refs/heads/{branch}",
                ],
                cwd=repository,
                env=authentication,
            )
    except SystemExit:
        print(
            f"Binary upload failed after creating {html_url}; the Gist was left intact for inspection.",
            file=sys.stderr,
        )
        raise

    final = gh_api_json(f"/gists/{gist_id}", environment=authentication)
    final_files = final.get("files", {}) if isinstance(final, dict) else {}
    output = {
        "id": gist_id,
        "html_url": html_url,
        "files": {
            filename: metadata.get("raw_url")
            for filename, metadata in final_files.items()
            if isinstance(metadata, dict)
        },
    }
    print(json.dumps(output, indent=2, ensure_ascii=False))
    return 0


def main() -> int:
    args = build_parser().parse_args()

    if args.command == "create":
        files = [Path(path) for path in args.file]
        missing = [str(path) for path in files if not path.is_file()]
        if missing:
            raise SystemExit(f"Missing gist file(s): {', '.join(missing)}")

        if any(is_binary_file(path) for path in files):
            return create_binary_gist(
                files,
                args.description,
                args.public,
                dry_run=args.dry_run,
            )

        command = ["gh", "gist", "create", *(str(path) for path in files)]
        if args.description:
            command.extend(["--desc", args.description])
        if args.public:
            command.append("--public")
        return gh_command(command, dry_run=args.dry_run)

    raise AssertionError(args.command)


if __name__ == "__main__":
    raise SystemExit(main())
