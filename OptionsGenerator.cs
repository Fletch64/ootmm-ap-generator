using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.ClearScript;
using OoTMM.Generators;

internal partial class OptionsGenerator : GeneratorBase
{
    private static readonly HashSet<string> Exclude =
    [
        "mode",
        "players",
        "distinctWorlds",
        "logic",
        "generateSpoilerLog",
        "noPlandoHints",
    ];

    private static readonly Dictionary<string, string> Overrides =
        new()
        {
            ["triforceGoal"] = """
                {
                    key: 'triforceGoal',
                    name: 'Triforce Goal',
                    category: 'main',
                    type: 'number',
                    description: 'The amount of Triforce Pieces that are required to win.',
                    default: 20,
                    cond: (s: any) => s.goal === 'triforce',
                    min: 1,
                    max: 999,
                }
                """,
        };

    public static async ValueTask GenerateAsync(HttpClient http)
    {
        await using var writer = CreatePythonWriter("Output/Options.py");
        await GenerateAsync(writer, http, "packages/core/lib/combo/settings/data.ts");
    }

    private static async ValueTask GenerateAsync(
        PythonWriter writer,
        HttpClient client,
        string file
    )
    {
        var typescript = await TypeScript.CreateAsync(client);

        var root = new Category();
        root.Extra.Add(("death_link", "DeathLink"));

        var source = await client.GetStringAsync(file);
        var exports = typescript.EvaluateModule(source);
        var settings = ((IList<dynamic>)exports.SETTINGS)
            .Cast<dynamic>()
            .Where(Included)
            .Select(s => CheckOverride(s, typescript));
        root.AddRange(settings);

        await WriteGeneratedHeaderAsync(writer, client, file);
        await writer.WriteLineAsync(
            """
            import typing
            from Options import Option, DefaultOnToggle, Toggle, Range, OptionList, OptionSet, DeathLink

            class OoTMMChoice(Choice):
                display_names: list[str] = []

                @classmethod
                def get_option_name(cls, value: int) -> str:
                    if value in display_names:
                        return display_names[value]
                    return super().get_option_name(value)

            """
        );
        await WriteCategoryAsync(writer, root);
    }

    private static bool Included(dynamic setting) =>
        !Exclude.Contains(setting.key) && setting.category != "entrances";

    private static dynamic CheckOverride(dynamic setting, TypeScript typescript) =>
        Overrides.TryGetValue((string)setting.key, out var o)
            ? typescript.Evaluate(o)
            : setting;

    private static async ValueTask WriteCategoryAsync(
        PythonWriter writer,
        Category category
    )
    {
        var name = category.Name;
        if (name is not null)
        {
            var header = $"# {new string('=', name.Length + 10)}";
            await writer.WriteLineAsync($"# category: {name}");
            await writer.WriteLineAsync();
        }
        name ??= "ootmm";

        foreach (var setting in category.Settings)
        {
            await WriteSettingAsync(writer, setting);
        }

        foreach (var subcategory in category.Categories)
        {
            await WriteCategoryAsync(writer, subcategory);
        }

        await writer.WriteLineAsync(
            $"{name.Replace('.', '_')}_options: typing.Dict[str, type(Option)] = {{"
        );
        writer.Indent++;

        foreach (var setting in category.Settings)
        {
            await writer.WriteLineAsync(
                $"\"{ToIdentifier(setting.key)}\": {Type(setting)},"
            );
        }

        foreach (var subcategory in category.Categories)
        {
            var subName = subcategory.Name ?? "ootmm";
            await writer.WriteLineAsync($"**{subName.Replace('.', '_')}_options,");
        }

        foreach (var (key, type) in category.Extra)
        {
            await writer.WriteLineAsync($"\"{key}\": {type},");
        }

        writer.Indent--;
        await writer.WriteLineAsync("}");
        await writer.WriteLineAsync();
    }

    private static async ValueTask WriteSettingAsync(
        PythonWriter writer,
        dynamic setting
    )
    {
        await writer.WriteLineAsync($"class {Type(setting)}({BaseType(setting)}):");
        writer.Indent++;
        await WriteDescriptionAsync(writer, setting);
        await writer.WriteLineAsync($"display_name = \"{setting.name}\"");
        await writer.WriteLineAsync();
        await (
            setting["type"] switch
            {
                "enum" => WriteChoiceAsync(writer, setting),
                "set" => WriteOptionSetAsync(writer, setting),
                "number" => WriteRangeAsync(writer, setting),
                _ => ValueTask.CompletedTask,
            }
        );
        await WriteConditionAsync(writer, setting);
        await writer.WriteLineAsync();
        writer.Indent--;
    }

    private static string Type(dynamic setting)
    {
        var key = ((string)setting.key).AsSpan();
        var initial = char.ToUpper(key[0], CultureInfo.InvariantCulture);
        return string.Concat(new ReadOnlySpan<char>(in initial), key[1..]);
    }

    private static string BaseType(dynamic setting) =>
        setting.type switch
        {
            "boolean"
                => setting.@default switch
                {
                    true => "DefaultOnToggle",
                    false => "Toggle",
                    _ => throw new InvalidOperationException(),
                },
            "number" => "Range",
            "enum" => "OoTMMChoice",
            "set" => "OptionSet",
            _ => throw new InvalidOperationException(),
        };

