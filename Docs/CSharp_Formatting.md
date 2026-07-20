# C# Formatting

This repository uses [CSharpier](https://csharpier.com/) as its deterministic C# formatter and
formatting linter. CSharpier is an opinionated formatter inspired by Prettier. It parses and
reprints C# without requiring Unity-generated project or solution files, which keeps the same
workflow usable from Unity, Visual Studio, Rider, command-line development, and CI.

The exact CSharpier version is pinned in `.config/dotnet-tools.json`. Formatting options shared by
CSharpier and supported editors live in `.editorconfig`. `.csharpierignore` limits the rollout to
C# so formatting commands do not rewrite Unity or MSBuild XML.

## Initial Setup

Install the .NET 8 SDK, then restore the repository's pinned tools from the checkout root:

```powershell
dotnet tool restore
```

To enable automatic formatting of staged C# files, install
[pre-commit](https://pre-commit.com/) and its repository hook:

```powershell
pre-commit install
```

The hook restores pinned .NET tools when necessary and formats only staged C# files. If formatting
changes a file, the commit stops so the result can be reviewed and staged before committing again.

## Format And Check

Apply formatting automatically to every non-generated C# file:

```powershell
dotnet csharpier format .
```

Check formatting without changing files:

```powershell
dotnet csharpier check .
```

The check exits nonzero and reports unformatted files, making it suitable for local validation and
CI. Generated C# identified by CSharpier's standard generated-file markers remains untouched.

The `C# Formatting` GitHub Actions workflow restores the same pinned tool and runs the full check
for every pull request and push targeting a maintained branch.
