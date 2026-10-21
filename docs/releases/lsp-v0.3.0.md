# RuleScript Language Server v0.3.0

RuleScript Language Server v0.3.0 is built on `RuleScript.Core` 1.15.0 and supports its new syntax: conditional expressions (`a ? b : c`), compound assignment (`+=`, `-=`, `*=`, `/=`, `%=`), template strings (`` `Lot ${id}` ``), and the `Range` built-in.

Diagnostics, hover, definition, references, rename, completion, and signature help work inside template string substitutions. Template text is treated as a string, so completion is not offered there. Formatting spaces the new operators and keeps template text unchanged.

There are no protocol or configuration changes.
