from __future__ import annotations

import argparse
from pathlib import Path

from gh_common import add_dry_run_argument, gh_command


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Create GitHub gists with gh while keeping arguments predictable.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    create = subparsers.add_parser("create", help="Create a secret gist by default.")
    create.add_argument("--description", default="", help="Gist description.")
    create.add_argument("--file", action="append", required=True, help="File to upload; may be repeated.")
    create.add_argument("--public", action="store_true", help="Create a public gist. Default is secret.")
    add_dry_run_argument(create)

    return parser


def main() -> int:
    args = build_parser().parse_args()

    if args.command == "create":
        files = [str(Path(path)) for path in args.file]
        missing = [path for path in files if not Path(path).is_file()]
        if missing:
            raise SystemExit(f"Missing gist file(s): {', '.join(missing)}")

        command = ["gh", "gist", "create", *files]
        if args.description:
            command.extend(["--desc", args.description])
        if args.public:
            command.append("--public")
        return gh_command(command, dry_run=args.dry_run)

    raise AssertionError(args.command)


if __name__ == "__main__":
    raise SystemExit(main())
