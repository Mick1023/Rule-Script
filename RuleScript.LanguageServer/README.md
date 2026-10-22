# RuleScript Language Server

A [Language Server Protocol](https://microsoft.github.io/language-server-protocol/) server for RuleScript `.rules` files, built on `RuleScript.Core` editor APIs.

## Features

| LSP request | Behavior |
|---|---|
| `textDocument/publishDiagnostics` | Syntax and semantic diagnostics, refreshed on every change. Incomplete documents are analyzed tolerantly. |
| `textDocument/definition` | Functions, variables, parameters, and exported functions in imported files. Calls to overloaded functions go to the overload that matches the arguments. |
| `textDocument/references` | All usages of the symbol under the cursor. For an overloaded function, only the usages of that overload. |
| `textDocument/hover` | Signature with parameter and return types, plus `///` documentation. Calls to overloaded functions show the overload that matches the arguments. |
| `textDocument/completion` | In-scope variables and parameters, functions, import aliases, keywords, and block snippets; import members after `alias.`; `@HostTrigger` after `@`. Nothing is offered inside strings, comments, or where a new name is being declared. |
| `textDocument/signatureHelp` | Parameter hints for the call around the cursor, triggered by `(` and `,`, including nested calls and `alias.Function(...)`. |
| `textDocument/rename`, `textDocument/prepareRename` | Renames variables, parameters, loop variables, and non-exported functions. Renaming an overloaded function renames only that overload. Built-in and Host Functions, exported symbols, and symbols declared in imported files are rejected with a reason, as are invalid names, names already in use, and renames that would change what references resolve to. |
| `textDocument/documentSymbol` | Outline of imports, functions (with locals), variables, and constants. |
| `textDocument/foldingRange` | `#region` blocks, multi-line statements and blocks, and comments. |
| `textDocument/formatting` | `RuleScriptFormatter`. Documents with syntax errors are left unchanged. |

Documents are synchronized in full on each change. Relative imports resolve from the document's own file location and read unsaved edits from open editor buffers. Each workspace folder gets its own engine with that folder as `WorkingDirectory`; a document belongs to the innermost folder that contains it.

## Host Metadata (`rulescript.json`)

Host applications register Host Functions and context variables at runtime, so the server cannot see them. Declare them in a `rulescript.json` at the root of a workspace folder to get hover, completion, and signature help for them, and to stop them from being reported as undefined:

```jsonc
{
  "hostFunctions": [
    {
      "name": "GetDistance",
      "parameters": [{ "name": "sensorId", "type": "string" }],
      "returnType": "number",
      "documentation": "Reads a distance sensor."
    }
  ],
  "variables": [
    { "name": "Distance", "type": "number" }
  ]
}
```

Types are `any`, `null`, `number`, `string`, `boolean` (or `bool`), `array`, `object`, and `void`. Comments and trailing commas are allowed. Repeat a name with different parameters to declare overloads. Missing or unknown Host Function types fall back to `any`, and problems are reported in the client's output log. The server reloads the file when it changes, if the client supports file watching.

The manifest only describes metadata for analysis; the server never runs Host Functions.

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

- Diagnostics of documents that import an edited file refresh when that file is saved. Hover, completion, and navigation see the unsaved edits immediately.
- Rename does not yet update other files, so exported symbols cannot be renamed.
- Workspace folders added after startup are not picked up until the server restarts.
- Snippets are offered only to clients that declare snippet support.
