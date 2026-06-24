namespace RuleScript.Core.Runtime;

/// <summary>
/// Default import resolver backed by the local file system.
/// </summary>
public sealed class FileSystemImportResolver : IImportResolver
{
    /// <inheritdoc />
    public string GetFullPath(string path)
    {
        return Path.GetFullPath(path);
    }

    /// <inheritdoc />
    public bool Exists(string path)
    {
        return File.Exists(path);
    }

    /// <inheritdoc />
    public string ReadAllText(string path)
    {
        return File.ReadAllText(path);
    }
}
