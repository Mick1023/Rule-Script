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
