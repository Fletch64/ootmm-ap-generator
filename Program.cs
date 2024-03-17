using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OoTMM.Generators;

public class Record
{
    [Ignore]
    public string Key { get; set; }
    [Ignore]
    public string Game { get; set; }
    [Name("location")]
    public string Location { get; set; }
    [Name("type")]
    public string Type { get; set; }
    [Name("hint")]
    public string Hint { get; set; }
    [Name("scene")]
    public string Scene { get; set; }
    [Name("id")]
    public string Id { get; set; }
    [Name("item")]
    public string Item { get; set; }
    [Optional]
    public string Time { get; set; }
    [Optional]
    public string Region { get; set; }
    [Optional]
    public string Dungeon { get; set; }
    [Optional]
    public string Logic { get; set; }
    [Optional]
    public string AreaSetName { get; set; }
    [Optional]
    public bool AgeChange { get; set; }
    [Optional]
    public long ArchipelagoId { get; set; }
    [Optional]
    public bool IsMasterQuest { get; set; }



}

public class AreaSet
{
    [YamlMember(Alias = "region")]
    public string Region { get; set; }
    [YamlMember(Alias = "dungeon")]
    public string Dungeon { get; set; }
    [YamlMember(Alias = "time")]
    public string Time { get; set; }
    [YamlMember(Alias = "locations")]
    public Dictionary<string, string> Locations { get; set; }
    [YamlMember(Alias = "events")]
    public Dictionary<string, string> Events { get; set; }
    [YamlMember(Alias = "exits")]
    public Dictionary<string, string> Exits { get; set; }
    [YamlMember(Alias = "stay")]
    public Dictionary<string, string> Stay { get; set; }
    [YamlMember(Alias = "gossip")]
    public Dictionary<string, string> GossipStones { get; set; }
    [YamlMember(Alias = "boss")]
    public bool Boss { get; set; }
    [YamlMember(Alias = "age_change")]
    public bool AgeChange { get; set; }
}

partial class Program
{


    [GeneratedRegex("^[^<]*?<[^>]*>")]
    private static partial Regex PrefixRegex();

    public static long offset = 362100;

