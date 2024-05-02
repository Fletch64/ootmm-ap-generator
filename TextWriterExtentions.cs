using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Esprima;
using Esprima.Ast;
using Esprima.Utils;

namespace OoTMM.Generators;

internal record Context
{
    public string Self { get; init; } = null!;

    public MacroSet Macros { get; init; } = null!;

    public IImmutableSet<string> Locals { get; init; } = ImmutableHashSet<string>.Empty;

    public int Precedence { get; init; } = 0;

    public Context Nested(IEnumerable<string> locals) =>
        this with
        {
            Locals = Locals.Union(locals)
        };
}

internal static partial class TextWriterExtentions
{
    public static ValueTask WriteExpressionAsync(
        this TextWriter writer,
        Expression expression,
        MacroSet macros,
        string self = "self"
    ) =>
        writer.WriteExpressionAsync(
            expression,
            new Context() { Macros = macros, Self = self, }
        );

    public static ValueTask WriteStatementAsync(
        this TextWriter writer,
        Statement statement,
        MacroSet macros,
        string self = "self"
    ) =>
        writer.WriteStatementAsync(
            statement,
            new Context() { Macros = macros, Self = self, }
        );

    private static async ValueTask WriteExpressionAsync(
        this TextWriter writer,
        Expression expression,
        Context context
    )
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(expression);

        var indented = writer is IndentedTextWriter temp
            ? temp
            : new IndentedTextWriter(writer);

