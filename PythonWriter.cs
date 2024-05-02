using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OoTMM.Generators;

public partial class PythonWriter : TextWriter
{
    private readonly Process process;
    private readonly TextWriter writer;
    private readonly TextWriter? disposable;
    private bool newLine = true;

    public override Encoding Encoding { get; } = new UTF8Encoding(false);

    public override IFormatProvider FormatProvider => writer.FormatProvider;

    public int Indent { get; set; }

    public PythonWriter(string path, PythonWriterSettings? settings = null)
        : this(path, TextWriter.Null, settings) { }

    public PythonWriter(
        string path,
        TextWriter error,
        PythonWriterSettings? settings = null
    )
        : this(new StreamWriter(path), error, path, true, settings) { }

    public PythonWriter(Stream stream, PythonWriterSettings? settings = null)
        : this(stream, TextWriter.Null, settings) { }

    public PythonWriter(
        Stream stream,
        TextWriter error,
        PythonWriterSettings? settings = null
    )
        : this(
            new StreamWriter(
                stream,
                encoding: new UTF8Encoding(false),
                leaveOpen: true
            ),
            error,
            null,
            true,
            settings
        ) { }

    public PythonWriter(TextWriter output, PythonWriterSettings? settings = null)
        : this(output, TextWriter.Null, settings) { }

    public PythonWriter(
        TextWriter output,
        TextWriter error,
        PythonWriterSettings? settings = null
    )
        : this(output, error, null, false, settings) { }

    private PythonWriter(
        TextWriter output,
        TextWriter error,
        string? path,
        bool closeOutput,
        PythonWriterSettings? settings = null
    )
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        disposable = closeOutput ? output : null;
        settings ??= new();

        var start = new ProcessStartInfo()
        {
            FileName = "black",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding,
            StandardOutputEncoding = Encoding,
            StandardErrorEncoding = Encoding,
        };
        foreach (var arg in GetArguments(settings, path))
        {
            start.ArgumentList.Add(arg);
        }
        start.ArgumentList.Add("-");

        process = new() { StartInfo = start };

        process.OutputDataReceived += (s, e) => output.WriteLine(e.Data);
        process.ErrorDataReceived += (s, e) =>
        {
            var line = UnicodeEscape()
                .Replace(
                    e.Data ?? "",
                    match =>
                    {
                        var hex = match.Groups[1].Success
                            ? match.Groups[1].Value
                            : match.Groups[2].Value;
                        var codePoint = int.Parse(hex, NumberStyles.HexNumber);
                        return char.ConvertFromUtf32(codePoint);
                    }
                );
            error.WriteLine(line);
        };

        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        writer = process.StandardInput;
        writer.NewLine = "\n";
    }

    public override void Write(char value)
    {
        if (newLine)
        {
            newLine = false;
            for (int i = Indent * 4; i > 0; i--)
            {
                writer.Write(' ');
            }
        }
        writer.Write(value);
        if (value == '\n')
        {
            newLine = true;
        }
    }

    public override async Task WriteAsync(char value)
    {
        if (newLine)
        {
            newLine = false;
            for (int i = Indent * 4; i > 0; i--)
            {
                await writer.WriteAsync(' ');
            }
        }
        await writer.WriteAsync(value);
        if (value == '\n')
        {
            newLine = true;
        }
    }

    public sealed override async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        await writer.DisposeAsync();
        await process.WaitForExitAsync();
        await (disposable?.DisposeAsync() ?? ValueTask.CompletedTask);
        process.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            writer.Dispose();
            process.WaitForExit();
            disposable?.Dispose();
            process.Dispose();
        }
    }

    private static IEnumerable<string> GetArguments(
        PythonWriterSettings settings,
        string? path
    )
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.LineLength);

        if (settings.BlackVersion is string black)
        {
            yield return "--required-version";
            yield return black;
        }

        var file = settings.File ?? Path.GetFileName(path);
        if (file is not null)
        {
            yield return "--stdin-filename";
            yield return file;
        }

        if (settings.LineLength is > 0)
        {
            yield return "--line-length";
            yield return settings.LineLength.ToString();
        }

        if (settings.TargetVersion is string target)
        {
            yield return "--target-version";
            yield return target;
        }

        if (settings.Quiet)
        {
            yield return "--quiet";
        }

        if (settings.SkipFirstLne)
        {
            yield return "--skip-source-first-line";
        }

        if (settings.SkipStringNormalization)
        {
            yield return "--skip-string-normalization";
        }

        if (settings.SkipMagicTrailingComma)
        {
            yield return "--skip-magic-trailing-comma";
        }
    }

    [GeneratedRegex(@"\\u([0-9A-Fa-f]{4})|\\U([0-9A-Fa-f]{8})")]
    private static partial Regex UnicodeEscape();
}

public record PythonWriterSettings
{
    public string? BlackVersion { get; init; } = null;
    public string? File { get; init; } = null;
    public int LineLength { get; init; } = 0;
    public string? TargetVersion { get; init; } = null;
    public bool Quiet { get; init; } = true;
    public bool SkipFirstLne { get; init; } = false;
    public bool SkipStringNormalization { get; init; } = false;
    public bool SkipMagicTrailingComma { get; init; } = false;
}
