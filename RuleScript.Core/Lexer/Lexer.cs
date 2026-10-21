using RuleScript.Core.Diagnostics;

namespace RuleScript.Core.Lexer;

public sealed class Lexer
{
    private readonly string _source;
    private readonly List<Token> _tokens = [];
    private int _start;
    private int _current;
    private int _line = 1;
    private int _column = 1;
    private int _tokenLine = 1;
    private int _tokenColumn = 1;
    private bool _includeComments;
    private List<SyntaxException>? _diagnostics;

    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        ["var"] = TokenType.Var,
        ["const"] = TokenType.Const,
        ["export"] = TokenType.Export,
        ["if"] = TokenType.If,
        ["then"] = TokenType.Then,
        ["else"] = TokenType.Else,
        ["elseif"] = TokenType.ElseIf,
        ["end"] = TokenType.End,
        ["endif"] = TokenType.EndIf,
        ["while"] = TokenType.While,
        ["endwhile"] = TokenType.EndWhile,
        ["foreach"] = TokenType.Foreach,
        ["in"] = TokenType.In,
        ["endforeach"] = TokenType.EndForeach,
        ["function"] = TokenType.Function,
        ["endfunction"] = TokenType.EndFunction,
        ["return"] = TokenType.Return,
        ["import"] = TokenType.Import,
        ["as"] = TokenType.As,
        ["break"] = TokenType.Break,
        ["continue"] = TokenType.Continue,
        ["switch"] = TokenType.Switch,
        ["case"] = TokenType.Case,
        ["default"] = TokenType.Default,
        ["when"] = TokenType.When,
        ["endswitch"] = TokenType.EndSwitch,
        ["parallel"] = TokenType.Parallel,
        ["task"] = TokenType.Task,
        ["trigger"] = TokenType.Trigger,
        ["endtask"] = TokenType.EndTask,
        ["endparallel"] = TokenType.EndParallel,
        ["and"] = TokenType.And,
        ["or"] = TokenType.Or,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["null"] = TokenType.Null
    };

    /// <summary>
    /// Gets whether the text is a reserved RuleScript keyword.
    /// </summary>
    internal static bool IsKeyword(string text) => Keywords.ContainsKey(text);

    /// <summary>
    /// Gets whether the text is a valid identifier that is not a reserved keyword.
    /// </summary>
    internal static bool IsIdentifier(string text)
    {
        return text.Length > 0
            && IsIdentifierStart(text[0])
            && text.All(IsIdentifierPart)
            && !IsKeyword(text);
    }

    public Lexer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public IReadOnlyList<Token> Tokenize() => Tokenize(includeComments: false);

    /// <summary>
    /// Tokenizes as much of the source as possible and collects recoverable lexical errors instead of throwing.
    /// </summary>
    internal IReadOnlyList<Token> TokenizeWithDiagnostics(List<SyntaxException> diagnostics, bool includeComments = false)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        _diagnostics = diagnostics;
        return Tokenize(includeComments);
    }

    internal IReadOnlyList<Token> Tokenize(bool includeComments)
    {
        _includeComments = includeComments;

        while (!IsAtEnd())
        {
            _start = _current;
            _tokenLine = _line;
            _tokenColumn = _column;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.EndOfFile, string.Empty, null, _line, _column));
        return _tokens;
    }

    private void ScanToken()
    {
        var c = Advance();

        switch (c)
        {
            case '(':
                AddToken(TokenType.LeftParen);
                break;
            case ')':
                AddToken(TokenType.RightParen);
                break;
            case '[':
                AddToken(TokenType.LeftBracket);
                break;
            case ']':
                AddToken(TokenType.RightBracket);
                break;
            case '{':
                AddToken(TokenType.LeftBrace);
                break;
            case '}':
                AddToken(TokenType.RightBrace);
                break;
            case '.':
                AddToken(TokenType.Dot);
                break;
            case ',':
                AddToken(TokenType.Comma);
                break;
            case ';':
                AddToken(TokenType.Semicolon);
                break;
            case ':':
                AddToken(TokenType.Colon);
                break;
            case '@':
                AddToken(TokenType.At);
                break;
            case '+':
                AddToken(TokenType.Plus);
                break;
            case '-':
                AddToken(Match('>') ? TokenType.Arrow : TokenType.Minus);
                break;
            case '*':
                AddToken(TokenType.Star);
                break;
            case '%':
                AddToken(TokenType.Percent);
                break;
            case '!':
                AddToken(Match('=') ? TokenType.BangEqual : TokenType.Bang);
                break;
            case '=':
                AddToken(Match('=') ? TokenType.EqualEqual : TokenType.Assign);
                break;
            case '<':
                AddToken(Match('=') ? TokenType.LessOrEqual : TokenType.Less);
                break;
            case '>':
                AddToken(Match('=') ? TokenType.GreaterOrEqual : TokenType.Greater);
                break;
            case '?':
                if (Match('?'))
                {
                    AddToken(TokenType.QuestionQuestion);
                }
                else if (Match('.'))
                {
                    AddToken(TokenType.QuestionDot);
                }
                else
                {
                    Report(new SyntaxException("Expected '?' or '.' after '?'.", _line, _tokenColumn, "?"));
                }

                break;
            case '#':
                ScanRegionDirective();
                break;
            case '/':
                if (Match('/'))
                {
                    if (Match('/'))
                    {
                        ScanDocumentationComment();
                    }
                    else
                    {
                        ScanLineComment();
                    }
                }
                else if (Match('*'))
                {
                    ScanBlockComment();
                }
                else
                {
                    AddToken(TokenType.Slash);
                }
                break;
            case '"':
                ScanString();
                break;
            case ' ':
            case '\r':
            case '\t':
                break;
            case '\n':
                NewLine();
                break;
            default:
                if (char.IsDigit(c))
                {
                    ScanNumber();
                }
                else if (IsIdentifierStart(c))
                {
                    ScanIdentifier();
                }
                else
                {
                    Report(new SyntaxException($"Unsupported character '{c}'.", _line, _tokenColumn, c.ToString()));
                }

                break;
        }
    }

    private void ScanString()
    {
        var value = new System.Text.StringBuilder();

        while (!IsAtEnd() && Peek() != '"')
        {
            if (Peek() == '\n')
            {
                ReportUnterminatedString(value);
                return;
            }

            if (Peek() == '\\')
            {
                Advance();

                if (IsAtEnd())
                {
                    ReportUnterminatedString(value);
                    return;
                }

                value.Append(AdvanceEscapedCharacter());
                continue;
            }

            value.Append(Advance());
        }

        if (IsAtEnd())
        {
            ReportUnterminatedString(value);
            return;
        }

        Advance();

        AddToken(TokenType.String, value.ToString());
    }

    private void ReportUnterminatedString(System.Text.StringBuilder value)
    {
        Report(new SyntaxException("Unterminated string literal.", _line, _tokenColumn, "\""));

        // Recovery keeps the partial literal so the parser can continue with the rest of the line.
        AddToken(TokenType.String, value.ToString());
    }

    private char AdvanceEscapedCharacter()
    {
        var escaped = Advance();

        switch (escaped)
        {
            case '"':
                return '"';
            case '\\':
                return '\\';
            case 'n':
                return '\n';
            case 'r':
                return '\r';
            case 't':
                return '\t';
            default:
                Report(new SyntaxException($"Unsupported string escape '\\{escaped}'.", _line, _column - 1, escaped.ToString()));
                return escaped;
        }
    }

    private void ScanNumber()
    {
        while (char.IsDigit(Peek()))
        {
            Advance();
        }

        if (Peek() == '.' && char.IsDigit(PeekNext()))
        {
            Advance();

            while (char.IsDigit(Peek()))
            {
                Advance();
            }
        }

        var text = _source[_start.._current];
        AddToken(TokenType.Number, double.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
    }

    private void ScanIdentifier()
    {
        while (IsIdentifierPart(Peek()))
        {
            Advance();
        }

        var text = _source[_start.._current];
        AddToken(Keywords.GetValueOrDefault(text, TokenType.Identifier));
    }

    private void ScanLineComment()
    {
        while (!IsAtEnd() && Peek() is not '\r' and not '\n')
        {
            Advance();
        }

        if (_includeComments)
        {
            AddToken(TokenType.LineComment);
        }
    }

    private void ScanDocumentationComment()
    {
        while (!IsAtEnd() && Peek() is not '\r' and not '\n')
        {
            Advance();
        }

        var content = _source[(_start + 3).._current];
        if (content.StartsWith(' '))
        {
            content = content[1..];
        }

        AddToken(TokenType.DocumentationComment, content);
    }

    private void ScanRegionDirective()
    {
        while (!IsAtEnd() && Peek() is not '\r' and not '\n')
        {
            Advance();
        }

        var directive = _source[_start.._current];
        if (TryGetDirectiveValue(directive, "#region", out var name))
        {
            AddToken(TokenType.RegionStart, name);
            return;
        }

        if (TryGetDirectiveValue(directive, "#endregion", out _))
        {
            AddToken(TokenType.RegionEnd);
            return;
        }

        Report(new SyntaxException($"Unsupported directive '{directive}'.", _line, _tokenColumn, directive));
    }

    private static bool TryGetDirectiveValue(string directive, string keyword, out string value)
    {
        if (!directive.StartsWith(keyword, StringComparison.Ordinal)
            || (directive.Length > keyword.Length && !char.IsWhiteSpace(directive[keyword.Length])))
        {
            value = string.Empty;
            return false;
        }

        value = directive[keyword.Length..].Trim();
        return true;
    }

    private void ScanBlockComment()
    {
        while (!IsAtEnd())
        {
            if (Peek() == '*' && PeekNext() == '/')
            {
                Advance();
                Advance();
                if (_includeComments)
                {
                    AddToken(TokenType.MultiLineComment);
                }

                return;
            }

            if (Peek() == '\n')
            {
                Advance();
                NewLine();
            }
            else
            {
                Advance();
            }
        }

        Report(new SyntaxException("Unterminated multi-line comment.", _tokenLine, _tokenColumn, "/*"));

        if (_includeComments)
        {
            AddToken(TokenType.MultiLineComment);
        }
    }

    private bool Match(char expected)
    {
        if (IsAtEnd() || _source[_current] != expected)
        {
            return false;
        }

        _current++;
        _column++;
        return true;
    }

    private char Advance()
    {
        _column++;
        return _source[_current++];
    }

    private char Peek() => IsAtEnd() ? '\0' : _source[_current];

    private char PeekNext() => _current + 1 >= _source.Length ? '\0' : _source[_current + 1];

    private bool IsAtEnd() => _current >= _source.Length;

    private void AddToken(TokenType type, object? literal = null)
    {
        var text = _source[_start.._current];
        _tokens.Add(new Token(type, text, literal, _tokenLine, _tokenColumn));
    }

    /// <summary>
    /// Throws in strict mode, or records the error and lets the caller skip the offending input in recovery mode.
    /// </summary>
    private void Report(SyntaxException exception)
    {
        if (_diagnostics is null)
        {
            throw exception;
        }

        _diagnostics.Add(exception);
    }

    private void NewLine()
    {
        _line++;
        _column = 1;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || char.IsDigit(c);
}
