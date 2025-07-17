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

public record MacroStub(string Name, int ArgsMin, int ArgsMax);

public class MacroSet
{
    private readonly string? typename;

    private readonly MacroSet? parent;

    private readonly Dictionary<string, FunctionExpression> macros = [];
    private readonly Dictionary<string, MacroStub> missing = [];

    public MacroSet(string typename, MacroSet? parent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typename);

        this.typename = typename;
        this.parent = parent;
    }

    public MacroSet Root => parent?.Root ?? this;

    public IEnumerable<MacroStub> Missing => Root.missing.Values.OrderBy(m => m.Name);

    public IEnumerable<FunctionExpression> Macros =>
        macros.OrderBy(m => m.Key).Select(m => m.Value);

    public string TypeName => typename ?? string.Empty;

    public static async ValueTask<MacroSet> CreateAsync(
        HttpClient client,
        string[] uris,
        string typename,
        MacroSet? nested = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(uris);

        var result = new MacroSet(typename, nested);
        var parser = new JavaScriptParser();
        foreach (var uri in uris)
        {
            await foreach (var (name, expression) in Download(client, uri))
            {
                var (identifier, arguments) = parser.ProcessExpression(name) switch
                {
                    Identifier id => (id, new NodeList<Node>()),
                    CallExpression { Callee: Identifier id, Arguments: var args }
                        => (id, NodeList.Create<Node>(args)),
                    _ => throw new NotImplementedException(),
                };
                result.macros.Add(
                    identifier.Name,
                    new(
                        identifier,
                        arguments,
                        new(
                            NodeList.Create(
                                Enumerable.Repeat<Statement>(
                                    new ReturnStatement(
                                        parser.ProcessExpression(expression)),
                                    1))),
                        false,
                        false,
                        false));
            }
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

        if (found || !force) { return found; }

        if (Root.missing.TryGetValue(macro, out var stub))
        {
            var newStub = stub with
            {
                ArgsMin = Math.Min(stub.ArgsMin, args),
                ArgsMax = Math.Max(stub.ArgsMax, args),
            };
            Root.missing[macro] = newStub;
        }
        else { Root.missing.Add(macro, new(macro, args, args)); }

        return true;
    }

    private static async IAsyncEnumerable<(string, string)> Download(
        HttpClient client,
        string uri)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        await using var stream = await client.GetStreamAsync(
            new Uri(uri, UriKind.RelativeOrAbsolute));
        using var reader = new StreamReader(stream);
        foreach (
            var (key, value) in deserializer.Deserialize<IDictionary<string, string>>(
                reader)
        ) { yield return (key, value); }
    }
}