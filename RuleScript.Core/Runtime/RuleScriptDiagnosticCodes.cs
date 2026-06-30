namespace RuleScript.Core.Runtime;

/// <summary>
/// Stable diagnostic codes produced by RuleScript analysis.
/// </summary>
public static class RuleScriptDiagnosticCodes
{
    public const string SyntaxError = "RS1000";
    public const string UndefinedVariable = "RS2001";
    public const string UndefinedFunction = "RS2002";
    public const string TypeMismatch = "RS2003";
    public const string DuplicateDeclaration = "RS2004";
    public const string DuplicateParameter = "RS2005";
}
