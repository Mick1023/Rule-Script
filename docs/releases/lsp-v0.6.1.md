# RuleScript Language Server v0.6.1

RuleScript Language Server v0.6.1 is built on `RuleScript.Core` 2.1.1, which makes editor navigation pick the overload a call actually uses. This applies to built-in functions, user functions, and Host Functions declared in `rulescript.json`.

## Overload-Aware Navigation

- `textDocument/hover` and `textDocument/definition` use the overload that matches the call's argument count and argument types. Before, they always used the last overload with that name. For example, hovering `PadLeft("5", 2)` showed the three-parameter `PadLeft`.
- On an overload's own declaration, hover and go to definition show that declaration.
- `textDocument/references` on an overloaded function returns only the usages of that overload.
- `textDocument/rename` uses the same references, so renaming one overload renames only that overload.

```rulescript
var a = Describe(1);     // host function Describe(value: number) -> string
var b = Describe("x");   // host function Describe(value: string) -> string
var c = Describe(1, 2);  // host function Describe(value: number, digits: number) -> string
```

There are no protocol changes.
