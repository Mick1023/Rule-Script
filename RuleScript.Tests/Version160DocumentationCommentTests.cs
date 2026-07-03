using RuleScript.Core.Lexer;
using RuleScript.Core.Parser;
using RuleScript.Core.Parser.Ast;
using RuleScript.Core.Runtime;

namespace RuleScript.Tests;

public sealed class Version160DocumentationCommentTests
{
    [Fact]
    public void Lexer_PreservesDocumentationCommentTokens()
    {
        const string source = """
            /// Summary
            /// @param id Player ID
            function FindPlayer(id):
                return id;
            endfunction
            """;

        var tokens = new Lexer(source).Tokenize();
        var comments = tokens
            .Where(token => token.Type == TokenType.DocumentationComment)
            .ToArray();

        Assert.Collection(
            comments,
            comment => Assert.Equal("Summary", comment.Literal),
            comment => Assert.Equal("@param id Player ID", comment.Literal));
    }

    [Fact]
    public void Parser_AssociatesMultiLineDocumentationWithFollowingFunction()
    {
        const string source = """
            ///
            /// Updates player data.
            /// @param id Player ID
            /// @return Player name
            ///
            function GetPlayerName(id):
                return "Player";
            endfunction
            """;

        var statements = new Parser(new Lexer(source).Tokenize()).Parse();
        var function = Assert.IsType<FunctionDeclarationStatement>(Assert.Single(statements));

        Assert.Equal(
            "Updates player data.\n@param id Player ID\n@return Player name",
            function.Documentation);
    }

    [Fact]
    public void Parser_AssociatesDocumentationWithExportedFunction()
    {
        const string source = """
            /// Public function.
            export function GetValue():
                return 1;
            endfunction
            """;

        var statements = new Parser(new Lexer(source).Tokenize()).Parse();
        var function = Assert.IsType<FunctionDeclarationStatement>(Assert.Single(statements));

        Assert.True(function.IsExported);
        Assert.Equal("Public function.", function.Documentation);
    }

    [Fact]
    public void Analyze_ExposesFunctionDocumentation()
    {
        const string source = """
            /// Adds two values.
            /// @param left First value
            /// @param right Second value
            /// @return Sum
            function Add(left: number, right: number):
                return left + right;
            endfunction
            """;

        var result = new RuleScriptEngine().Analyze(source);
        var function = Assert.Single(result.UserFunctions);

        Assert.Equal(
            "Adds two values.\n@param left First value\n@param right Second value\n@return Sum",
            function.Documentation);
    }

    [Fact]
    public void DocumentationComment_DoesNotAffectExecution()
    {
        const string source = """
            /// Returns a fixed value.
            function GetValue():
                return 42;
            endfunction

            result = GetValue();
            """;

        var context = new RuleScriptEngine().Execute(source);

        Assert.Equal(42d, context.Get<double>("result"));
    }
}
