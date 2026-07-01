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
    public const string DuplicateCase = "RS2006";
    public const string MissingDefaultBranch = "RS2007";
    public const string PropertyNotFound = "RS2008";
    public const string InvalidAssignment = "RS2009";
    public const string CannotAssignToReadonly = "RS2010";
    public const string IndexTypeError = "RS2011";
    public const string NullAccess = "RS2012";
    public const string InvalidNullCoalescing = "RS2013";
    public const string DuplicateObjectProperty = "RS2014";
}
