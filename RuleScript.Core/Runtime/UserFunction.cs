using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Runtime;

internal sealed record UserFunction(FunctionDeclarationStatement Declaration, ScriptModule Module);
