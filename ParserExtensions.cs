using System.Collections.Generic;
using System.Text.RegularExpressions;
using Esprima;
using Esprima.Ast;

namespace OoTMM.Generators;

internal static partial class ParserExtensions
{
    private static Regex Token { get; } = TokenRegex();

    private static readonly Dictionary<string, string> TokenMap =
        new() { ["var"] = "variable" };

    public static Expression ProcessExpression(
        this JavaScriptParser parser,
        string expression
    ) => parser.ProcessExpression(expression, TokenMap);

    public static Expression ProcessExpression(
        this JavaScriptParser parser,
        string expression,
        Dictionary<string, string> tokenMap
    ) =>
        parser.ParseExpression(
            Token.Replace(
                expression,
                match => tokenMap.GetValueOrDefault(match.Value, match.Value)
            )
        );

    [GeneratedRegex(@"\b(\w+)\b")]
    private static partial Regex TokenRegex();
}
