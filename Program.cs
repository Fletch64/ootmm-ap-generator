using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
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
class Program
{
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
        var urlBase = "https://raw.githubusercontent.com/OoTMM/OoTMM/7293aeb33fa5868fbaea1199d99920e80b0c4459";

        var csvFiles = new[]
        {
            ("OOT", "/packages/core/data/oot/pool.csv"),
            ("MM", "/packages/core/data/mm/pool.csv"),
        };

        var yamlFiles = new[]
        {
            ("OOT","/packages/core/data/oot/world/boss.yml"),
            ("OOT","/packages/core/data/oot/world/bottom_of_the_well.yml"),
            ("OOT","/packages/core/data/oot/world/deku_tree.yml"),
            ("OOT","/packages/core/data/oot/world/dodongo_cavern.yml"),
            ("OOT","/packages/core/data/oot/world/fire_temple.yml"),
            ("OOT","/packages/core/data/oot/world/forest_temple.yml"),
            ("OOT","/packages/core/data/oot/world/ganon_castle.yml"),
            ("OOT","/packages/core/data/oot/world/ganon_tower.yml"),
            ("OOT","/packages/core/data/oot/world/gerudo_fortress.yml"),
            ("OOT","/packages/core/data/oot/world/gerudo_training_grounds.yml"),
            ("OOT","/packages/core/data/oot/world/ice_cavern.yml"),
            ("OOT","/packages/core/data/oot/world/jabu_jabu.yml"),
            ("OOT","/packages/core/data/oot/world/overworld.yml"),
            ("OOT","/packages/core/data/oot/world/shadow_temple.yml"),
            ("OOT","/packages/core/data/oot/world/spirit_temple.yml"),
            ("OOT","/packages/core/data/oot/world/treasure_chest_game.yml"),
            ("OOT","/packages/core/data/oot/world/water_temple.yml"),

            ("MM", "/packages/core/data/mm/world/ancient_castle_of_ikana.yml"),
            ("MM", "/packages/core/data/mm/world/beneath_the_well.yml"),
            ("MM", "/packages/core/data/mm/world/great_bay_temple.yml"),
            ("MM", "/packages/core/data/mm/world/moon.yml"),
            ("MM", "/packages/core/data/mm/world/ocean_spider_house.yml"),
            ("MM", "/packages/core/data/mm/world/overworld.yml"),
            ("MM", "/packages/core/data/mm/world/pirate_fortress.yml"),
            ("MM", "/packages/core/data/mm/world/secret_shrine.yml"),
            ("MM", "/packages/core/data/mm/world/snowhead_temple.yml"),
            ("MM", "/packages/core/data/mm/world/stone_tower_temple.yml"),
            ("MM", "/packages/core/data/mm/world/stone_tower_temple_inverted.yml"),
            ("MM", "/packages/core/data/mm/world/swamp_spider_house.yml"),
            ("MM", "/packages/core/data/mm/world/woodfall_temple.yml"),
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
                if (locations is null) { 
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

        var itemSet = new Dictionary<string, int>();
        foreach (var (_, record) in csvData)
        {
            itemSet.TryGetValue(record.Item, out var count);
            itemSet[record.Item] = count + 1;
        }
    
        foreach (var key in keys)
        {
            var record = csvData[key];
            Debug.WriteLine($"\"{record.Key}\": {record.ArchipelagoId}, ");
            // Debug.WriteLine($"Game:        {record.Game}");
            // Debug.WriteLine($"Location:    {record.Location}");
            // Debug.WriteLine($"Type:        {record.Type}");
            // Debug.WriteLine($"Hint:        {record.Hint}");
            // Debug.WriteLine($"AreaSetName: {record.AreaSetName}");
            // Debug.WriteLine($"Dungeon:     {record.Dungeon}");
            // Debug.WriteLine($"Scene:       {record.Scene}");
            // Debug.WriteLine($"Region:      {record.Region}");
            // Debug.WriteLine($"Id:          {record.Id}");
            // Debug.WriteLine($"Item:        {record.Item}");
            // Debug.WriteLine($"Logic:       {record.Logic}");
            // Debug.WriteLine($"Id:          {record.ArchipelagoId}");
            // Debug.WriteLine("============");
        }
        foreach (var (item, count) in itemSet)
        {
            Debug.WriteLine($"\"{item}\"");
            Debug.WriteLine($"\"{count}\"");
        }
    }
}
