using RuleScript.Core.Lexer;
using RuleScript.Core.Runtime;

namespace RuleScript.Core;

internal static class RuleScriptSignatureHelpAnalyzer
{
    // Tokens that cannot appear inside call arguments, so the scan for an open call stops there.
    private static readonly HashSet<TokenType> StatementBoundaries =
    [
        TokenType.Semicolon,
        TokenType.Then,
        TokenType.Var,
        TokenType.Const,
        TokenType.Function,
        TokenType.If,
        TokenType.ElseIf,
        TokenType.Else,
        TokenType.While,
        TokenType.Foreach,
        TokenType.Switch,
        TokenType.Case,
        TokenType.Default,
        TokenType.Return,
        TokenType.EndIf,
        TokenType.EndWhile,
        TokenType.EndForeach,
        TokenType.EndFunction,
        TokenType.EndSwitch,
        TokenType.EndTask,
        TokenType.EndParallel
    ];

    public static RuleScriptSignatureHelp? GetSignatureHelp(RuleScriptDocumentAnalysisResult document, int line, int column)
    {
        var tokens = document.Tokens
            .Where(token => token.Type != TokenType.EndOfFile
                && RuleScriptCompletionAnalyzer.IsBefore(token.Line, token.Column, line, column, inclusive: false))
            .ToArray();

        var (openParen, activeParameter) = FindOpenCall(tokens);
        if (openParen <= 0 || tokens[openParen - 1].Type != TokenType.Identifier)
        {
            return null;
        }

        var name = tokens[openParen - 1].Lexeme;
        if (openParen >= 3 && tokens[openParen - 2].Type == TokenType.Dot && tokens[openParen - 3].Type == TokenType.Identifier)
        {
            name = $"{tokens[openParen - 3].Lexeme}.{name}";
        }

        var functions = document.Analysis.Functions
            .Where(function => string.Equals(function.Name, name, StringComparison.Ordinal))
            .ToArray();
        if (functions.Length == 0)
        {
            return null;
        }

        var signatures = functions.Select(CreateSignature).ToArray();
        var activeSignature = Array.FindIndex(functions, function => function.Parameters.Count > activeParameter);

        return new RuleScriptSignatureHelp(signatures, Math.Max(0, activeSignature), activeParameter);
    }

    /// <summary>
    /// Walks backwards to the innermost unclosed '(' and counts the top-level commas after it.
    /// </summary>
    private static (int OpenParen, int ActiveParameter) FindOpenCall(IReadOnlyList<Token> tokens)
    {
        var depth = 0;
        var commas = 0;

        for (var index = tokens.Count - 1; index >= 0; index--)
        {
            if (StatementBoundaries.Contains(tokens[index].Type))
            {
                return (-1, 0);
            }

            switch (tokens[index].Type)
            {
                case TokenType.RightParen:
                case TokenType.RightBracket:
                case TokenType.RightBrace:
                    depth++;
                    break;
                case TokenType.LeftBracket:
                case TokenType.LeftBrace:
                    if (depth == 0)
                    {
                        // The cursor sits inside an array or object literal argument; keep looking for the call.
                        commas = 0;
                    }
                    else
                    {
                        depth--;
                    }

                    break;
                case TokenType.LeftParen:
                    if (depth == 0)
                    {
                        return (index, commas);
                    }

                    depth--;
                    break;
                case TokenType.Comma when depth == 0:
                    commas++;
                    break;
            }
        }

        return (-1, 0);
    }

    private static RuleScriptSignatureInfo CreateSignature(RuleScriptFunctionSymbol function)
    {
        var (label, offsets) = RuleScriptSignatureFormatter.FormatSignature(function.Name, function.Parameters, function.ReturnType);
        var parameters = offsets
            .Select(offset => new RuleScriptParameterInfo(label[offset.Start..offset.End], offset.Start, offset.End))
            .ToArray();

        return new RuleScriptSignatureInfo(label, function.Documentation, parameters);
    }
}
