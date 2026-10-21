# Contributing

Thank you for helping improve RuleScript.

## Branch Strategy

- Use `main` for stable code.
- Create short-lived feature branches from `main`.
- Prefer branch names like `feature/parser-diagnostics`, `fix/runtime-error-message`, or `docs/readme-update`.

## Pull Requests

- Keep pull requests focused on one change.
- Include a short summary of the behavior changed.
- Link related issues when available.
- Call out any compatibility or public API impact.

## Coding Style

- Target .NET 8.
- Use nullable reference types.
- Use file-scoped namespaces.
- Use `var` when the assigned type is obvious.
- Keep implementations simple and avoid adding abstractions before they are needed.

## Test Requirements

Before opening a pull request, run:

```powershell
dotnet build RuleScript.sln
dotnet test RuleScript.sln
```

New parser, runtime, built-in function, host function, or diagnostics behavior should include focused tests.

## Releasing

Releases are published automatically when a version tag is pushed to the merge commit on `main`. Each workflow builds, runs the full test suite, publishes to NuGet.org, and creates a GitHub Release from the matching notes file.

| Package | Version source | Release notes | Tag | Workflow |
|---|---|---|---|---|
| `RuleScript.Core` | `RuleScript.Core/RuleScript.Core.csproj` | `docs/releases/vX.Y.Z.md` | `vX.Y.Z` | `package.yml` |
| `RuleScript.LanguageServer` | `RuleScript.LanguageServer/RuleScript.LanguageServer.csproj` | `docs/releases/lsp-vX.Y.Z.md` | `lsp-vX.Y.Z` | `package-language-server.yml` |

Update the project version and add the release notes in the pull request, then tag the merge commit:

```bash
git tag v1.13.0 origin/main
git push origin v1.13.0
```

The language server workflow fails if the tag does not match the project version. Language server releases are not marked as the latest GitHub release, so the latest release always points at `RuleScript.Core`.
