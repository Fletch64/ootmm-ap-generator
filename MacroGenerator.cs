using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace OoTMM.Generators;

internal class MacroGenerator : GeneratorBase
{
    public MacroSet MacrosBase { get; } = new("MacrosBase");
    public MacroSet MacrosOot { get; private set; } = null!;
    public MacroSet MacrosMm { get; private set; } = null!;

    public async ValueTask
        GenerateOotAsync(
            HttpClient http, IReadOnlyDictionary<string, string> tokenMap) =>
        MacrosOot = await GenerateAsync(
            http, GetOutputPath("MacrosOoT.py"),
            [
                "packages/data/src/macros/macros_common.yml",
                "packages/data/src/macros/macros_oot.yml",
            ],
            "OoT", tokenMap);

    public async ValueTask
        GenerateMmAsync(HttpClient http, IReadOnlyDictionary<string, string> tokenMap) =>
        MacrosMm = await GenerateAsync(
            http, GetOutputPath("MacrosMM.py"),
            [
                "packages/data/src/macros/macros_common.yml",
                "packages/data/src/macros/macros_mm.yml",
            ],
            "MM", tokenMap);

    private async ValueTask<MacroSet> GenerateAsync(
        HttpClient http, string output, string[] files, string game,
        IReadOnlyDictionary<string, string> tokenMap)
    {
        var macros = await MacroSet.CreateAsync(
            http, files, "Macros" + game, MacrosBase);

        await using var writer = CreatePythonWriter(output);
        await WriteGeneratedHeaderAsync(writer, http, files);
        await writer.WriteLineAsync(
            $"""
             from typing import TYPE_CHECKING, Any
             from ..{MacrosBase.TypeName} import {MacrosBase.TypeName}
             
             if TYPE_CHECKING:
                 from .. import OoTMMWorld
             
             class {macros.TypeName}({MacrosBase.TypeName}):
             """);
        writer.Indent++;
        await writer.WriteLineAsync(
            $"""
             def __init__(self, world: "OoTMMWorld"):
                 super().__init__("{game.ToUpperInvariant()}", world)
             """);

        foreach (var macro in macros.Macros)
        {
            await writer.WriteExpressionAsync(macro, macros, tokenMap, game);
        }

        writer.Indent--;
        return macros;
    }

    public async ValueTask GenerateBaseStubsAsync()
    {
        await using var writer = CreatePythonWriter(GetStubPath("MacrosBase.py"));
        await writer.WriteLineAsync(
            $"""
             from typing import TYPE_CHECKING, Any
             from BaseClasses import CollectionState

             if TYPE_CHECKING:
                 from .. import OoTMMWorld

             class MacrosBase:
                 def __init__(self, game: str, world: "OoTMMWorld"):
                     self.game = game
                     self.world = world
                     self.state: CollectionState | None = None

             """);
        writer.Indent++;

        foreach (var macro in MacrosBase.Missing)
        {
            await writer.WriteAsync($"def {macro.Name}(self");
            for (var i = 0; i < macro.ArgsMax; i++)
            {
                await writer.WriteAsync($", {(char)('a' + i)}: Any");
                if (i >= macro.ArgsMin) { await writer.WriteAsync(" = None"); }
            }

            await writer.WriteAsync($"):");
            writer.Indent++;
            await writer.WriteLineAsync("raise NotImplementedError");
            writer.Indent--;
        }

        writer.Indent--;
        
        await writer.WriteLineAsync(
            $"""
             
             def always(_: MacrosBase):
                 return True
             
             def never(_: MacrosBase):
                 return True
             """);
    }
}