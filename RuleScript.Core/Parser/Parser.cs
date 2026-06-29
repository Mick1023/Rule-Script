using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Core.Parser;

public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private List<SyntaxException>? _diagnostics;
    private int _current;
    private int _blockDepth;

    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public IReadOnlyList<Statement> Parse()
    {
        Reset();

        if (_tokens.Count == 0 || (_tokens.Count == 1 && _tokens[0].Type == TokenType.EndOfFile))
        {
            return [];
        }

        var statements = new List<Statement>();

        while (!IsAtEnd())
        {
            statements.Add(ParseStatement());
        }

        return statements;
    }

    /// <summary>
    /// Parses as much of the token stream as possible and collects recoverable syntax errors.
    /// </summary>
    public RuleScriptParseResult ParseWithDiagnostics()
    {
        Reset();
        _diagnostics = [];

        if (_tokens.Count == 0 || (_tokens.Count == 1 && _tokens[0].Type == TokenType.EndOfFile))
        {
            return new RuleScriptParseResult([], []);
        }

        var statements = new List<Statement>();

        while (!IsAtEnd())
        {
            ParseRecovering(statements);
        }

        return new RuleScriptParseResult(statements, _diagnostics);
    }

    private Statement ParseStatement()
    {
        if (Match(TokenType.Import))
        {
            if (_blockDepth > 0)
            {
                throw Error(Previous(), "Import statements are only allowed at top level.");
            }

            return ParseImportStatement();
        }

        if (Match(TokenType.Function))
        {
            if (_blockDepth > 0)
            {
                throw Error(Previous(), "Function declarations are only allowed at top level.");
            }

            return ParseFunctionDeclarationStatement();
        }

        if (Match(TokenType.Var))
        {
            return ParseVarStatement();
        }

        if (Match(TokenType.If))
        {
            return ParseIfStatement();
        }

        if (Match(TokenType.While))
        {
            return ParseWhileStatement();
        }

        if (Match(TokenType.Foreach))
        {
            return ParseForeachStatement();
        }

        if (Match(TokenType.Break))
        {
            return ParseBreakStatement();
        }

        if (Match(TokenType.Continue))
        {
            return ParseContinueStatement();
        }

        if (Match(TokenType.Return))
        {
            return ParseReturnStatement();
        }

        if (CheckGlobalAssignment())
        {
            return ParseGlobalAssignmentStatement();
        }

        if (Check(TokenType.Identifier) && CheckNext(TokenType.Assign))
        {
            return ParseAssignmentStatement();
        }

        return ParseExpressionStatement();
    }

    private VarStatement ParseVarStatement()
    {
        var start = Previous();
        var name = Consume(TokenType.Identifier, "Expected variable name after 'var'.");
        Expression? initializer = null;

        if (Match(TokenType.Assign))
        {
            initializer = ParseExpression();
        }

        Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
        return Complete(new VarStatement(name.Lexeme, initializer, name.Line, name.Column), start);
    }

    private AssignmentStatement ParseAssignmentStatement()
    {
        var start = Peek();
        var name = Consume(TokenType.Identifier, "Expected assignment target.");
        Consume(TokenType.Assign, "Expected '=' after assignment target.");
        var value = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after assignment.");
        return Complete(new AssignmentStatement(name.Lexeme, value, name.Line, name.Column), start);
    }

    private ImportStatement ParseImportStatement()
    {
        var importToken = Previous();
        var path = Consume(TokenType.String, "Expected import path string after 'import'.");
        string? alias = null;

        if (Match(TokenType.As))
        {
            alias = Consume(TokenType.Identifier, "Expected identifier for import alias after 'as'.").Lexeme;
        }

        Consume(TokenType.Semicolon, "Expected ';' after import statement.");
        return Complete(new ImportStatement(path.Literal?.ToString() ?? path.Lexeme, alias, importToken.Line, importToken.Column), importToken);
    }

    private GlobalAssignmentStatement ParseGlobalAssignmentStatement()
    {
        var start = Peek();
        var globalToken = Consume(TokenType.Identifier, "Expected 'global' assignment target.");
        Consume(TokenType.Dot, "Expected '.' after 'global'.");
        var name = Consume(TokenType.Identifier, "Expected global variable name after 'global.'.");
        Consume(TokenType.Assign, "Expected '=' after global assignment target.");
        var value = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after global assignment.");
        return Complete(new GlobalAssignmentStatement(name.Lexeme, value, globalToken.Line, globalToken.Column), start);
    }

    private ExpressionStatement ParseExpressionStatement()
    {
        var start = Peek();
        var expression = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after expression.");
        return Complete(new ExpressionStatement(expression), start);
    }

    private IfStatement ParseIfStatement()
    {
        var ifToken = Previous();
        var condition = ParseExpression();
        Consume(TokenType.Then, "Expected 'then' after if condition.");
        Consume(TokenType.Colon, "Expected ':' after 'then'.");

        var thenBranch = ParseBlock(TokenType.Else, TokenType.EndIf, TokenType.End);
        var elseBranch = Array.Empty<Statement>();

        if (Match(TokenType.Else))
        {
            Consume(TokenType.Colon, "Expected ':' after 'else'.");
            elseBranch = ParseBlock(TokenType.EndIf, TokenType.End);
        }

        ConsumeBlockEnd(TokenType.EndIf, "endif", "if statement");
        return Complete(new IfStatement(condition, thenBranch, elseBranch, ifToken.Line, ifToken.Column), ifToken);
    }

    private WhileStatement ParseWhileStatement()
    {
        var whileToken = Previous();
        var condition = ParseExpression();
        Consume(TokenType.Colon, "Expected ':' after while condition.");

        var body = ParseBlock(TokenType.EndWhile, TokenType.End);

        ConsumeBlockEnd(TokenType.EndWhile, "endwhile", "while statement");
        return Complete(new WhileStatement(condition, body, whileToken.Line, whileToken.Column), whileToken);
    }

    private ForeachStatement ParseForeachStatement()
    {
        var foreachToken = Previous();
        var variable = Consume(TokenType.Identifier, "Expected iterator variable after 'foreach'.");
        Consume(TokenType.In, "Expected 'in' after foreach iterator variable.");
        var iterable = ParseExpression();
        Consume(TokenType.Colon, "Expected ':' after foreach iterable expression.");

        var body = ParseBlock(TokenType.EndForeach, TokenType.End);

        ConsumeBlockEnd(TokenType.EndForeach, "endforeach", "foreach statement");
        return Complete(new ForeachStatement(variable.Lexeme, iterable, body, foreachToken.Line, foreachToken.Column), foreachToken);
    }

    private FunctionDeclarationStatement ParseFunctionDeclarationStatement()
    {
        var functionToken = Previous();
        var name = Consume(TokenType.Identifier, "Expected function name after 'function'.");
        Consume(TokenType.LeftParen, "Expected '(' after function name.");

        var parameters = new List<string>();
        var parameterDefinitions = new List<FunctionParameterDefinition>();

        if (!Check(TokenType.RightParen))
        {
            do
            {
                var parameter = Consume(TokenType.Identifier, "Expected parameter name.");

                if (parameters.Contains(parameter.Lexeme, StringComparer.Ordinal))
                {
                    throw Error(parameter, $"Duplicate parameter name '{parameter.Lexeme}'.");
                }

                parameters.Add(parameter.Lexeme);
                string? typeName = null;

                if (Match(TokenType.Colon))
                {
                    var typeToken = Consume(TokenType.Identifier, "Expected parameter type after ':'.");

                    if (!RuleScriptTypeFacts.TryParse(typeToken.Lexeme, out _))
                    {
                        throw Error(typeToken, $"Unknown parameter type '{typeToken.Lexeme}'.");
                    }

                    typeName = typeToken.Lexeme;
                }

                parameterDefinitions.Add(new FunctionParameterDefinition(parameter.Lexeme, typeName));
            }
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightParen, "Expected ')' after function parameters.");
        Consume(TokenType.Colon, "Expected ':' after function declaration.");

        var body = ParseBlock(TokenType.EndFunction, TokenType.End);

        ConsumeBlockEnd(TokenType.EndFunction, "endfunction", "function declaration");
        var declaration = new FunctionDeclarationStatement(name.Lexeme, parameters, body, functionToken.Line, functionToken.Column)
        {
            ParameterDefinitions = parameterDefinitions
        };
        return Complete(declaration, functionToken);
    }

    private BreakStatement ParseBreakStatement()
    {
        var breakToken = Previous();
        Consume(TokenType.Semicolon, "Expected ';' after break.");
        return Complete(new BreakStatement(breakToken.Line, breakToken.Column), breakToken);
    }

    private ContinueStatement ParseContinueStatement()
    {
        var continueToken = Previous();
        Consume(TokenType.Semicolon, "Expected ';' after continue.");
        return Complete(new ContinueStatement(continueToken.Line, continueToken.Column), continueToken);
    }

    private ReturnStatement ParseReturnStatement()
    {
        var returnToken = Previous();
        var value = Check(TokenType.Semicolon) ? null : ParseExpression();

        Consume(TokenType.Semicolon, "Expected ';' after return.");
        return Complete(new ReturnStatement(value, returnToken.Line, returnToken.Column), returnToken);
    }

    private Statement[] ParseBlock(params TokenType[] terminators)
    {
        var statements = new List<Statement>();

        _blockDepth++;

        try
        {
            while (!IsAtEnd() && !terminators.Contains(Peek().Type))
            {
                if (_diagnostics is null)
                {
                    statements.Add(ParseStatement());
                }
                else
                {
                    ParseRecovering(statements, terminators);
                }
            }
        }
        finally
        {
            _blockDepth--;
        }

        return [.. statements];
    }

    private Expression ParseExpression() => ParseOr();

    private Expression ParseOr()
    {
        var expression = ParseAnd();

        while (Match(TokenType.Or))
        {
            var operatorToken = Previous();
            var right = ParseAnd();
            expression = new BinaryExpression(expression, TokenType.Or, right, operatorToken.Line, operatorToken.Column, operatorToken.Lexeme);
        }

        return expression;
    }

    private Expression ParseAnd()
    {
        var expression = ParseEquality();

        while (Match(TokenType.And))
        {
            var operatorToken = Previous();
            var right = ParseEquality();
            expression = new BinaryExpression(expression, TokenType.And, right, operatorToken.Line, operatorToken.Column, operatorToken.Lexeme);
        }

        return expression;
    }

    private Expression ParseEquality()
    {
        var expression = ParseComparison();

        while (Match(TokenType.EqualEqual, TokenType.BangEqual))
        {
            var operatorType = Previous().Type;
            var operatorToken = Previous();
            var right = ParseComparison();
            expression = new BinaryExpression(expression, operatorType, right, operatorToken.Line, operatorToken.Column, operatorToken.Lexeme);
        }

        return expression;
    }

    private Expression ParseComparison()
    {
        var expression = ParseTerm();

        while (Match(TokenType.Greater, TokenType.GreaterOrEqual, TokenType.Less, TokenType.LessOrEqual))
        {
            var operatorType = Previous().Type;
            var operatorToken = Previous();
            var right = ParseTerm();
            expression = new BinaryExpression(expression, operatorType, right, operatorToken.Line, operatorToken.Column, operatorToken.Lexeme);
        }

        return expression;
    }

    private Expression ParseTerm()
    {
        var expression = ParseFactor();

        while (Match(TokenType.Plus, TokenType.Minus))
        {
            var operatorType = Previous().Type;
            var operatorToken = Previous();
            var right = ParseFactor();
            expression = new BinaryExpression(expression, operatorType, right, operatorToken.Line, operatorToken.Column, operatorToken.Lexeme);
        }

        return expression;
    }

    private Expression ParseFactor()
    {
        var expression = ParseUnary();

        while (Match(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            var operatorType = Previous().Type;
            var operatorToken = Previous();
            var right = ParseUnary();
            expression = new BinaryExpression(expression, operatorType, right, operatorToken.Line, operatorToken.Column, operatorToken.Lexeme);
        }

        return expression;
    }

    private Expression ParseUnary()
    {
        if (Match(TokenType.Bang, TokenType.Minus))
        {
            var operatorType = Previous().Type;
            var operatorToken = Previous();
            var operand = ParseUnary();
            return new UnaryExpression(operatorType, operand, operatorToken.Line, operatorToken.Column, operatorToken.Lexeme);
        }

        return ParseCall();
    }

    private Expression ParseCall()
    {
        var expression = ParsePrimary();

        while (true)
        {
            if (Match(TokenType.LeftParen))
            {
                if (expression is MemberAccessExpression { Target: IdentifierExpression moduleIdentifier } memberAccess)
                {
                    var moduleArguments = ParseArguments();
                    expression = new ModuleFunctionCallExpression(
                        moduleIdentifier.Name,
                        memberAccess.MemberName,
                        moduleArguments,
                        memberAccess.Line,
                        memberAccess.Column);
                    continue;
                }

                if (expression is not IdentifierExpression identifier)
                {
                    throw Error(Previous(), "Only named function calls are supported.");
                }

                var arguments = ParseArguments();
                expression = new FunctionCallExpression(identifier.Name, arguments, identifier.Line, identifier.Column);
                continue;
            }

            if (Match(TokenType.LeftBracket))
            {
                var bracket = Previous();
                var index = ParseExpression();
                Consume(TokenType.RightBracket, "Expected ']' after index expression.");
                expression = new IndexExpression(expression, index, bracket.Line, bracket.Column);
                continue;
            }

            if (Match(TokenType.Dot))
            {
                var dot = Previous();
                var member = Consume(TokenType.Identifier, "Expected property name after '.'.");

                expression = expression is IdentifierExpression { Name: "global" }
                    ? new GlobalIdentifierExpression(member.Lexeme, dot.Line, dot.Column)
                    : new MemberAccessExpression(expression, member.Lexeme, dot.Line, dot.Column);
                continue;
            }

            break;
        }

        return expression;
    }

    private IReadOnlyList<Expression> ParseArguments()
    {
        var arguments = new List<Expression>();

        if (!Check(TokenType.RightParen))
        {
            do
            {
                arguments.Add(ParseExpression());
            }
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightParen, "Expected ')' after function arguments.");
        return arguments;
    }

    private Expression ParsePrimary()
    {
        if (Match(TokenType.False))
        {
            return new LiteralExpression(false);
        }

        if (Match(TokenType.True))
        {
            return new LiteralExpression(true);
        }

        if (Match(TokenType.Number, TokenType.String))
        {
            return new LiteralExpression(Previous().Literal);
        }

        if (Match(TokenType.Identifier))
        {
            var token = Previous();
            return new IdentifierExpression(token.Lexeme, token.Line, token.Column);
        }

        if (Match(TokenType.LeftParen))
        {
            var expression = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after expression.");
            return expression;
        }

        if (Match(TokenType.LeftBracket))
        {
            return ParseArrayExpression();
        }

        throw Error(Peek(), "Expected expression.");
    }

    private ArrayExpression ParseArrayExpression()
    {
        var elements = new List<Expression>();

        if (!Check(TokenType.RightBracket))
        {
            do
            {
                elements.Add(ParseExpression());
            }
            while (Match(TokenType.Comma));
        }

        Consume(TokenType.RightBracket, "Expected ']' after array literal.");
        return new ArrayExpression(elements);
    }

    private bool Match(params TokenType[] types)
    {
        foreach (var type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }

        return false;
    }

    private Token Consume(TokenType type, string message)
    {
        if (Check(type))
        {
            return Advance();
        }

        throw Error(Peek(), message);
    }

    private Token ConsumeBlockEnd(TokenType specificType, string specificKeyword, string blockName)
    {
        if (Check(specificType) || Check(TokenType.End))
        {
            return Advance();
        }

        throw Error(Peek(), $"Expected '{specificKeyword}' or 'end' after {blockName}.");
    }

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

    private bool CheckNext(TokenType type) => _current + 1 < _tokens.Count && _tokens[_current + 1].Type == type;

    private bool CheckGlobalAssignment()
    {
        return _current + 3 < _tokens.Count
            && _tokens[_current].Type == TokenType.Identifier
            && _tokens[_current].Lexeme == "global"
            && _tokens[_current + 1].Type == TokenType.Dot
            && _tokens[_current + 2].Type == TokenType.Identifier
            && _tokens[_current + 3].Type == TokenType.Assign;
    }

    private Token Advance()
    {
        if (!IsAtEnd())
        {
            _current++;
        }

        return Previous();
    }

    private bool IsAtEnd() => Peek().Type == TokenType.EndOfFile;

    private Token Peek() => _tokens[_current];

    private Token Previous() => _tokens[_current - 1];

    private T Complete<T>(T statement, Token start) where T : Statement
    {
        var end = Previous();
        statement.SourceSpan = new SourceSpan(start.Line, start.Column, end.EndLine, end.EndColumn);
        return statement;
    }

    private void ParseRecovering(List<Statement> statements, IReadOnlyCollection<TokenType>? terminators = null)
    {
        try
        {
            statements.Add(ParseStatement());
        }
        catch (SyntaxException exception)
        {
            _diagnostics!.Add(exception);
            Synchronize(terminators);
        }
    }

    private void Synchronize(IReadOnlyCollection<TokenType>? terminators)
    {
        if (IsAtEnd())
        {
            return;
        }

        if (_current > 0 && Previous().Type == TokenType.Semicolon)
        {
            return;
        }

        while (!IsAtEnd())
        {
            if (terminators?.Contains(Peek().Type) == true || IsStatementStart(Peek().Type))
            {
                return;
            }

            if (Advance().Type == TokenType.Semicolon)
            {
                return;
            }
        }
    }

    private void Reset()
    {
        _current = 0;
        _blockDepth = 0;
        _diagnostics = null;
    }

    private static bool IsStatementStart(TokenType type)
    {
        return type is TokenType.Import
            or TokenType.Function
            or TokenType.Var
            or TokenType.If
            or TokenType.While
            or TokenType.Foreach
            or TokenType.Break
            or TokenType.Continue
            or TokenType.Return
            or TokenType.Identifier;
    }

    private static SyntaxException Error(Token token, string message)
    {
        var tokenText = token.Type == TokenType.EndOfFile ? "end of file" : token.Lexeme;
        return new SyntaxException(
            $"{message} Unexpected token '{tokenText}'.",
            token.Line,
            token.Column,
            tokenText,
            sourceFile: null,
            endLine: token.EndLine,
            endColumn: token.EndColumn);
    }
}
