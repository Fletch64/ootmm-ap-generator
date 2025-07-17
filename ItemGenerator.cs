using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;

namespace OoTMM.Generators;

internal partial class ItemGenerator : GeneratorBase
{
    public IReadOnlyDictionary<string, string> ItemMapOot { get; private set; } = null!;
    public IReadOnlyDictionary<string, string> ItemMapMm { get; private set; } = null!;

    private int ItemCount { get; set; }

    public async ValueTask GenerateAsync()
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        using var reader = new StreamReader(Path.Join(directory, "items.csv"));
        using var deserializer = new CsvReader(
            reader,
            new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                TrimOptions = TrimOptions.Trim,
            });

        var items = new List<Item>();
        var names = new HashSet<string>();
        var map = new Dictionary<string, string>();
        var mapOot = new Dictionary<string, string>();
        var mapMm = new Dictionary<string, string>();

        await foreach (var record in deserializer.GetRecordsAsync<dynamic>())
        {
            var symbolicId = (string)record.symbolic_id;
            var displayName = (string)record.display_name;
            var type = (string)record.type switch
            {
                "PROGRESSION" => "progression",
                "USEFUL" => "useful",
                "FILLER" => "filler",
                "TRAP" => "trap",
                "REMOVED" => "REMOVED",
                _ => throw new InvalidOperationException(
                    $"Unknown type: {record.type}"),
            };

            if (type is "REMOVED") { continue; }

            var parts = symbolicId.Split('_', 2);
            var game = parts[0];
            var gameSpecificId = parts[^1];
            if (game == symbolicId || game is "SHARED")
            {
                game = null;
                gameSpecificId = symbolicId;
            }

            if (!names.Add(displayName))
            {
                throw new InvalidOperationException(
                    $"Duplicate display name: {displayName}");
            }

            mapOot.Add(symbolicId, displayName);
            mapMm.Add(symbolicId, displayName);
            switch (game)
            {
                case "OOT": mapOot.Add(gameSpecificId, displayName); break;
                case "MM": mapMm.Add(gameSpecificId, displayName); break;
            }

            items.Add(new(symbolicId, displayName, type));
        }

        ItemMapOot = map;
        ItemMapOot = mapOot;
        ItemMapMm = mapMm;

        await using var writer = CreatePythonWriter(GetOutputPath("Items.py"));
        await writer.WriteLineAsync(
            """
            from BaseClasses import ItemClassification
            from ..Item import ItemData

            filler = ItemClassification.filler
            progression = ItemClassification.progression
            useful = ItemClassification.useful
            trap = ItemClassification.trap
            skip_balancing = ItemClassification.skip_balancing
            progression_skip_balancing = ItemClassification.progression_skip_balancing

            items: list[ItemData] = [
            """);
        writer.Indent++;
        foreach (var item in items.OrderBy(i => Normalize(i.SymbolicId)))
        {
            await writer.WriteAsync($"ItemData({ItemCount++}");
            await writer.WriteAsync($", \"{item.SymbolicId}\"");
            await writer.WriteAsync($", \"{item.DisplayName}\"");

            if (item.Type is not "progression")
            {
                await writer.WriteAsync($", type = {item.Type}");
            }

            await writer.WriteLineAsync("),");
        }

        writer.Indent--;
        await writer.WriteLineAsync("]");
    }

    private static string Normalize(string id) =>
        GamePattern.Replace(
            NumericPattern.Replace(
                id,
                m => new string('0', Math.Max(0, 4 - m.ValueSpan.Length)) + m.Value),
            m => m.Value switch
            {
                "OOT" => "A",
                "MM" => "B",
                _ => m.Value,
            });

    private static Regex GamePattern { get; } = GetGamePattern();
    private static Regex NumericPattern { get; } = GetNumericPattern();

    [GeneratedRegex("^[^_]*")]
    private static partial Regex GetGamePattern();

    [GeneratedRegex("(?<=^|_)[0-9]+")]
    private static partial Regex GetNumericPattern();

    private record Item(
        string SymbolicId,
        string DisplayName,
        string Type);
}