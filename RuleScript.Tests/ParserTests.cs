using RuleScript.Core.Diagnostics;
using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;

namespace RuleScript.Tests;

public sealed class ParserTests
{
    [Fact]
    public void Parse_EmptyScript_ReturnsNoStatements()
    {
        var statements = Parse(string.Empty);

        Assert.Empty(statements);
    }

    [Fact]
    public void Parse_VariableDeclaration_ReturnsVarStatement()
    {
        var statement = Assert.Single(Parse("var a = 1;"));
        var varStatement = Assert.IsType<VarStatement>(statement);

        Assert.Equal("a", varStatement.Name);
        AssertLiteral(1d, varStatement.Initializer);
    }

    [Fact]
    public void Parse_Assignment_ReturnsAssignmentStatement()
    {
        var statement = Assert.Single(Parse("result = \"OK\";"));
        var assignment = Assert.IsType<AssignmentStatement>(statement);

        Assert.Equal("result", assignment.Name);
        AssertLiteral("OK", assignment.Value);
    }

    [Fact]
    public void Parse_ExpressionStatement_ReturnsExpressionStatement()
    {
        var statement = Assert.Single(Parse("name;"));
        var expressionStatement = Assert.IsType<ExpressionStatement>(statement);
        var identifier = Assert.IsType<IdentifierExpression>(expressionStatement.Expression);

        Assert.Equal("name", identifier.Name);
    }

    [Fact]
    public void Parse_FunctionCall_ReturnsFunctionCallExpression()
    {
        var statement = Assert.Single(Parse("Print(a, \"OK\");"));
        var expressionStatement = Assert.IsType<ExpressionStatement>(statement);
        var call = Assert.IsType<FunctionCallExpression>(expressionStatement.Expression);

        Assert.Equal("Print", call.Name);
        Assert.Equal(2, call.Arguments.Count);
        Assert.IsType<IdentifierExpression>(call.Arguments[0]);
        AssertLiteral("OK", call.Arguments[1]);
    }

    [Fact]
    public void Parse_BinaryExpression_UsesExpectedPrecedence()
    {
        var statement = Assert.Single(Parse("1 + 2 * 3;"));
        var expressionStatement = Assert.IsType<ExpressionStatement>(statement);
        var plus = AssertBinary(expressionStatement.Expression, TokenType.Plus);

        AssertLiteral(1d, plus.Left);

        var star = AssertBinary(plus.Right, TokenType.Star);
        AssertLiteral(2d, star.Left);
        AssertLiteral(3d, star.Right);
    }

    [Fact]
    public void Parse_GroupedExpression_OverridesPrecedence()
    {
        var statement = Assert.Single(Parse("(1 + 2) * 3;"));
        var expressionStatement = Assert.IsType<ExpressionStatement>(statement);
        var star = AssertBinary(expressionStatement.Expression, TokenType.Star);

        AssertBinary(star.Left, TokenType.Plus);
        AssertLiteral(3d, star.Right);
    }

    [Fact]
    public void Parse_UnaryExpression_ReturnsUnaryExpression()
    {
        var statement = Assert.Single(Parse("!-enabled;"));
        var expressionStatement = Assert.IsType<ExpressionStatement>(statement);
        var bang = Assert.IsType<UnaryExpression>(expressionStatement.Expression);
        var minus = Assert.IsType<UnaryExpression>(bang.Operand);

        Assert.Equal(TokenType.Bang, bang.Operator);
        Assert.Equal(TokenType.Minus, minus.Operator);
    }

    [Fact]
    public void Parse_IfStatementWithoutElse_ReturnsIfStatement()
    {
        var statement = Assert.Single(Parse("""
            if a > 0 then:
                result = "OK";
            endif
            """));

        var ifStatement = Assert.IsType<IfStatement>(statement);

        AssertBinary(ifStatement.Condition, TokenType.Greater);
        Assert.Single(ifStatement.ThenBranch);
        Assert.Empty(ifStatement.ElseBranch);
    }

    [Fact]
    public void Parse_IfStatementWithElse_ReturnsIfStatement()
    {
        var statement = Assert.Single(Parse("""
            if a + b > 0 then:
                result = "OK";
            else:
                result = "NG";
            endif
            """));

        var ifStatement = Assert.IsType<IfStatement>(statement);

        AssertBinary(ifStatement.Condition, TokenType.Greater);
        Assert.Single(ifStatement.ThenBranch);
        Assert.Single(ifStatement.ElseBranch);
    }

    [Fact]
    public void Parse_InvalidSyntax_ThrowsSyntaxException()
    {
        Assert.Throws<SyntaxException>(() => Parse("var = 1;"));
    }

    private static IReadOnlyList<Statement> Parse(string script)
    {
        var lexer = new Lexer(script);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);

        return parser.Parse();
    }

    private static BinaryExpression AssertBinary(Expression expression, TokenType operatorType)
    {
        var binary = Assert.IsType<BinaryExpression>(expression);
        Assert.Equal(operatorType, binary.Operator);
        return binary;
    }

    private static void AssertLiteral(object? expected, Expression? expression)
    {
        var literal = Assert.IsType<LiteralExpression>(expression);
        Assert.Equal(expected, literal.Value);
    }
}
