# RuleScript Language Server

A [Language Server Protocol](https://microsoft.github.io/language-server-protocol/) server for RuleScript `.rules` files, built on `RuleScript.Core` editor APIs.

## Features

| LSP request | Behavior |
|---|---|
| `textDocument/publishDiagnostics` | Syntax and semantic diagnostics, refreshed on every change. Incomplete documents are analyzed tolerantly. |
| `textDocument/definition` | Functions, variables, parameters, and exported functions in imported files. |
| `textDocument/references` | All usages of the symbol under the cursor. |
| `textDocument/hover` | Signature with parameter and return types, plus `///` documentation. |
| `textDocument/completion` | In-scope variables and parameters, functions, import aliases, keywords, and block snippets; import members after `alias.`; `@HostTrigger` after `@`. Nothing is offered inside strings, comments, or where a new name is being declared. |
| `textDocument/signatureHelp` | Parameter hints for the call around the cursor, triggered by `(` and `,`, including nested calls and `alias.Function(...)`. |
| `textDocument/documentSymbol` | Outline of imports, functions (with locals), variables, and constants. |
| `textDocument/foldingRange` | `#region` blocks, multi-line statements and blocks, and comments. |
| `textDocument/formatting` | `RuleScriptFormatter`. Documents with syntax errors are left unchanged. |

Documents are synchronized in full on each change. Relative imports resolve from the document's own file location, and the first workspace folder becomes the engine `WorkingDirectory`.

## Install

```bash
dotnet tool install --global RuleScript.LanguageServer
```

To build and install from source instead:

```bash
dotnet pack RuleScript.LanguageServer -c Release -o ./artifacts
dotnet tool install --global --add-source ./artifacts RuleScript.LanguageServer
```

The server communicates over stdio:

```bash
rulescript-lsp
```

## Editor Setup

### Neovim (0.11+)

```lua
vim.filetype.add({ extension = { rules = "rulescript" } })

vim.lsp.config("rulescript", {
  cmd = { "rulescript-lsp" },
  filetypes = { "rulescript" },
  root_markers = { ".git" },
})
vim.lsp.enable("rulescript")
```

### Other Editors

Any LSP client can launch `rulescript-lsp` over stdio for files with the `.rules` extension and the `rulescript` language id.

## Limitations

- Host Functions registered by your application are unknown to the server, so calls to them may be reported as undefined and are missing from completion.
- Snippets are offered only to clients that declare snippet support.
