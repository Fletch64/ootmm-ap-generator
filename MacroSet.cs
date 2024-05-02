using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Esprima;
using Esprima.Ast;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OoTMM.Generators;

public class MacroSet
{
    private readonly string? typename = null;
    private readonly MacroSet? parent = null;
    private readonly List<string> keys = [];
    private readonly Dictionary<string, FunctionExpression> macros = [];
    private readonly SortedSet<string> missing = [];

    public MacroSet() { }

    public MacroSet(string typename, MacroSet? parent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typename);

        this.typename = typename;
        this.parent = parent;
    }

    public Statement Wrapper
    {
        get
        {
            if (TypeName is null)
            {
                return new EmptyStatement();
            }

            var constructor = new FunctionExpression(
                new Identifier("__init__"),
                NodeList.Create<Node>([new Identifier("state")]),
                new BlockStatement(
                    NodeList.Create<Statement>(
                        [
                            new ExpressionStatement(
                                new CallExpression(
                                    new StaticMemberExpression(
                                        new CallExpression(
                                            new Identifier("super"),
                                            [],
                                            false
                                        ),
                                        new Identifier("__init__"),
                                        false
                                    ),
                                    NodeList.Create<Expression>(
                                        [new Identifier("state")]
                                    ),
                                    false
                                )
                            )
                        ]
                    )
                ),
                false,
                false,
                false
            );

            var members = Enumerable
                .Repeat(constructor, 1)
                .Concat(Macros)
                .Select(function => new MethodDefinition(
                    function.Id!,
                    false,
                    function,
                    PropertyKind.Method,
                    false,
                    new NodeList<Decorator>()
                ));

            return new ClassDeclaration(
                new Identifier(TypeName),
                parent?.TypeName is string super ? new Identifier(super) : null,
                new ClassBody(NodeList.Create<ClassElement>(members)),
                new NodeList<Decorator>()
            );
        }
    }

    public MacroSet Root => parent?.Root ?? this;

    public IEnumerable<string> Missing => Root.missing.AsEnumerable();

    private IEnumerable<FunctionExpression> Macros => keys.Select(key => macros[key]);

    public string TypeName => typename ?? string.Empty;

    public static async ValueTask<MacroSet> CreateAsync(
        HttpClient client,
        string uri,
        string typename,
        MacroSet? nested = null
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(uri);

        var result = new MacroSet(typename, nested);
        var parser = new JavaScriptParser();
        await foreach (var (name, expression) in Download(client, uri))
        {
            var (identifier, arguments) = parser.ParseExpression(name) switch
            {
                Identifier id => (id, new NodeList<Node>()),
                CallExpression { Callee: Identifier id, Arguments: var args }
                    => (id, NodeList.Create<Node>(args)),
                _ => throw new NotImplementedException(),
            };
            result.keys.Add(identifier.Name);
            result.macros.Add(
                identifier.Name,
                new FunctionExpression(
                    identifier,
                    arguments,
                    new BlockStatement(
                        NodeList.Create(
                            Enumerable.Repeat<Statement>(
                                new ReturnStatement(parser.ParseExpression(expression)),
                                1
                            )
                        )
                    ),
                    false,
                    false,
                    false
                )
            );
        }
        return result;
    }

    public bool Has(string macro, bool unknown = true) => Has(macro, 0, unknown);

    public bool Has(string macro, int args, bool force = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(macro);

        var found = macros.TryGetValue(macro, out var function)
            ? function.Params.Count == args
            : parent?.Has(macro, args, false) == true;

        if (!found && force)
        {
            var argString = string.Join(
                ", ",
                Enumerable
                    .Repeat("self", 1)
                    .Concat(
                        Enumerable.Range('a', args).Select(c => new string((char)c, 1))
                    )
            );
            Root.missing.Add($"{macro}({argString})");
            found = true;
        }

        return found;
    }

    private static async IAsyncEnumerable<(string, string)> Download(
        HttpClient client,
        string uri
    )
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        using var stream = await client.GetStreamAsync(
            new Uri(uri, UriKind.RelativeOrAbsolute)
        );
        using var reader = new StreamReader(stream);
        foreach (
            var (key, value) in deserializer.Deserialize<IDictionary<string, string>>(
                reader
            )
        )
        {
            yield return (key, value);
        }
    }
}
