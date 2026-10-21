using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Core;

internal static class RuleScriptRenameAnalyzer
{
    public static RuleScriptPrepareRenameResult PrepareRename(RuleScriptDocumentAnalysisResult document, int line, int column)
    {
        var (references, rejectReason) = FindRenameableReferences(document, line, column);
        if (rejectReason is not null)
        {
            return new RuleScriptPrepareRenameResult(false, rejectReason, null, null);
        }

        var current = references.First(reference => RuleScriptSourceMapper.Contains(reference.Range, line, column));
        return new RuleScriptPrepareRenameResult(true, null, current.Name, current.Range);
    }

    public static RuleScriptRenameResult Rename(RuleScriptDocumentAnalysisResult document, int line, int column, string newName)
    {
        var (references, rejectReason) = FindRenameableReferences(document, line, column);
        if (rejectReason is not null)
        {
            return Reject(rejectReason, newName);
        }

        var oldName = references[0].Name;
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return new RuleScriptRenameResult(true, null, [], newName);
        }

        if (!Lexer.Lexer.IsIdentifier(newName) || newName == "global")
        {
            return Reject($"'{newName}' is not a valid RuleScript identifier.", newName);
        }

        if (IsNameTaken(document, newName))
        {
            return Reject($"A symbol named '{newName}' already exists.", newName);
        }

        var edits = references.Select(reference => reference.Range!).ToArray();

        // Re-analyze the renamed text: the new name must reach exactly the same occurrences,
        // otherwise the rename captured or was captured by another symbol.
        var renamed = RuleScriptLanguageService.Reanalyze(document, ApplyEdits(document.Source, edits, newName));
        var first = edits.OrderBy(edit => edit.StartLine).ThenBy(edit => edit.StartColumn).First();
        var renamedReferences = RuleScriptNavigationAnalyzer.FindReferences(renamed, first.StartLine, first.StartColumn);
        if (renamedReferences.Count != edits.Length)
        {
            return Reject("Renaming would change which declarations the references point to.", newName);
        }

        return new RuleScriptRenameResult(true, null, edits, newName);
    }

    private static (IReadOnlyList<RuleScriptReferenceInfo> References, string? RejectReason) FindRenameableReferences(
        RuleScriptDocumentAnalysisResult document,
        int line,
        int column)
    {
        var references = RuleScriptNavigationAnalyzer.FindReferences(document, line, column);
        if (references.Any(reference => reference.IsExternal || reference.Kind == RuleScriptSymbolKind.HostFunction))
        {
            return (references, "Built-in and Host Functions cannot be renamed.");
        }

        if (references.Count == 0 || references.Any(reference => reference.Range is null))
        {
            return (references, "There is no renameable symbol at this position.");
        }

        var symbol = references[0];

        if (symbol.Name.Contains('.')
            || references.Any(reference => reference.Range!.File is not null)
            || IsExported(document.Statements, symbol))
        {
            return (references, "Exported symbols and symbols declared in imported files cannot be renamed, because other files may use them.");
        }

        return (references, null);
    }

    private static bool IsExported(IEnumerable<Statement> statements, RuleScriptReferenceInfo symbol)
    {
        return symbol.Kind switch
        {
            RuleScriptSymbolKind.Function => statements.OfType<FunctionDeclarationStatement>()
                .Any(function => function.IsExported && function.Name == symbol.Name),
            RuleScriptSymbolKind.Variable => statements.OfType<ConstStatement>()
                .Any(constant => constant.IsExported && constant.Name == symbol.Name),
            _ => false
        };
    }

    private static bool IsNameTaken(RuleScriptDocumentAnalysisResult document, string name)
    {
        return document.Analysis.VariableNames.Contains(name, StringComparer.Ordinal)
            || document.Analysis.Functions.Any(function => string.Equals(function.Name, name, StringComparison.Ordinal))
            || document.Analysis.ImportAliases.Contains(name, StringComparer.Ordinal)
            || ContainsParameter(document.Statements, name);
    }

    private static bool ContainsParameter(IEnumerable<Statement> statements, string name)
    {
        return statements.OfType<FunctionDeclarationStatement>()
            .Any(function => function.ParameterDefinitions.Any(parameter => parameter.Name == name));
    }

    private static string ApplyEdits(string source, IEnumerable<RuleScriptSourceRange> edits, string newName)
    {
        var text = source;

        // Apply from the end so earlier offsets stay valid.
        foreach (var edit in edits.OrderByDescending(edit => edit.StartLine).ThenByDescending(edit => edit.StartColumn))
        {
            var start = RuleScriptCompletionAnalyzer.ToOffset(text, edit.StartLine, edit.StartColumn);
            var end = RuleScriptCompletionAnalyzer.ToOffset(text, edit.EndLine, edit.EndColumn);
            text = string.Concat(text.AsSpan(0, start), newName, text.AsSpan(end));
        }

        return text;
    }

    private static RuleScriptRenameResult Reject(string message, string newName)
    {
        return new RuleScriptRenameResult(false, message, [], newName);
    }
}
