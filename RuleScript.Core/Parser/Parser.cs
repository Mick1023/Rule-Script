using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Core.Parser;

public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _current;

    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public IReadOnlyList<Statement> Parse()
    {
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

    private Statement ParseStatement()
    {
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

        if (Check(TokenType.Identifier) && CheckNext(TokenType.Assign))
        {
            return ParseAssignmentStatement();
        }

        return ParseExpressionStatement();
    }

    private VarStatement ParseVarStatement()
    {
        var name = Consume(TokenType.Identifier, "Expected variable name after 'var'.");
        Expression? initializer = null;

        if (Match(TokenType.Assign))
        {
            initializer = ParseExpression();
        }

        Consume(TokenType.Semicolon, "Expected ';' after variable declaration.");
        return new VarStatement(name.Lexeme, initializer, name.Line, name.Column);
    }

    private AssignmentStatement ParseAssignmentStatement()
    {
        var name = Consume(TokenType.Identifier, "Expected assignment target.");
        Consume(TokenType.Assign, "Expected '=' after assignment target.");
        var value = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after assignment.");
        return new AssignmentStatement(name.Lexeme, value, name.Line, name.Column);
    }

    private ExpressionStatement ParseExpressionStatement()
    {
        var expression = ParseExpression();
        Consume(TokenType.Semicolon, "Expected ';' after expression.");
        return new ExpressionStatement(expression);
    }

    private IfStatement ParseIfStatement()
    {
        var ifToken = Previous();
        var condition = ParseExpression();
        Consume(TokenType.Then, "Expected 'then' after if condition.");
        Consume(TokenType.Colon, "Expected ':' after 'then'.");

        var thenBranch = ParseBlock(TokenType.Else, TokenType.EndIf);
        var elseBranch = Array.Empty<Statement>();

        if (Match(TokenType.Else))
        {
            Consume(TokenType.Colon, "Expected ':' after 'else'.");
            elseBranch = ParseBlock(TokenType.EndIf);
        }

        Consume(TokenType.EndIf, "Expected 'endif' after if statement.");
        return new IfStatement(condition, thenBranch, elseBranch, ifToken.Line, ifToken.Column);
    }

    private WhileStatement ParseWhileStatement()
    {
        var whileToken = Previous();
        var condition = ParseExpression();
        Consume(TokenType.Colon, "Expected ':' after while condition.");

        var body = ParseBlock(TokenType.EndWhile);

        Consume(TokenType.EndWhile, "Expected 'endwhile' after while statement.");
        return new WhileStatement(condition, body, whileToken.Line, whileToken.Column);
    }

    private ForeachStatement ParseForeachStatement()
    {
        var foreachToken = Previous();
        var variable = Consume(TokenType.Identifier, "Expected iterator variable after 'foreach'.");
        Consume(TokenType.In, "Expected 'in' after foreach iterator variable.");
        var iterable = ParseExpression();
        Consume(TokenType.Colon, "Expected ':' after foreach iterable expression.");

        var body = ParseBlock(TokenType.EndForeach);

        Consume(TokenType.EndForeach, "Expected 'endforeach' after foreach statement.");
        return new ForeachStatement(variable.Lexeme, iterable, body, foreachToken.Line, foreachToken.Column);
    }

    private BreakStatement ParseBreakStatement()
    {
        var breakToken = Previous();
        Consume(TokenType.Semicolon, "Expected ';' after break.");
        return new BreakStatement(breakToken.Line, breakToken.Column);
    }

    private ContinueStatement ParseContinueStatement()
    {
        var continueToken = Previous();
        Consume(TokenType.Semicolon, "Expected ';' after continue.");
        return new ContinueStatement(continueToken.Line, continueToken.Column);
    }

    private Statement[] ParseBlock(params TokenType[] terminators)
    {
        var statements = new List<Statement>();

        while (!IsAtEnd() && !terminators.Contains(Peek().Type))
        {
            statements.Add(ParseStatement());
        }

        return [.. statements];
    }

    private Expression ParseExpression() => ParseEquality();

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
                if (expression is not IdentifierExpression identifier)
                {
                    throw Error(Previous(), "Only named function calls are supported.");
                }

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
                expression = new MemberAccessExpression(expression, member.Lexeme, dot.Line, dot.Column);
                continue;
            }

            break;
        }

        return expression;
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

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

    private bool CheckNext(TokenType type) => _current + 1 < _tokens.Count && _tokens[_current + 1].Type == type;

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

    private static SyntaxException Error(Token token, string message)
    {
        var tokenText = token.Type == TokenType.EndOfFile ? "end of file" : token.Lexeme;
        return new SyntaxException($"{message} Unexpected token '{tokenText}'.", token.Line, token.Column, tokenText);
    }
}
