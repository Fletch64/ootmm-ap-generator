using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Corvus.UriTemplates.TavisApi;

namespace OoTMM.Generators;

public static class Program
{
    public static string OutputDir { get; private set; } = Path.Join(
        Directory.GetCurrentDirectory(), "Output");

    public static string StubsDir { get; private set; } = Path.Join(
        Directory.GetCurrentDirectory(), "Stubs");

    private const string User = "OoTMM";
    private const string Repo = "OoTMM";
    private const string Tag = "master";

    private const string Template =
        "https://raw.githubusercontent.com/{user}/{repo}/{tag}/";

    private static async Task Main(string[] args)
    {
        if (args.Length >= 1) { OutputDir = args[0]; }

        if (args.Length >= 2) { StubsDir = args[1]; }

        var http = new HttpClient
        {
            BaseAddress = new(
                new UriTemplate(Template)
                    .AddParameter("user", User)
                    .AddParameter("repo", Repo)
                    .AddParameter("tag", Tag)
                    .Resolve()),
        };

        await RunStepAsync(
            "Creating output directories...",
            () =>
            {
                Directory.CreateDirectory(OutputDir);
                Directory.CreateDirectory(StubsDir);
            });

        var optionsGenerator = new OptionsGenerator();
        var itemGenerator = new ItemGenerator();
        var macroGenerator = new MacroGenerator();
        var regionGenerator = new RegionGenerator();
        
        await RunStepAsync(
            "Generating options...",
            async () => await optionsGenerator.GenerateAsync(http));

        await RunStepAsync(
            "Generating items...",
            async () => await itemGenerator.GenerateAsync());

        await RunStepAsync(
            "Generating OoT macros...",
            async () =>
                await macroGenerator.GenerateOotAsync(http, itemGenerator.ItemMapOot));

        await RunStepAsync(
            "Generating OoT regions...",
            async () =>
                await regionGenerator.GenerateOotAsync(
                    http, macroGenerator.MacrosOot, itemGenerator.ItemMapOot));

        await RunStepAsync(
            "Generating MM macros...",
            async () =>
                await macroGenerator.GenerateMmAsync(http, itemGenerator.ItemMapMm));

        await RunStepAsync(
            "Generating MM regions...",
            async () =>
                await regionGenerator.GenerateMmAsync(
                    http, macroGenerator.MacrosMm, itemGenerator.ItemMapMm));

        await RunStepAsync(
            "Generating macro stubs...",
            async () => await macroGenerator.GenerateBaseStubsAsync());
    }

    private static ValueTask RunStepAsync(string message, Action action) =>
        RunStepAsync(
            message, () =>
            {
                try { action(); }
                catch (Exception e) { return ValueTask.FromException(e); }

                return ValueTask.CompletedTask;
            });

    private static async ValueTask RunStepAsync(string message, Func<ValueTask> action)
    {
        Console.Write(message);

        try { await action(); }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAILED");
            Console.Error.WriteLine(e);
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("OK");
        Console.ResetColor();
    }
}