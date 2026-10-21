# RuleScript Language Server v0.2.0

RuleScript Language Server v0.2.0 adds rename, host metadata from `rulescript.json`, unsaved import buffers, and multi-root workspaces. It is built on `RuleScript.Core` 1.14.0.

## Rename

`textDocument/rename` and `textDocument/prepareRename` rename variables, parameters, loop variables, and non-exported functions. Rejected renames return an LSP `RequestFailed` error whose message explains why, for example `Built-in and Host Functions cannot be renamed.`

## Host Metadata (`rulescript.json`)

Declare the Host Functions and context variables your application registers at runtime in a `rulescript.json` at the root of a workspace folder:

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
  "variables": [{ "name": "Distance", "type": "number" }]
}
```

Declared symbols get hover, completion, and signature help, and are no longer reported as undefined. Invalid entries are reported in the client log and skipped without stopping the server. The file is reloaded when it changes, if the client supports file watching.

## Unsaved Import Buffers

Imports resolve from open editor buffers before the file system, so hover, completion, and navigation in an importing document see unsaved edits in the imported file. Diagnostics of open documents refresh when any document is saved.

## Multi-Root Workspaces

Each workspace folder gets its own engine, `WorkingDirectory`, and `rulescript.json`. A document belongs to the innermost folder that contains it. Folders added after startup require a server restart.

## Validation

The language server test suite contains 24 tests, including end-to-end JSON-RPC coverage of rename and its rejection message.