    private static string ToIdentifier(string name) =>
        IdentifierPattern().Replace(name, "_$1").ToLower(CultureInfo.InvariantCulture);

    private static async ValueTask WriteDescriptionAsync(
        PythonWriter writer,
        dynamic setting
    )
    {
        var description = new List<string>();
        if (setting.description is string text)
        {
            description.Add(text);
        }
        var values = setting.type switch
        {
            "enum"
                => (setting.values as IEnumerable<dynamic> ?? [])
                    .Where(value => value.description is not Undefined)
                    .Select(value => $"{value.name}: {value.description}"),
            "set"
                => (setting.values as IEnumerable<dynamic> ?? []).Select(value =>
                    $"{value.value.ToLowerInvariant()}: {value.name}"
                ),
            _ => [],
        };
        if (values.Any())
        {
            description.Add(string.Empty);
            description.AddRange(values);
        }

        var lines = description
            .SelectMany(s => s.Split('\n'))
            .SelectMany(s => s.Split("<br>"))
            .Select(s => s.TrimEnd());

        if (lines.Any())
        {
            await writer.WriteLineAsync("\"\"\"");
            var last = default(string);
            foreach (var line in lines)
            {
                if (line != last)
                {
                    last = line;
                    await writer.WriteLineAsync(line);
                }
            }
            await writer.WriteLineAsync("\"\"\"");
            await writer.WriteLineAsync();
        }
    }

    private static async ValueTask WriteRangeAsync(PythonWriter writer, dynamic setting)
    {
        await writer.WriteLineAsync($"range_start = {Check(setting.min)}");
        await writer.WriteLineAsync($"range_end = {Check(setting.max)}");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"default = {Check(setting.@default)}");
        await writer.WriteLineAsync();

        static object Check(dynamic value)
        {
            if (value is not int)
            {
                throw new InvalidOperationException(
                    "Dynamic settings are not supported by Archipelago."
                );
            }
            return value;
        }
    }

    private static async ValueTask WriteChoiceAsync(
        PythonWriter writer,
        dynamic setting
    )
    {
        var values = (setting.values as IEnumerable<dynamic> ?? []).Select(
            (value, index) =>
                new
                {
                    Index = index,
                    Identifier = (string)value.value switch
                    {
                        "random" => "randomized",
                        string name => ToIdentifier(name),
                    },
                    Name = (string)value.name,
                }
        );

        if (values.Any())
        {
            foreach (var value in values)
            {
                await writer.WriteLineAsync(
                    $"option_{value.Identifier} = {value.Index}"
                );
            }
            await writer.WriteLineAsync();

            var defaultName = setting.@default as string;
            var defaultIndex = values
                .Where(value => value.Name == defaultName)
                .Select(value => value.Index)
                .SingleOrDefault();
            await writer.WriteLineAsync($"default = {defaultIndex}");
            await writer.WriteLineAsync();

            await writer.WriteLineAsync($"display_names = [");
            writer.Indent++;
            foreach (var value in values)
            {
                await writer.WriteLineAsync($"\"{value.Name}\",");
            }
            writer.Indent--;
            await writer.WriteLineAsync($"]");
            await writer.WriteLineAsync();
        }
    }

    private static async ValueTask WriteOptionSetAsync(
        PythonWriter writer,
        dynamic setting
    )
    {
        var valid_keys = setting.values as IEnumerable<dynamic> ?? [];
        await writer.WriteLineAsync("valid_keys = {");
        writer.Indent++;
        foreach (var key in valid_keys)
        {
            await writer.WriteLineAsync($"\"{key.value.ToLowerInvariant()}\",");
        }
        writer.Indent--;
        await writer.WriteLineAsync("}");
        await writer.WriteLineAsync();
    }

    private static async ValueTask WriteConditionAsync(
        PythonWriter writer,
        dynamic setting
    )
    {
        if (setting.cond is not Undefined)
        {
            await writer.WriteLineAsync($"# condition: {setting.cond.toString()}");
            await writer.WriteLineAsync();
        }
    }

    [GeneratedRegex("(?<![A-Z])\\B([A-Z]+)")]
    private static partial Regex IdentifierPattern();

    private class Category(string? name = null)
    {
        private readonly Dictionary<string, Category> map = [];

        public string? Name { get; } = name;

        public IList<Category> Categories { get; } = [];

        public IList<dynamic> Settings { get; } = [];

        public IList<(string key, string type)> Extra { get; } = [];

        public Category Get(string? name)
        {
            var result = this;
            if (name is not null)
            {
                foreach (var part in name.Split('.'))
                {
                    if (!result.map.TryGetValue(part, out var category))
                    {
                        category = new(
                            result.Name is null ? part : $"{result.Name}.{part}"
                        );
                        result.map.Add(part, category);
                        result.Categories.Add(category);
                    }
                    result = category;
                }
            }
            return result;
        }

        public void Add(dynamic setting) =>
            Get(setting.category as string).Settings.Add(setting);

        public void AddRange(IEnumerable<dynamic> settings)
        {
            foreach (var setting in settings)
            {
                Add(setting);
            }
        }
    }
}
