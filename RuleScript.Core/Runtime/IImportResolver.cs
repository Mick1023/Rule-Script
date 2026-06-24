namespace RuleScript.Core.Runtime;

/// <summary>
/// Resolves and reads RuleScript project files used by <see cref="RuleScriptEngine.ExecuteFile(string)"/> and import statements.
/// </summary>
public interface IImportResolver
{
    /// <summary>
    /// Converts a script path to a normalized full path.
    /// </summary>
    string GetFullPath(string path);

    /// <summary>
    /// Returns whether a script file exists at the resolved path.
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// Reads the complete script text from the resolved path.
    /// </summary>
    string ReadAllText(string path);
}