    private static async Task<string> DownloadFileAsync(Uri url)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(url);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task DownloadCsvFileAsync(Uri url, string game, Dictionary<string, Record> data, List<string> keys)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(url);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim,
        });

        var records = csv.GetRecords<Record>();
        foreach (var record in records)
        {
            record.Key = $"{record.Location} ({game})";
            record.Game = game;
            data.Add(record.Key, record);
            keys.Add(record.Key);
        }
    }

    static async Task Main()
    {
        var urlBase = "https://raw.githubusercontent.com/OoTMM/OoTMM/master";

        var csvFiles = new[]
        {
            ("OOT", "/packages/data/src/pool/pool_oot.csv"),
            ("MM", "/packages/data/src/pool/pool_mm.csv"),
        };

        var yamlFiles = new[]
        {
            ("OOT","/packages/data/src/world/oot/boss.yml"),
            ("OOT","/packages/data/src/world/oot/bottom_of_the_well.yml"),
            ("OOT","/packages/data/src/world/oot/deku_tree.yml"),
            ("OOT","/packages/data/src/world/oot/dodongo_cavern.yml"),
            ("OOT","/packages/data/src/world/oot/fire_temple.yml"),
            ("OOT","/packages/data/src/world/oot/forest_temple.yml"),
            ("OOT","/packages/data/src/world/oot/ganon_castle.yml"),
            ("OOT","/packages/data/src/world/oot/ganon_tower.yml"),
            ("OOT","/packages/data/src/world/oot/gerudo_fortress.yml"),
            ("OOT","/packages/data/src/world/oot/gerudo_training_grounds.yml"),
            ("OOT","/packages/data/src/world/oot/ice_cavern.yml"),
            ("OOT","/packages/data/src/world/oot/jabu_jabu.yml"),
            ("OOT","/packages/data/src/world/oot/overworld.yml"),
            ("OOT","/packages/data/src/world/oot/shadow_temple.yml"),
            ("OOT","/packages/data/src/world/oot/spirit_temple.yml"),
            ("OOT","/packages/data/src/world/oot/treasure_chest_game.yml"),
            ("OOT","/packages/data/src/world/oot/water_temple.yml"),

            ("MM", "/packages/data/src/world/mm/ancient_castle_of_ikana.yml"),
            ("MM", "/packages/data/src/world/mm/beneath_the_well.yml"),
            ("MM", "/packages/data/src/world/mm/great_bay_temple.yml"),
            ("MM", "/packages/data/src/world/mm/moon.yml"),
            ("MM", "/packages/data/src/world/mm/ocean_spider_house.yml"),
            ("MM", "/packages/data/src/world/mm/overworld.yml"),
            ("MM", "/packages/data/src/world/mm/pirate_fortress.yml"),
            ("MM", "/packages/data/src/world/mm/secret_shrine.yml"),
            ("MM", "/packages/data/src/world/mm/snowhead_temple.yml"),
            ("MM", "/packages/data/src/world/mm/stone_tower_temple.yml"),
            ("MM", "/packages/data/src/world/mm/stone_tower_temple_inverted.yml"),
            ("MM", "/packages/data/src/world/mm/swamp_spider_house.yml"),
            ("MM", "/packages/data/src/world/mm/woodfall_temple.yml"),
        };

        var csvData = new Dictionary<string, Record>();
        var keys = new List<string>();
        foreach (var (game, file) in csvFiles)
        {
            await DownloadCsvFileAsync(new Uri(urlBase + file), game, csvData, keys);
        }

        var currentId = offset;
        foreach (var record in csvData.Values)
        {
            record.ArchipelagoId = currentId;
            currentId++;
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
        foreach (var (game, file) in yamlFiles)
        {
            var yamlData = await DownloadFileAsync(new Uri(urlBase + file));
            var areaSets = deserializer.Deserialize<Dictionary<string, AreaSet>>(yamlData);
            foreach (var (name, areaSet) in areaSets)
            {
                var locations = areaSet.Locations;
                if (locations is null)
                {
                    // Debug.WriteLine(name);
                    continue;
                }

                foreach (var location in locations)
                {
                    var record = csvData[$"{location.Key} ({game})"];
                    record.AreaSetName = name;
                    record.Region = areaSet.Region;
                    record.Dungeon = areaSet.Dungeon;
                    record.Logic = location.Value;
                }
            }
        }

        var itemGenerator = new ItemGenerator();
        var items = await itemGenerator.GetItems();
        var itemId = offset;
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                // Hack to remove articles and color codes.
                var name = PrefixRegex().Replace(item.Name, "");
                // Console.WriteLine($"\"{item.Id}\": OoTMMItemData(code={itemId}, name=\"{name}\", count={1}),");
                itemId++;
                // Debug.WriteLine($"Id:        {item.Id}");
                // Debug.WriteLine($"Item:      {item.Item}");
                // Debug.WriteLine($"Type:      {item.Type}");
                // Debug.WriteLine($"Add:       {item.Add}");
                // Debug.WriteLine($"Flags:     {item.Flags}");
                // Debug.WriteLine($"Draw:      {item.Draw}");
                // Debug.WriteLine($"Object:    {item.Object}");
                // Debug.WriteLine($"Name:      {item.Name}");
                // Debug.WriteLine("--------------------------------");
            }

        }

        // var itemSet = new Dictionary<string, int>();
        // foreach (var (_, record) in csvData)
        // {
        //     itemSet.TryGetValue(record.Item, out var count);
        //     itemSet[record.Item] = count + 1;
        // }

        using var writer = new StreamWriter("locations.py");
        foreach (var key in keys)
        {
            var record = csvData[key];
            record.IsMasterQuest = record.Location.StartsWith("MQ ");
            // writer.WriteLine($"\"{record.Key}\": {record.ArchipelagoId}, ");
            // writer.WriteLine($"Game:        {record.Game}");
            // writer.WriteLine($"Location:    {record.Location}");
            // writer.WriteLine($"Type:        {record.Type}");
            // writer.WriteLine($"Hint:        {record.Hint}");
            // writer.WriteLine($"AreaSetName: {record.AreaSetName}");
            // writer.WriteLine($"Dungeon:     {record.Dungeon}");
            // writer.WriteLine($"Scene:       {record.Scene}");
            // writer.WriteLine($"Region:      {record.Region}");
            // writer.WriteLine($"Id:          {record.Id}");
            // writer.WriteLine($"Item:        {record.Item}");
            // writer.WriteLine($"Logic:       {record.Logic}");
            // writer.WriteLine($"Id:          {record.ArchipelagoId}");
            // writer.WriteLine($"Age:         {record.AgeChange}");
            // writer.WriteLine($"MQ:          {record.IsMasterQuest}");
            // writer.WriteLine("============");

            writer.WriteLine($"(\"{record.Key}\", OoTMMLocationData(region=\"{record.Scene}\", address={record.ArchipelagoId}), mq={record.IsMasterQuest}, type=\"{record.Type}\", logic=\"{record.Logic}\"),");
        }

        // // var itemId = offset;
        // // foreach (var (item, count) in itemSet)
        // // {
        // //     Console.WriteLine($"\"{item}\": OoTMMItemData(code={itemId}, count = {count}),");
        // //     itemId++;
        // //     // Debug.WriteLine($"\"{count}\"");
        // // }
    }
}
