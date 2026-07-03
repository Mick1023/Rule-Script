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
    private int _tokenColumn = 1;

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
        ["endtask"] = TokenType.EndTask,
        ["endparallel"] = TokenType.EndParallel,
        ["and"] = TokenType.And,
        ["or"] = TokenType.Or,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["null"] = TokenType.Null
    };

    public Lexer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public IReadOnlyList<Token> Tokenize()
    {
        while (!IsAtEnd())
        {
            _start = _current;
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
            case '+':
                AddToken(TokenType.Plus);
                break;
            case '-':
                AddToken(TokenType.Minus);
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
                    throw new SyntaxException("Expected '?' or '.' after '?'.", _line, _tokenColumn, "?");
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
                        SkipLineComment();
                    }
                }
                else if (Match('*'))
                {
                    SkipBlockComment();
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
                    throw new SyntaxException($"Unsupported character '{c}'.", _line, _tokenColumn, c.ToString());
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
                throw new SyntaxException("Unterminated string literal.", _line, _tokenColumn, "\"");
            }

            if (Peek() == '\\')
            {
                Advance();

                if (IsAtEnd())
                {
                    throw new SyntaxException("Unterminated string literal.", _line, _tokenColumn, "\"");
                }

                value.Append(AdvanceEscapedCharacter());
                continue;
            }

            value.Append(Advance());
        }

        if (IsAtEnd())
        {
            throw new SyntaxException("Unterminated string literal.", _line, _tokenColumn, "\"");
        }

        Advance();

        AddToken(TokenType.String, value.ToString());
    }

    private char AdvanceEscapedCharacter()
    {
        var escaped = Advance();

        return escaped switch
        {
            '"' => '"',
            '\\' => '\\',
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            _ => throw new SyntaxException($"Unsupported string escape '\\{escaped}'.", _line, _column - 1, escaped.ToString())
        };
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

    private void SkipLineComment()
    {
        while (!IsAtEnd() && Peek() != '\n')
        {
            Advance();
        }
    }

    private void ScanDocumentationComment()
    {
        while (!IsAtEnd() && Peek() != '\n')
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
        while (!IsAtEnd() && Peek() != '\n')
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

        throw new SyntaxException($"Unsupported directive '{directive}'.", _line, _tokenColumn, directive);
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

    private void SkipBlockComment()
    {
        var commentLine = _line;

        while (!IsAtEnd())
        {
            if (Peek() == '*' && PeekNext() == '/')
            {
                Advance();
                Advance();
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

        throw new SyntaxException("Unterminated multi-line comment.", commentLine, _tokenColumn, "/*");
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
        _tokens.Add(new Token(type, text, literal, _line, _tokenColumn));
    }

    private void NewLine()
    {
        _line++;
        _column = 1;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || char.IsDigit(c);
}
