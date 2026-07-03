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
        foreach (var token in tokens)
        {
            if (token.Type == TokenType.EndOfFile)
            {
                break;
            }

            writer.Write(token);
        }

        return writer.GetText();
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

        public void Write(Token token)
        {
            if (IsTrivia(token.Type))
            {
                WriteTrivia(token);
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

        private void WriteTrivia(Token token)
        {
            switch (token.Type)
            {
                case TokenType.LineComment:
                    WriteIndentIfNeeded();
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

                    WriteMultilineComment(token.Lexeme);
                    break;

                case TokenType.DocumentationComment:
                case TokenType.RegionStart:
                case TokenType.RegionEnd:
                    EnsureLineStart();
                    WriteIndentIfNeeded();
                    _builder.Append(token.Lexeme.TrimEnd());
                    NewLine();
                    break;
            }
        }

        private void WriteMultilineComment(string comment)
        {
            var lines = comment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                WriteIndentIfNeeded();
                _builder.Append(lines[index].TrimEnd());

                if (index < lines.Length - 1)
                {
                    NewLine();
                }
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

        private void WriteIndentIfNeeded()
        {
            if (!_atLineStart)
            {
                return;
            }

            _builder.Append(' ', IndentText.Length * _indent);
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

        private static bool IsTrivia(TokenType type) => type is TokenType.LineComment
            or TokenType.MultiLineComment
            or TokenType.DocumentationComment
            or TokenType.RegionStart
            or TokenType.RegionEnd;
    }
}
