# RuleScript Language Server v0.1.0

The first release of the RuleScript Language Server, a dotnet tool that serves RuleScript `.rules` files over the Language Server Protocol on stdio. It is built on `RuleScript.Core` 1.13.0.

## Install

```bash
dotnet tool install --global RuleScript.LanguageServer
rulescript-lsp
```

See the [language server README](../../RuleScript.LanguageServer/README.md) for editor setup.

## Features

- Diagnostics: syntax and semantic diagnostics on every change, with tolerant analysis of incomplete documents
- Definition and references: functions, variables, parameters, loop variables, and exported functions in imported files
- Hover: signatures with parameter and return types, plus `///` documentation
- Completion: in-scope variables and parameters, functions, import members after `alias.`, keywords, block snippets, and `@HostTrigger`
- Signature help: active parameter tracking across nested calls, triggered by `(` and `,`
- Document symbols: imports, functions with their locals, variables, and constants
- Folding ranges: `#region` blocks, multi-line statements and blocks, and comments
- Formatting: `RuleScriptFormatter`, skipped for documents with syntax errors

## Known Limitations

- Host Functions registered by host applications are unknown to the server, so calls to them may be reported as undefined.
- Imported files are read from disk, so unsaved edits in an open imported file are not visible to the importing document.
- Only the first workspace folder is used as the engine working directory.

## Validation

The language server test suite contains 15 tests, including an end-to-end JSON-RPC session over in-memory pipes.
