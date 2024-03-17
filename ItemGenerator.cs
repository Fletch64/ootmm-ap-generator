using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OoTMM.Generators;

class ItemData
{
    public string Id { get; set; }
    public object Item { get; set; }
    public string Type { get; set; }
    public object Add { get; set; }
    public object Flags { get; set; }
    public object Draw { get; set; }
    public object Object { get; set; }
    public string Name { get; set; }
}

class ItemGenerator
{
    string urlBase = "https://raw.githubusercontent.com/OoTMM/OoTMM/master";
    string ItemsUrl = "/packages/data/src/defs/gi.yml";

    public async Task<List<ItemData>> GetItems()
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var yamlData = await DownloadFileAsync(new Uri(urlBase + ItemsUrl));
        return deserializer.Deserialize<List<ItemData>>(yamlData);
        // return deserializer.Deserialize<List<ItemData>>("- { id: OOT_BOMBS_5, item: OOT_BOMBS_5, type: MINOR, add: [OOT_BOMBS, 5], flags: 0x59, draw: BOMB, object: [oot, 0x00ce], name: \"<C0>5 Bombs\" }");
    }

    private static async Task<string> DownloadFileAsync(Uri url)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(url);
        return await response.Content.ReadAsStringAsync();
    }
}
