using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RuleScript.LanguageServer.Handlers;

internal static class RuleScriptDocumentSelector
{
    public static TextDocumentSelector Instance { get; } = new(
        new TextDocumentFilter { Language = RuleScriptLanguageFeatures.LanguageId },
        new TextDocumentFilter { Pattern = "**/*.rules" });
}
