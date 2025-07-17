using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace OoTMM.Generators;

internal class GeneratorBase
{
    public PythonWriterSettings PythonSettings { get; set; } =
        new() { LineLength = 120, TargetVersion = "py38" };

    protected ValueTask WriteGeneratedHeaderAsync(
        TextWriter writer, HttpClient client, params string[] files) =>
        WriteGeneratedHeaderAsync(writer, client, (IEnumerable<string>)files);

    protected PythonWriter CreatePythonWriter(string path) =>
        new(path, Console.Error, PythonSettings);

    protected string GetOutputPath(string path) => Path.Join(Program.OutputDir, path);

    protected string GetStubPath(string path) => Path.Join(Program.StubsDir, path);

    protected async ValueTask WriteGeneratedHeaderAsync(
        TextWriter writer, HttpClient client, IEnumerable<string> files)
    {
        var filesArray = files.Order().ToArray();
        if (filesArray.Length == 0)
        {
            throw new ArgumentException(
                "At least one file must be provided",
                nameof(files));
        }

        await writer.WriteLineAsync(
            $"""
             # -----------------------------------------------------------------------------
             # This file was auto-generated from the following files from the OoTMM project:
             #
             # {client.BaseAddress}
             #    {string.Join("\n#    ", filesArray)}
             # -----------------------------------------------------------------------------
             """);
        await writer.WriteLineAsync();
    }
}