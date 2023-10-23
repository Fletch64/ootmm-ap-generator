using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using CsvHelper;
using System.Globalization;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using System.Diagnostics;
using System.ComponentModel;

namespace combineCsvYaml
{
    public class Location
    {
        [Ignore]
        public string key { get; set; }
        [Ignore]
        public string game { get; set; }
        public string location { get; set; }
        public string type { get; set; }
        public string hint { get; set; }
        public string scene { get; set; }
        public string id { get; set; }
        public string item { get; set; }
        [Optional]
        public string time { get; set; }
        [Optional]
        public string region { get; set; }
        [Optional]
        public string dungeon { get; set; }
        [Optional]
        public string logic { get; set; }
        [Optional]
        public string areaSetName { get; set; }
        [Optional]
        public bool ageChange { get; set; }
    }

    public class AreaSet
    {
        public string region;
        public string dungeon;
        public string time;

        public Dictionary<string, string> locations;
        public Dictionary<string, string> events;
        public Dictionary<string, string> exits;
        public Dictionary<string, string> stay;
        public Dictionary<string, string> gossip;

        public bool boss;
        public bool age_change;
    }

    class Program
    {
        private static string getUrlFile(string url)
        {
            var webRequest = WebRequest.Create(url);
            using (var response = webRequest.GetResponse())
            using (var content = response.GetResponseStream())
            using (var reader = new StreamReader(content))
            {
                var strContent = reader.ReadToEnd();
                return strContent;
            }
        }

        private static void getCSVFile(string url, string game, Dictionary<string, Location> dictionary)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                //PrepareHeaderForMatch = args => args.Header.Trim(),
                TrimOptions = TrimOptions.Trim,
            };
            var webRequest = WebRequest.Create(url);
            using (var response = webRequest.GetResponse())
            using (var content = response.GetResponseStream())
            using (var reader = new StreamReader(content))
            using (var csv = new CsvReader(reader, config))
            {
                var records = csv.GetRecords<Location>();
                foreach (var record in records)
                {
                    record.key = game + "-" + record.location;
                    record.game = game;
                    dictionary.Add(record.key, record);
                }
            }

        }

        static void Main(string[] args)
        {
            var csvData = new Dictionary<string, Location>();

            getCSVFile(
                "https://raw.githubusercontent.com/OoTMM/OoTMM/master/packages/core/data/oot/pool.csv",
                "OOT",
            csvData);
            getCSVFile(
                "https://raw.githubusercontent.com/OoTMM/OoTMM/master/packages/core/data/mm/pool.csv",
                "MM",
                csvData);

            var yamlFiles = new[]
            {
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
            };
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            
            foreach (var (game, file) in yamlFiles)
            {
                var yamlData = getUrlFile("https://raw.githubusercontent.com/OoTMM/OoTMM/7293aeb33fa5868fbaea1199d99920e80b0c4459" + file);
                var areaSets = deserializer.Deserialize<Dictionary<string, AreaSet>>(yamlData);
                var idCounter = 0;
                foreach (var areaSet in areaSets)
                {
                    var locations = areaSet.Value.locations;
                    if (locations is null) { continue; }

                    foreach (var location in locations)
                    {
                        var record = csvData[game + "-" + location.Key];
                        record.areaSetName = areaSet.Key;
                        record.region = areaSet.Value.region;
                        record.dungeon = areaSet.Value.dungeon;
                        record.logic = location.Value;
                        Debug.WriteLine("============");
                        Debug.WriteLine($"game:\t\t{record.game}");
                        Debug.WriteLine($"location:\t{record.location}");
                        Debug.WriteLine($"type:\t\t{record.type}");
                        Debug.WriteLine($"hint:\t\t{record.hint}");
                        Debug.WriteLine($"areaSetName:{record.areaSetName}");
                        Debug.WriteLine($"dungeon:\t{record.dungeon}");
                        Debug.WriteLine($"scene:\t\t{record.scene}");
                        Debug.WriteLine($"region:\t\t{record.region}");
                        Debug.WriteLine($"id:\t\t\t{record.id}");
                        Debug.WriteLine($"item:\t\t{record.item}");
                        Debug.WriteLine($"logic:\t\t{record.logic}");
                        Debug.WriteLine("============");

                    }
                }
            }
            
            //foreach (var Region in Regions)
            //{
            //    var regionName = Region.Key;
            //    var regionData = (Dictionary<object, object>)Region.Value;
            //    if (regionData.TryGetValue("locations", out var tempLocations))
            //    {
            //        var locations = (Dictionary<object, object>)tempLocations;
            //        foreach (var location in locations)
            //        {

            //        }
            //    }
            //}

        }
    }
}

// by fletch64 & iryoku
// https://raw.githubusercontent.com/OoTMM/OoTMM/master/packages/core/data/mm/world/overworld.yml