namespace RuleScript.LanguageServer;

/// <summary>
/// Signals a rename the core rejected; the message is returned to the client as the request error.
/// </summary>
internal sealed class RenameRejectedException : Exception
{
    public RenameRejectedException(string message)
        : base(message)
    {
    }
}
