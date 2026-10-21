namespace RuleScript.Core.Lexer;

public sealed record Token(
    TokenType Type,
    string Lexeme,
    object? Literal,
    int Line,
    int Column)
{
    /// <summary>
    /// Gets the 1-based line containing the last character of the token, accounting for multi-line lexemes.
    /// </summary>
    public int EndLine => Line + Lexeme.Count(character => character == '\n');

    /// <summary>
    /// Gets the exclusive 1-based end column on <see cref="EndLine"/>.
    /// </summary>
    public int EndColumn
    {
        get
        {
            var lastNewLine = Lexeme.LastIndexOf('\n');
            return lastNewLine < 0
                ? Column + Lexeme.Length
                : Lexeme.Length - lastNewLine;
        }
    }
}
