using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Esprima;
using Esprima.Ast;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OoTMM.Generators;

internal partial class RegionGenerator : GeneratorBase
{
    private readonly Dictionary<string, string> overrides =
        new() { ["MM_ARROWS_20"] = "MM_ARROWS_30", ["MM_ARROWS_30"] = "MM_ARROWS_40", };

    private int LocationCount { get; set; }

    public async ValueTask GenerateOotAsync(
        HttpClient http, MacroSet macros, IReadOnlyDictionary<string, string> tokenMap)
    {
        await using var writer = CreatePythonWriter(GetOutputPath("RegionsOoT.py"));
        await GenerateAsync(
            writer, http, "OoT", macros,
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
                "packages/data/src/world/oot/water_temple.yml",
            ],
            [
                "packages/data/src/pool/pool_oot.csv",
                "packages/data/src/pool/pool_mm.csv",
            ], tokenMap);
    }

    public async ValueTask GenerateMmAsync(
        HttpClient http, MacroSet macros, IReadOnlyDictionary<string, string> tokenMap)
    {
        await using var writer = CreatePythonWriter(GetOutputPath("RegionsMM.py"));
        await GenerateAsync(
            writer, http, "MM", macros,
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
            ],
            [
                "packages/data/src/pool/pool_mm.csv",
                "packages/data/src/pool/pool_oot.csv",
            ], tokenMap);
    }

    private async ValueTask GenerateAsync(
        PythonWriter writer, HttpClient http, string game, MacroSet macros,
        IEnumerable<string> files, IEnumerable<string> locationFiles,
        IReadOnlyDictionary<string, string> tokenMap)
    {
        JavaScriptParser parser = new();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var filesArray = files as string[] ?? files.ToArray();
        var locationFilesArray = files as string[] ?? locationFiles.ToArray();

        var regions = await LoadRegionsAsync(http, filesArray, deserializer);
        var locations = await LoadLocationsAsync(
            http, locationFilesArray, game, tokenMap);
        var locationCounts = locations.ToDictionary(p => p.Key, _ => 0);

        foreach (var location in regions.Values.SelectMany(r => r.Locations.Keys))
        {
            locationCounts[location]++;
        }

        foreach (var (location, _) in locationCounts.Where(c => c.Value > 1))
        {
            foreach (var region in regions.Values)
            {
                if (region.Locations.Remove(location, out var logic))
                {
                    region.Exits.Add(location, logic);
                }
            }

            regions.Add(
                location,
                new(Exits: [], Locations: new() { [location] = "true" }, Events: []));
        }

        await WriteGeneratedHeaderAsync(
            writer, http, filesArray.Concat(locationFilesArray));

        await writer.WriteLineAsync(
            $$"""
              from typing import Callable
              from ..Location import LocationData
              from ..Region import RegionData
              from ..MacrosBase import always, never
              from .Macros{{game}} import Macros{{game}}

              class {{game}}RegionData(RegionData):
                  def __init__(
                      self,
                      exits: dict[str, Callable[[Macros{{game}}], bool]] = None,
                      locations: dict[str, LocationData] = None,
                      events: dict[str, Callable[[Macros{{game}}], bool]] = None,
                  ):
                      super().__init__("{{game}}", exits, locations, events)


              {{game.ToLowerInvariant()}}_regions: dict[str, {{game}}RegionData] = {
              """);
        writer.Indent++;

        foreach (var (region, data) in regions.OrderBy(p => p.Key))
        {
            await writer.WriteLineAsync($"\"{region}\": {game}RegionData(");
            writer.Indent++;
            if (data.Exits.Count > 0)
            {
                await writer.WriteLineAsync("exits={");
                writer.Indent++;
                foreach (var (name, logic) in data.Exits.OrderBy(p => p.Key))
                {
                    await writer.WriteAsync($"\"{name}\": ");
                    await WriteLogicAsync(
                        writer, parser, macros, tokenMap, game, logic);
                    await writer.WriteLineAsync(",");
                }

                writer.Indent--;
                await writer.WriteLineAsync("},");
            }

            if (data.Locations.Count > 0)
            {
                await writer.WriteLineAsync("locations={");
                writer.Indent++;
                foreach (var (key, logic) in data.Locations.OrderBy(p => p.Key))
                {
                    var (name, type, item) = locations[key];

                    await writer.WriteAsync($"\"{name}\": LocationData(");
                    await writer.WriteAsync($"{LocationCount++},");
                    await writer.WriteAsync($"\"{type}\",");
                    await writer.WriteAsync($"\"{item}\",");
                    await WriteLogicAsync(
                        writer, parser, macros, tokenMap, game, logic);
                    await writer.WriteLineAsync("),");
                }

                writer.Indent--;
                await writer.WriteLineAsync("},");
            }

            if (data.Events.Count > 0)
            {
                await writer.WriteLineAsync("events={");
                writer.Indent++;
                foreach (var (name, logic) in data.Events.OrderBy(p => p.Key))
                {
                    var fixedName = !name.StartsWith("OOT_") && !name.StartsWith("MM_")
                        ? $"{game.ToUpperInvariant()}_{name}"
                        : name;

                    await writer.WriteAsync($"\"{fixedName}\": ");
                    await WriteLogicAsync(
                        writer, parser, macros, tokenMap, game, logic);
                    await writer.WriteLineAsync(",");
                }

                writer.Indent--;
                await writer.WriteLineAsync("},");
            }

            writer.Indent--;
            await writer.WriteLineAsync("),");
        }

        writer.Indent--;
        await writer.WriteLineAsync("}");
    }

    private static async ValueTask WriteLogicAsync(
        PythonWriter writer, JavaScriptParser parser, MacroSet macros,
        IReadOnlyDictionary<string, string> tokenMap, string game, string logic)
    {
        switch (logic)
        {
            case "true": await writer.WriteAsync("always"); break;
            case "false": await writer.WriteAsync("never"); break;
            default:
                await writer.WriteAsync("(");
                await writer.WriteExpressionAsync(
                    new ArrowFunctionExpression(
                        NodeList.Create<Node>([new Identifier("s")]),
                        parser.ProcessExpression(logic),
                        true, false, false),
                    macros, tokenMap, game, "s");
                await writer.WriteAsync(")");
                break;
        }
    }

    private static async ValueTask<Dictionary<string, Region>> LoadRegionsAsync(
        HttpClient http, IEnumerable<string> files, IDeserializer deserializer)
    {
        var result = new Dictionary<string, Region>();

        foreach (var file in files)
        {
            await using var stream = await http.GetStreamAsync(file);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            dynamic fileRegions = deserializer.Deserialize(reader)!;
            foreach (var pair in fileRegions)
            {
                var region = pair.Value;

                var exits = ReadDictionary(region, "exits");
                var locations = ReadDictionary(region, "locations");
                var events = ReadDictionary(region, "events");

                result.Add((string)pair.Key, new(exits, locations, events));
            }
        }

        return result;

        Dictionary<string, string> ReadDictionary(dynamic region, string key)
        {
            var dictionary = new Dictionary<string, string>();
            if (!region.TryGetValue(key, out dynamic data)) { return dictionary; }

            foreach (var pair in data)
            {
                var name = (string)pair.Key;
                var logic = (string)pair.Value;

                if (name.StartsWith("OOT ")) { name = name[4..]; }
                else if (name.StartsWith("MM ")) { name = name[3..]; }

                dictionary.Add(name, logic);
            }

            return dictionary;
        }
    }

    private async ValueTask<Dictionary<string, Location>> LoadLocationsAsync(
        HttpClient http, string[] files, string game,
        IReadOnlyDictionary<string, string> tokenMap)
    {
        var names = await LoadLocationNamesAsync(http, files.Skip(1));

        await using var stream = await http.GetStreamAsync(files.First());
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var deserializer = new CsvReader(
            reader,
            new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                TrimOptions = TrimOptions.Trim,
            });

        var result = new Dictionary<string, Location>();

        await foreach (var record in deserializer.GetRecordsAsync<dynamic>())
        {
            var key = (string)record.location;
            var name = FixName(key);
            var type = (string)record.type;
            var vanillaItem = (string)record.item;

            if (overrides.TryGetValue(vanillaItem, out var value))
            {
                vanillaItem = value;
            }

            if (tokenMap.TryGetValue(vanillaItem, out var replacement))
            {
                vanillaItem = replacement;
            }

            result.Add(key, new(name, type, vanillaItem));
        }

        return result;

        string FixName(string name)
        {
            var key = NumberedSuffixPattern.Replace(name, "");
            return names.Contains(key) ? $"{game} {name}" : name;
        }
    }

    private static async ValueTask<HashSet<string>> LoadLocationNamesAsync(
        HttpClient http, IEnumerable<string> files)
    {
        var result = new HashSet<string>();

        foreach (var file in files)
        {
            await using var stream = await http.GetStreamAsync(file);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var deserializer = new CsvReader(
                reader,
                new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    TrimOptions = TrimOptions.Trim,
                });

            await foreach (var record in deserializer.GetRecordsAsync<dynamic>())
            {
                var name = (string)record.location;
                var key = NumberedSuffixPattern.Replace(name, "");

                result.Add(key);
            }
        }

        return result;
    }

    private static Regex NumberedSuffixPattern { get; } = GetNumberedSuffixPattern();

    [GeneratedRegex(@"\s+\d+$")]
    private static partial Regex GetNumberedSuffixPattern();

    private record Region(
        Dictionary<string, string> Exits,
        Dictionary<string, string> Locations,
        Dictionary<string, string> Events);

    private record Location(string Name, string Type, string VanillaItem);
}