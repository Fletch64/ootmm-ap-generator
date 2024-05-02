using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Corvus.UriTemplates.TavisApi;

namespace OoTMM.Generators;

partial class Program
{
    private const string user = "OoTMM";
    private const string repo = "OoTMM";
    private const string tag = "master";
    private const string template =
        "https://raw.githubusercontent.com/{user}/{repo}/{tag}/";

    static async Task Main()
    {
        var baseId = 36210000UL;

        var http = new HttpClient
        {
            BaseAddress = new(
                new UriTemplate(template)
                    .AddParameter("user", user)
                    .AddParameter("repo", repo)
                    .AddParameter("tag", tag)
                    .Resolve()
            ),
        };

        Directory.CreateDirectory("Output");
        Directory.CreateDirectory("Stubs");

        await OptionsGenerator.GenerateAsync(http);
        await LocationGenerator.GenerateAsync(http, baseId);
        var (macrosOoT, macrosMM) = await LogicGenerator.GenerateAsync(http);
        await RegionGenerator.GenerateAsync(http, macrosOoT, macrosMM);
    }
}
