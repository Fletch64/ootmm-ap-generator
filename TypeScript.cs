using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace OoTMM.Generators;

public class TypeScript
{
    public static async ValueTask<TypeScript> CreateAsync(
        HttpClient client, string version = "latest",
        string cdn = "https://cdn.jsdelivr.net/npm/")
    {
        var source = await client.GetStringAsync(
            $"{cdn}/typescript@{version}/lib/typescript.js");
        return new(source);
    }

    public static ValueTask<TypeScript> CreateAsync(
        string version = "latest", string cdn = "https://cdn.jsdelivr.net/npm/") =>
        CreateAsync(new(), version, cdn);

    private readonly V8ScriptEngine engine;
    private readonly dynamic compiler;

    private TypeScript(string source)
    {
        engine = new();
        compiler = engine.Evaluate(
            $$"""
              (()=>{
                  {{source}};
                  return ts;
              })();
              """);
    }

    public string Transpile(string source) => compiler.transpile(source);

    public dynamic Evaluate(string source) =>
        engine.Evaluate(
            Transpile(
                $$"""
                  (()=>{
                      return {{source}};
                  })()
                  """));

    public string TranspileModule(string source) =>
        compiler.transpileModule(source, new { target = 99, module = 1 }).outputText;

    public dynamic EvaluateModule(string source) =>
        (ScriptObject)
        engine.Evaluate(
            $$"""
              (()=>{
                  let exports = {};
                  {{TranspileModule(source)}};
                  return exports;
              })()
              """);
}