        await indented.WriteInternalAsync(expression, context);
    }

    private static async ValueTask WriteStatementAsync(
        this TextWriter writer,
        Statement statement,
        Context context
    )
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(statement);

        var indented = writer is IndentedTextWriter temp
            ? temp
            : new IndentedTextWriter(writer);

        await indented.WriteInternalAsync(statement, context);
    }

    private static ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        Expression expression,
        Context context
    ) =>
        expression switch
        {
            ArrowFunctionExpression func => writer.WriteInternalAsync(func, context),
            BinaryExpression exp => writer.WriteInternalAsync(exp, context),
            CallExpression call => writer.WriteInternalAsync(call, context),
            FunctionExpression func => writer.WriteInternalAsync(func, context),
            Identifier identifier => writer.WriteInternalAsync(identifier, context),
            Literal literal => writer.WriteInternalAsync(literal, context),
            MemberExpression member => writer.WriteInternalAsync(member, context),
            UnaryExpression exp => writer.WriteInternalAsync(exp, context),
            _
                => ValueTask.FromException(
                    new NotImplementedException(
                        $"Unhandled Node Type: {expression.Type}"
                    )
                ),
        };

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        ArrowFunctionExpression function,
        Context context
    )
    {
        if (!function.Expression)
        {
            throw new NotImplementedException();
        }

        await writer.WriteAsync("lambda");
        var parameters = function.Params.Cast<Identifier>().Select(p => p.Name);
        context = context.Nested(parameters);
        var first = true;
        foreach (var param in parameters)
        {
            if (!first)
            {
                await writer.WriteAsync(",");
            }
            await writer.WriteAsync(" ");
            await writer.WriteAsync(param);
        }
        await writer.WriteAsync(": ");
        await writer.WriteInternalAsync(function.Body.As<Expression>(), context);
    }

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        BinaryExpression expression,
        Context context
    )
    {
        var (op, precedence) = expression.Operator switch
        {
            BinaryOperator.LogicalOr => ("or", 3),
            BinaryOperator.LogicalAnd => ("and", 4),
            BinaryOperator.Plus => ("+", 11),
            BinaryOperator.Minus => ("-", 11),
            var type
                => throw new NotImplementedException(
                    $"Unhandled Binary Operator: {type}"
                ),
        };

        var parentheses = context.Precedence > precedence;
        context = context with { Precedence = precedence };

        if (parentheses)
        {
            await writer.WriteAsync("(");
        }
        await writer.WriteInternalAsync(expression.Left, context);
        await writer.WriteAsync(" ");
        await writer.WriteAsync(op);
        await writer.WriteAsync(" ");
        await writer.WriteInternalAsync(expression.Right, context);
        if (parentheses)
        {
            await writer.WriteAsync(")");
        }
    }

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        CallExpression call,
        Context context
    )
    {
        if (call.Callee is Identifier)
        {
            var callee = call.Callee.As<Identifier>().Name;
            if (callee is "cond" && call.Arguments.Count is 3)
            {
                var parentheses = context.Precedence > 2;
                context = context with { Precedence = 2 };

                if (parentheses)
                {
                    await writer.WriteAsync("(");
                }
                await writer.WriteInternalAsync(call.Arguments[1], context);
                await writer.WriteAsync(" if ");
                await writer.WriteInternalAsync(call.Arguments[0], context);
                await writer.WriteAsync(" else ");
                await writer.WriteInternalAsync(call.Arguments[2], context);
                if (parentheses)
                {
                    await writer.WriteAsync(")");
                }
                return;
            }

            if (callee is "setting")
            {
                await writer.WriteAsync(context.Self);
                await writer.WriteAsync(".setting(");
                var first = true;
                foreach (var arg in call.Arguments.Cast<Identifier>())
                {
                    if (!first)
                    {
                        await writer.WriteAsync(", ");
                    }
                    first = false;
                    await writer.WriteAsync("\"");
                    await writer.WriteAsync(
                        SettingPattern().Replace(arg.Name, "_$0").ToLower()
                    );
                    await writer.WriteAsync("\"");
                }
                await writer.WriteAsync(")");
                return;
            }

            if (callee is not "super")
            {
                if (!context.Macros.Has(callee, call.Arguments.Count))
                {
                    throw new NotImplementedException();
                }
                await writer.WriteAsync(context.Self);
                await writer.WriteAsync(".");
            }
            await writer.WriteAsync(callee);
        }
        else
        {
            await writer.WriteInternalAsync(call.Callee, context);
        }

        await writer.WriteAsync("(");
        context = context with { Precedence = -1 };
        {
            var first = true;
            foreach (var arg in call.Arguments)
            {
                if (!first)
                {
                    await writer.WriteAsync(", ");
                }
                first = false;
                await writer.WriteInternalAsync(arg, context with { Precedence = -1 });
            }
        }
        await writer.WriteAsync(")");
    }

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        FunctionExpression function,
        Context context
    )
    {
        var name = function.Id!.Name;
        var parameters = function.Params.Cast<Identifier>().Select(p => p.Name);
        if (function.Params.Count is 0)
        {
            await writer.WriteLineAsync("@property");
        }
        await writer.WriteAsync("def ");
        await writer.WriteAsync(name);
        await writer.WriteAsync("(");
        await writer.WriteAsync(context.Self);
        foreach (var param in parameters)
        {
            await writer.WriteAsync(", ");
            await writer.WriteAsync(param);
        }
        await writer.WriteLineAsync("):");
        writer.Indent++;
        await writer.WriteInternalAsync(function.Body, context.Nested(parameters));
        writer.Indent--;
    }

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        Identifier identifier,
        Context context
    )
    {
        var name = identifier.Name;
        var local = context.Locals.Contains(name);
        var macro = context.Macros.Has(name, !local && context.Precedence >= 0);
        if (macro || local)
        {
            if (macro)
            {
                await writer.WriteAsync(context.Self);
                await writer.WriteAsync(".");
            }
            await writer.WriteAsync(name);
        }
        else
        {
            await writer.WriteAsync("\"");
            await writer.WriteAsync(name);
            await writer.WriteAsync("\"");
        }
    }

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        Literal literal,
        Context context
    )
    {
        switch (literal.TokenType)
        {
            default:
                literal.WriteJavaScript(writer);
                break;
            case TokenType.BooleanLiteral:
                await writer.WriteAsync((bool)literal.BooleanValue! ? "True" : "False");
                break;
        }
        ;
    }

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        MemberExpression member,
        Context context
    )
    {
        await writer.WriteInternalAsync(member.Object, context);
        await writer.WriteAsync(".");
        await writer.WriteAsync(member.Property.As<Identifier>().Name);
    }

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        UnaryExpression expression,
        Context context
    )
    {
        var (op, precedence) = expression.Operator switch
        {
            UnaryOperator.LogicalNot => ("not ", 5),
            var type
                => throw new NotImplementedException(
                    $"Unhandled Unary Operator: {type}"
                ),
        };

        var parentheses = context.Precedence > precedence;
        context = context with { Precedence = precedence };

        if (parentheses)
        {
            await writer.WriteAsync("(");
        }
        await writer.WriteAsync(op);
        await writer.WriteInternalAsync(expression.Argument, context);
        if (parentheses)
        {
            await writer.WriteAsync(")");
        }
    }

    private static ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        Statement statement,
        Context context
    ) =>
        statement switch
        {
            BlockStatement block => writer.WriteInternalAsync(block, context),
            ClassDeclaration declaration
                => writer.WriteInternalAsync(declaration, context),
            ExpressionStatement expression
                => writer.WriteInternalAsync(expression, context),
            ReturnStatement ret => writer.WriteInternalAsync(ret, context),
            _
                => ValueTask.FromException(
                    new NotImplementedException(
                        $"Unhandled Node Type: {statement.Type}"
                    )
                ),
        };

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        BlockStatement block,
        Context context
    )
    {
        var body = block.Body;
        if (body.Count is 0)
        {
            await writer.WriteLineAsync("pass");
        }
        foreach (var statement in body)
        {
            await writer.WriteInternalAsync(statement, context);
        }
    }

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        ClassDeclaration declaration,
        Context context
    )
    {
        await writer.WriteAsync("class ");
        await writer.WriteAsync(declaration.Id!.Name);
        await writer.WriteAsync("(");
        if (declaration.SuperClass is Identifier super)
        {
            await writer.WriteAsync(super.Name);
        }
        await writer.WriteLineAsync("):");
        writer.Indent++;
        foreach (var member in declaration.Body.Body)
        {
            var method = member.As<MethodDefinition>();
            await writer.WriteInternalAsync(method.Value, context);
            await writer.WriteLineAsync();
        }
    }

    private static ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        ExpressionStatement function,
        Context context
    ) => writer.WriteInternalAsync(function.Expression, context);

    private static async ValueTask WriteInternalAsync(
        this IndentedTextWriter writer,
        ReturnStatement ret,
        Context context
    )
    {
        await writer.WriteAsync("return");
        if (ret.Argument is Expression exp)
        {
            await writer.WriteAsync(" ");
            await writer.WriteInternalAsync(exp, context);
            await writer.WriteLineAsync();
        }
    }

    [GeneratedRegex("\\B(?<![A-Z])[A-Z]+")]
    private static partial Regex SettingPattern();
}
