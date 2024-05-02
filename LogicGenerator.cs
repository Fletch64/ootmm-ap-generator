using System.Net.Http;
using System.Threading.Tasks;

namespace OoTMM.Generators;

internal class LogicGenerator : GeneratorBase
{
    public static async ValueTask<(MacroSet OoT, MacroSet MM)> GenerateAsync(
        HttpClient http
    )
    {
        var common = await GenerateCommonAsync(http);
        var oot = await GenerateOoTAsync(http, common);
        var mm = await GenerateMMAsync(http, common);
        await GenerateBaseAsync(common);
        return (oot, mm);
    }

    public static ValueTask<MacroSet> GenerateCommonAsync(HttpClient http) =>
        GenerateAsync(
            http,
            "Output/LogicCommon.py",
            "packages/data/src/macros/macros_common.yml",
            "LogicCommon",
            new MacroSet("LogicBase")
        );

    public static ValueTask<MacroSet> GenerateOoTAsync(
        HttpClient http,
        MacroSet common
    ) =>
        GenerateAsync(
            http,
            "Output/LogicOoT.py",
            "packages/data/src/macros/macros_oot.yml",
            "OoTLogic",
            common
        );

    public static ValueTask<MacroSet> GenerateMMAsync(
        HttpClient http,
        MacroSet common
    ) =>
        GenerateAsync(
            http,
            "Output/LogicMM.py",
            "packages/data/src/macros/macros_mm.yml",
            "MMLogic",
            common
        );

    private static async ValueTask<MacroSet> GenerateAsync(
        HttpClient http,
        string output,
        string file,
        string name,
        MacroSet parent
    )
    {
        await using var writer = CreatePythonWriter(output);
        await WriteGeneratedHeaderAsync(writer, http, file);
        await writer.WriteLineAsync(
            $"""
            import typing
            import {parent.TypeName}
            
            """
        );
        var macros = await MacroSet.CreateAsync(http, file, name, parent);
        await writer.WriteStatementAsync(macros.Wrapper, macros);
        return macros;
    }

    private static async ValueTask GenerateBaseAsync(MacroSet common)
    {
        await using var writer = CreatePythonWriter("Stubs/LogicBase.py");
        await writer.WriteLineAsync(
            $"""
            class LogicBase:
                def __init__(self, state):
                    self.state = state
            
            """
        );
        writer.Indent++;

        foreach (var macro in common.Missing)
        {
            await writer.WriteLineAsync(
                $"""
                def {macro}:
                    raise NotImplementedError
                
                """
            );
        }

        writer.Indent--;
    }
}
