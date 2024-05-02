using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Esprima;
using Esprima.Ast;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OoTMM.Generators;

internal class RegionGenerator : GeneratorBase
{
    public static async Task GenerateAsync(
        HttpClient http,
        MacroSet macrosOoT,
        MacroSet macrosMM
    )
    {
        await GenerateOoTAsync(http, macrosOoT);
        await GenerateMMAsync(http, macrosMM);
    }

    public static async ValueTask GenerateOoTAsync(HttpClient http, MacroSet macros)
    {
        await using var writer = CreatePythonWriter("Output/RegionsOoT.py");
        await GenerateAsync(
            writer,
            http,
            "OoT",
            macros,
            [
                "packages/data/src/world/oot/boss.yml",
                "packages/data/src/world/oot/bottom_of_the_well.yml",
                "packages/data/src/world/oot/deku_tree.yml",
                "packages/data/src/world/oot/dodongo_cavern.yml",
                "packages/data/src/world/oot/fire_temple.yml",
                "packages/data/src/world/oot/forest_temple.yml",
                "packages/data/src/world/oot/ganon_castle.yml",
                "packages/data/src/world/oot/ganon_tower.yml",
                "packages/data/src/world/oot/gerudo_fortress.yml",
                "packages/data/src/world/oot/gerudo_training_grounds.yml",
                "packages/data/src/world/oot/ice_cavern.yml",
                "packages/data/src/world/oot/jabu_jabu.yml",
                "packages/data/src/world/oot/overworld.yml",
                "packages/data/src/world/oot/shadow_temple.yml",
                "packages/data/src/world/oot/spirit_temple.yml",
                "packages/data/src/world/oot/treasure_chest_game.yml",
                "packages/data/src/world/oot/water_temple.yml"
            ]
        );
    }

    public static async ValueTask GenerateMMAsync(HttpClient http, MacroSet macros)
    {
        await using var writer = CreatePythonWriter("Output/RegionsMM.py");
        await GenerateAsync(
            writer,
            http,
            "MM",
            macros,
            [
                "packages/data/src/world/mm/ancient_castle_of_ikana.yml",
                "packages/data/src/world/mm/beneath_the_well.yml",
                "packages/data/src/world/mm/great_bay_temple.yml",
                "packages/data/src/world/mm/moon.yml",
                "packages/data/src/world/mm/ocean_spider_house.yml",
                "packages/data/src/world/mm/overworld.yml",
                "packages/data/src/world/mm/pirate_fortress.yml",
                "packages/data/src/world/mm/secret_shrine.yml",
                "packages/data/src/world/mm/snowhead_temple.yml",
                "packages/data/src/world/mm/stone_tower_temple.yml",
                "packages/data/src/world/mm/stone_tower_temple_inverted.yml",
                "packages/data/src/world/mm/swamp_spider_house.yml",
                "packages/data/src/world/mm/woodfall_temple.yml",
            ]
        );
    }

    private static async ValueTask GenerateAsync(
        PythonWriter writer,
        HttpClient http,
        string game,
        MacroSet macros,
        IEnumerable<string> files
    )
    {
        JavaScriptParser parser = new();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        await WriteGeneratedHeaderAsync(writer, http, files);

        await writer.WriteLineAsync(
            $$"""
            import typing
            import OoTMMRegionData


            class {{game}}RegionData(OoTMMRegionData):
                def __init__(exits={}, locations={}, events={}):
                    super().__init__("{{game}}", exits, locations, events)


            {{game.ToLowerInvariant()}}_regions: dict[{{game}}RegionData] = {
            """
        );
        writer.Indent++;

        foreach (var file in files)
        {
            using var stream = await http.GetStreamAsync(file);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            dynamic regions = deserializer.Deserialize(reader)!;
            foreach (dynamic pair in regions)
            {
                var region = pair.Value;

                await writer.WriteLineAsync($"\"{pair.Key}\": {game}RegionData(");
                writer.Indent++;
                if (region.TryGetValue("exits", out dynamic exits) && exits.Count > 0)
                {
                    await writer.WriteLineAsync($"exits={{");
                    writer.Indent++;
                    foreach (dynamic exit in exits)
                    {
                        await writer.WriteAsync($"\"{exit.Key}\": (");
                        await WriteLogicAsync(writer, parser, macros, exit.Value);
                        await writer.WriteLineAsync("),");
                    }
                    writer.Indent--;
                    await writer.WriteLineAsync($"}},");
                }

                if (
                    region.TryGetValue("locations", out dynamic locations)
                    && locations.Count > 0
                )
                {
                    await writer.WriteLineAsync($"locations={{");
                    writer.Indent++;
                    foreach (dynamic location in locations)
                    {
                        var name = location.Key;
                        var logic = location.Value;
                        await writer.WriteAsync($"\"{name}\": (");
                        await WriteLogicAsync(writer, parser, macros, logic);
                        await writer.WriteLineAsync("),");
                    }
                    writer.Indent--;
                    await writer.WriteLineAsync($"}},");
                }

                if (
                    region.TryGetValue("events", out dynamic events)
                    && events.Count > 0
                )
                {
                    await writer.WriteLineAsync($"events={{");
                    writer.Indent++;
                    foreach (dynamic location in events)
                    {
                        var name = location.Key;
                        var logic = location.Value;
                        await writer.WriteAsync($"\"{name}\": (");
                        await WriteLogicAsync(writer, parser, macros, logic);
                        await writer.WriteLineAsync("),");
                    }
                    writer.Indent--;
                    await writer.WriteLineAsync($"}},");
                }
                writer.Indent--;
                await writer.WriteLineAsync($"),");
            }
        }

        writer.Indent--;
        await writer.WriteLineAsync($"}}");
    }

    private static ValueTask WriteLogicAsync(
        PythonWriter writer,
        JavaScriptParser parser,
        MacroSet macros,
        string logic
    ) =>
        writer.WriteExpressionAsync(
            new ArrowFunctionExpression(
                NodeList.Create<Node>([new Identifier("s")]),
                parser.ParseExpression(logic),
                true,
                false,
                false
            ),
            macros,
            "s"
        );
}
