# RuleScript Language Server v0.6.0

RuleScript Language Server v0.6.0 is built on `RuleScript.Core` 2.1.0, which supports Host Function overloads.

## Host Function Overloads in `rulescript.json`

Entries in `hostFunctions` that repeat a name with different parameters now declare overloads. Previously the last entry replaced the earlier ones.

```jsonc
{
  "hostFunctions": [
    {
      "name": "AddNumber",
      "parameters": [{ "name": "a", "type": "number" }, { "name": "b", "type": "number" }],
      "returnType": "number"
    },
    {
      "name": "AddNumber",
      "parameters": [{ "name": "a", "type": "number" }, { "name": "b", "type": "number" }, { "name": "c", "type": "number" }],
      "returnType": "number"
    }
  ]
}
```

Diagnostics accept calls that match any overload. Signature help lists every overload, and hover and completion include each one. Entries with the same name and the same parameter types still replace each other.

There are no protocol changes.
