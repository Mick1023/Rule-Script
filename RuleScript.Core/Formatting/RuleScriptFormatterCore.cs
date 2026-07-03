using System.Text;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;

namespace RuleScript.Core.Formatting;

internal static class RuleScriptFormatterCore
{
    private const string IndentText = "    ";

    public static string Format(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tokens = new RuleScript.Core.Lexer.Lexer(source).Tokenize(includeComments: true);
        _ = new RuleScript.Core.Parser.Parser(tokens).Parse();

        var writer = new FormatterWriter();
        Token? previousToken = null;

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Type == TokenType.EndOfFile)
            {
                break;
            }

            if (previousToken is not null)
            {
                writer.PreserveBlankLines(GetBlankLineCount(previousToken, token));
            }

            writer.Write(token, FindNextSignificantTokenType(tokens, index + 1));
            previousToken = token;
        }

        return writer.GetText();
    }

    private static int GetBlankLineCount(Token previous, Token current)
    {
        var currentStartLine = current.Line - current.Lexeme.Count(character => character == '\n');
        return Math.Max(0, currentStartLine - previous.Line - 1);
    }

    private static TokenType? FindNextSignificantTokenType(IReadOnlyList<Token> tokens, int startIndex)
    {
        for (var index = startIndex; index < tokens.Count; index++)
        {
            var type = tokens[index].Type;
            if (type == TokenType.EndOfFile)
            {
                return null;
            }

            if (!IsTrivia(type))
            {
                return type;
            }
        }

        return null;
    }

    private enum BlockKind
    {
        If,
        While,
        Foreach,
        Function,
        Switch,
        Parallel,
        Task
    }

    private sealed class BlockState(BlockKind kind)
    {
        public BlockKind Kind { get; } = kind;

        public bool CaseBodyActive { get; set; }
    }

    private sealed class FormatterWriter
    {
        private readonly StringBuilder _builder = new();
        private readonly Stack<BlockState> _blocks = new();
        private int _indent;
        private int _parenthesisDepth;
        private int _bracketDepth;
        private int _braceDepth;
        private bool _atLineStart = true;
        private TokenType? _previous;
        private BlockKind? _pendingBlock;
        private TokenType? _lineHead;

        public void Write(Token token, TokenType? nextSignificantTokenType)
        {
            if (IsTrivia(token.Type))
            {
                WriteTrivia(token, nextSignificantTokenType);
                return;
            }

            if (IsEnd(token.Type))
            {
                WriteEnd(token);
                return;
            }

            if (token.Type is TokenType.Else or TokenType.ElseIf)
            {
                StartAlternateBranch();
            }
            else if (token.Type is TokenType.Case or TokenType.Default)
            {
                StartSwitchBranch();
            }

            if (_atLineStart)
            {
                _lineHead = token.Type;
            }

            if (TryGetBlockKind(token.Type, out var blockKind) && IsAtStructuralDepth())
            {
                _pendingBlock = blockKind;
            }

            WriteToken(token);
            UpdateDepth(token.Type);

            if (token.Type == TokenType.Semicolon)
            {
                NewLine();
            }
            else if (token.Type == TokenType.Colon && IsAtStructuralDepth())
            {
                CompleteBlockHeader();
            }
        }

        public void PreserveBlankLines(int count)
        {
            if (count <= 0)
            {
                return;
            }

            EnsureLineStart();
            _builder.Append('\n', count);
            _atLineStart = true;
            _previous = null;
            _lineHead = null;
        }

        public string GetText()
        {
            var result = _builder.ToString().TrimEnd();
            return result.Length == 0 ? string.Empty : result + "\n";
        }

        private void WriteToken(Token token)
        {
            WriteIndentIfNeeded();

            if (_previous.HasValue && NeedsSpace(_previous.Value, token.Type))
            {
                _builder.Append(' ');
            }

            _builder.Append(token.Lexeme);
            _previous = token.Type;
        }

        private void WriteTrivia(Token token, TokenType? nextSignificantTokenType)
        {
            var triviaIndent = GetIndentForNextToken(nextSignificantTokenType);

            switch (token.Type)
            {
                case TokenType.LineComment:
                    WriteIndentIfNeeded(triviaIndent);
                    if (_previous.HasValue)
                    {
                        _builder.Append(' ');
                    }

                    _builder.Append(token.Lexeme.TrimEnd());
                    NewLine();
                    break;

                case TokenType.MultiLineComment:
                    if (!_atLineStart && _previous.HasValue)
                    {
                        _builder.Append(' ');
                    }

                    WriteMultilineComment(token.Lexeme, triviaIndent);
                    break;

                case TokenType.DocumentationComment:
                case TokenType.RegionStart:
                case TokenType.RegionEnd:
                    EnsureLineStart();
                    WriteIndentIfNeeded(triviaIndent);
                    _builder.Append(token.Lexeme.TrimEnd());
                    NewLine();
                    break;
            }
        }

        private void WriteMultilineComment(string comment, int indent)
        {
            var lines = comment.ReplaceLineEndings("\n").Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0)
                {
                    NewLine();
                }

                WriteIndentIfNeeded(indent);

                var line = lines[index].Trim();
                if (index > 0 && line != "*/")
                {
                    _builder.Append("  ");
                }

                _builder.Append(line);
            }

            if (lines.Length > 1)
            {
                NewLine();
            }
            else
            {
                _builder.Append(' ');
                _previous = null;
            }
        }

        private int GetIndentForNextToken(TokenType? nextTokenType)
        {
            var indent = _indent;

            if (nextTokenType.HasValue && IsEnd(nextTokenType.Value))
            {
                if (_blocks.TryPeek(out var current)
                    && current.Kind == BlockKind.Switch
                    && current.CaseBodyActive)
                {
                    indent--;
                }

                indent--;
            }
            else if (nextTokenType is TokenType.Else or TokenType.ElseIf)
            {
                indent--;
            }
            else if (nextTokenType is TokenType.Case or TokenType.Default
                && _blocks.TryPeek(out var current)
                && current.Kind == BlockKind.Switch
                && current.CaseBodyActive)
            {
                indent--;
            }

            return Math.Max(0, indent);
        }

        private void WriteEnd(Token token)
        {
            EnsureLineStart();

            if (_blocks.TryPeek(out var current) && current.Kind == BlockKind.Switch && current.CaseBodyActive)
            {
                _indent = Math.Max(0, _indent - 1);
            }

            if (_blocks.Count > 0)
            {
                _blocks.Pop();
                _indent = Math.Max(0, _indent - 1);
            }

            WriteIndentIfNeeded();
            _builder.Append(token.Lexeme);
            NewLine();
            _pendingBlock = null;
        }

        private void StartAlternateBranch()
        {
            EnsureLineStart();
            _indent = Math.Max(0, _indent - 1);
        }

        private void StartSwitchBranch()
        {
            EnsureLineStart();

            if (_blocks.TryPeek(out var current)
                && current.Kind == BlockKind.Switch
                && current.CaseBodyActive)
            {
                _indent = Math.Max(0, _indent - 1);
                current.CaseBodyActive = false;
            }
        }

        private void CompleteBlockHeader()
        {
            if (_pendingBlock.HasValue)
            {
                var state = new BlockState(_pendingBlock.Value);
                _blocks.Push(state);
                _indent++;
                _pendingBlock = null;
                NewLine();
                return;
            }

            if (_lineHead is TokenType.Else or TokenType.ElseIf)
            {
                _indent++;
                NewLine();
                return;
            }

            if (_lineHead is TokenType.Case or TokenType.Default)
            {
                if (_blocks.TryPeek(out var current) && current.Kind == BlockKind.Switch)
                {
                    current.CaseBodyActive = true;
                }

                _indent++;
                NewLine();
            }
        }

        private void EnsureLineStart()
        {
            if (!_atLineStart)
            {
                NewLine();
            }
        }

        private void NewLine()
        {
            while (_builder.Length > 0 && _builder[^1] == ' ')
            {
                _builder.Length--;
            }

            if (_builder.Length == 0 || _builder[^1] != '\n')
            {
                _builder.Append('\n');
            }

            _atLineStart = true;
            _previous = null;
            _lineHead = null;
        }

        private void WriteIndentIfNeeded(int? indent = null)
        {
            if (!_atLineStart)
            {
                return;
            }

            _builder.Append(' ', IndentText.Length * (indent ?? _indent));
            _atLineStart = false;
        }

        private bool IsAtStructuralDepth() => _parenthesisDepth == 0 && _bracketDepth == 0 && _braceDepth == 0;

        private void UpdateDepth(TokenType type)
        {
            switch (type)
            {
                case TokenType.LeftParen:
                    _parenthesisDepth++;
                    break;
                case TokenType.RightParen:
                    _parenthesisDepth--;
                    break;
                case TokenType.LeftBracket:
                    _bracketDepth++;
                    break;
                case TokenType.RightBracket:
                    _bracketDepth--;
                    break;
                case TokenType.LeftBrace:
                    _braceDepth++;
                    break;
                case TokenType.RightBrace:
                    _braceDepth--;
                    break;
            }
        }

        private static bool NeedsSpace(TokenType previous, TokenType current)
        {
            if (current is TokenType.RightParen
                or TokenType.RightBracket
                or TokenType.Comma
                or TokenType.Semicolon
                or TokenType.Colon
                or TokenType.Dot
                or TokenType.QuestionDot)
            {
                return false;
            }

            if (previous is TokenType.LeftParen
                or TokenType.LeftBracket
                or TokenType.Dot
                or TokenType.QuestionDot
                or TokenType.Bang)
            {
                return false;
            }

            if (current == TokenType.LeftParen && previous is TokenType.Identifier or TokenType.RightParen or TokenType.RightBracket)
            {
                return false;
            }

            if (current == TokenType.LeftBracket && previous is TokenType.Identifier or TokenType.RightParen or TokenType.RightBracket)
            {
                return false;
            }

            if (current == TokenType.RightBrace)
            {
                return previous != TokenType.LeftBrace;
            }

            if (previous == TokenType.LeftBrace)
            {
                return current != TokenType.RightBrace;
            }

            return true;
        }

        private static bool TryGetBlockKind(TokenType type, out BlockKind kind)
        {
            kind = type switch
            {
                TokenType.If => BlockKind.If,
                TokenType.While => BlockKind.While,
                TokenType.Foreach => BlockKind.Foreach,
                TokenType.Function => BlockKind.Function,
                TokenType.Switch => BlockKind.Switch,
                TokenType.Parallel => BlockKind.Parallel,
                TokenType.Task => BlockKind.Task,
                _ => default
            };

            return type is TokenType.If
                or TokenType.While
                or TokenType.Foreach
                or TokenType.Function
                or TokenType.Switch
                or TokenType.Parallel
                or TokenType.Task;
        }

        private static bool IsEnd(TokenType type) => type is TokenType.End
            or TokenType.EndIf
            or TokenType.EndWhile
            or TokenType.EndForeach
            or TokenType.EndFunction
            or TokenType.EndSwitch
            or TokenType.EndTask
            or TokenType.EndParallel;

    }

    private static bool IsTrivia(TokenType type) => type is TokenType.LineComment
        or TokenType.MultiLineComment
        or TokenType.DocumentationComment
        or TokenType.RegionStart
        or TokenType.RegionEnd;
}
