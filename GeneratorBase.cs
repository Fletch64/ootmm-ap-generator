using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace OoTMM.Generators;

internal class GeneratorBase
{
    public static PythonWriterSettings PythonSettings { get; set; } =
        new PythonWriterSettings { LineLength = 120, TargetVersion = "py38" };

    protected static ValueTask WriteGeneratedHeaderAsync(
        TextWriter writer,
        HttpClient client,
        params string[] files
    ) => WriteGeneratedHeaderAsync(writer, client, (IEnumerable<string>)files);

    protected static PythonWriter CreatePythonWriter(string path) =>
        new(path, Console.Error, PythonSettings);

    protected static async ValueTask WriteGeneratedHeaderAsync(
        TextWriter writer,
        HttpClient client,
        IEnumerable<string> files
    )
    {
        if (!files.Any())
        {
            throw new ArgumentException(
                "At least one file must be provided",
                nameof(files)
            );
        }

        await writer.WriteLineAsync(
            $"""
            # -----------------------------------------------------------------------------
            # This file was auto-generated from the following files from the OoTMM project:
            #
            # {client.BaseAddress}
            #    {string.Join("\n#    ", files.Order())}
            # -----------------------------------------------------------------------------
            """
        );
        await writer.WriteLineAsync();
    }
}
