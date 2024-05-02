using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;

namespace OoTMM.Generators;

internal class LocationGenerator : GeneratorBase
{
    private const ulong OoTOffset = 0;
    private const ulong MMOffset = 3000;

    public static async ValueTask GenerateAsync(HttpClient http, ulong baseId)
    {
        await GenerateOoTAsync(http, baseId);
        await GenerateMMAsync(http, baseId);
    }

    private static async ValueTask GenerateOoTAsync(HttpClient http, ulong baseId)
    {
        await using var writer = CreatePythonWriter("Output/LocationsOoT.py");
        await GenerateAsync(
            writer,
            http,
            baseId + OoTOffset,
            "OoT",
            "packages/data/src/pool/pool_oot.csv"
        );
    }

    private static async ValueTask GenerateMMAsync(HttpClient http, ulong baseId)
    {
        await using var writer = CreatePythonWriter("Output/LocationsMM.py");
        await GenerateAsync(
            writer,
            http,
            baseId + MMOffset,
            "MM",
            "packages/data/src/pool/pool_mm.csv"
        );
    }

    private static async ValueTask GenerateAsync(
        PythonWriter writer,
        HttpClient client,
        ulong baseId,
        string game,
        string file
    )
    {
        await WriteGeneratedHeaderAsync(writer, client, file);

        var type = $"{game}LocationData";
        var data = $"{game.ToLowerInvariant()}_location_data";
        var locations = $"{game.ToLowerInvariant()}_locations";

        await writer.WriteLineAsync(
            $"""
            import typing
            import OoTMMLocationData


            class {type}(OoTMMLocationData):
                def __init__(id, name, type, vanilla_item):
                    super().__init__(id, "{game}", name, type, vanilla_item)

            {data} : list[{type}] = [
            """
        );
        writer.Indent++;

        using var stream = await client.GetStreamAsync(file);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        using var deserializer = new CsvReader(
            reader,
            new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                TrimOptions = TrimOptions.Trim,
            }
        );

        var id = baseId;

        await foreach (var record in deserializer.GetRecordsAsync<dynamic>())
        {
            await writer.WriteLineAsync(
                $"{type}({id++}, \"{record.location}\", \"{record.type}\", \"{record.item}\"),"
            );
        }

        writer.Indent--;
        await writer.WriteLineAsync($"]");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync();

        await writer.WriteLineAsync(
            $"{locations} = {{location.name: location for location in {data}}}"
        );
    }
}
