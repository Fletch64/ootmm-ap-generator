using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.ClearScript;

namespace OoTMM.Generators;

internal partial class OptionsGenerator : GeneratorBase
{
    private readonly HashSet<string> exclude =
    [
        "distinctWorlds", "generateSpoilerLog", "logic", "mode", "noPlandoHints",
        "players", "teams",
    ];

    private readonly Dictionary<string, string> overrides =
        new()
        {
            ["triforceGoal"] =
                """
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

    private readonly Dictionary<string, string> groupNames =
        new()
        {
            ["main"] = "Game Options",
            ["main.shuffle"] = "Shuffle Options",
            ["main.prices"] = "Price Options",
            ["main.events"] = "Event Options",
            ["main.cross"] = "Cross-Game Options",
            ["main.world"] = "World Options",
            // Special Conditions
            ["main.misc"] = "Misc. Options",
            ["hints"] = "Hints",
            ["items.extensions"] = "Item Extensions",
            ["items.progressive"] = "Progressive Items",
            ["items.shared"] = "Shared Items",
            ["items.ageless"] = "Ageless Items",
            // Logic Tricks
            // Logic Glitches
            // Advanced
        };

    public async ValueTask GenerateAsync(HttpClient http)
    {
        await using var writer = CreatePythonWriter(GetOutputPath("Options.py"));
        await GenerateAsync(writer, http, "packages/core/lib/combo/settings/data.ts");
    }

    private async ValueTask GenerateAsync(
        PythonWriter writer,
        HttpClient client,
        string file)
    {
        var typescript = await TypeScript.CreateAsync(client);

        var root = new Category();

        var source = await client.GetStringAsync(file);
        var exports = typescript.EvaluateModule(source);
        var settings = ((IList<dynamic>)exports.SETTINGS)
            .Where(Included)
            .Select(s => CheckOverride(s, typescript));
        root.AddRange(settings);
        root.Get("main").Extra.Add(("death_link", "DeathLink"));

        await WriteGeneratedHeaderAsync(writer, client, file);
        await writer.WriteLineAsync(
            """
            from dataclasses import dataclass
            from Options import (
                Choice,
                DeathLink,
                DefaultOnToggle,
                OptionGroup,
                OptionSet,
                PerGameCommonOptions,
                Range,
                Toggle,
            )

            class OoTMMChoice(Choice):
                display_names: list[str] = []
            
                @classmethod
                def get_option_name(cls, value: int) -> str:
                    if value < len(cls.display_names):
                        return cls.display_names[value]
                    return super().get_option_name(value)

            """);
        await WriteCategoryAsync(writer, root);
        await WriteDataClassAsync(writer, root);
        await WriteOptionGroupsAsync(writer, root);
    }

    private bool Included(dynamic setting) =>
        !exclude.Contains(setting.key) && setting.category != "entrances";

    private dynamic CheckOverride(dynamic setting, TypeScript typescript) =>
        overrides.TryGetValue((string)setting.key, out var o)
            ? typescript.Evaluate(o)
            : setting;

    private async ValueTask WriteCategoryAsync(
        PythonWriter writer,
        Category category)
    {
        var name = category.Name;
        if (name is not null)
        {
            // var header = $"# {new string('=', name.Length + 10)}";
            await writer.WriteLineAsync($"# group: {name}");
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

        await writer.WriteLineAsync();
    }

    private async ValueTask WriteDataClassAsync(PythonWriter writer, Category root)
    {
        await writer.WriteLineAsync(
            """
            @dataclass
            class OoTMMOptions(PerGameCommonOptions):
            """);
        writer.Indent++;
        await WriteDataClassMembersAsync(writer, root);
        writer.Indent--;
    }

    private async ValueTask WriteDataClassMembersAsync(
        PythonWriter writer, Category category)
    {
        var name = category.Name;
        if (name is not null && category.Settings.Count + category.Extra.Count > 0)
        {
            if (name is not "main") { await writer.WriteLineAsync(); }

            await writer.WriteLineAsync($"# group: {name}");
        }

        foreach (var setting in category.Settings)
        {
            await writer.WriteLineAsync(
                $"{ToIdentifier(setting.key)}: {Type(setting)}");
        }

        foreach (var (key, type) in category.Extra)
        {
            await writer.WriteLineAsync($"{key}: {type}");
        }

        foreach (var subcategory in category.Categories)
        {
            await WriteDataClassMembersAsync(writer, subcategory);
        }
    }

    private async ValueTask WriteSettingAsync(
        PythonWriter writer,
        dynamic setting)
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

    private IEnumerable<Category> Flatten(Category category) =>
        Enumerable
            .Repeat(category, 1)
            .Concat(category.Categories.SelectMany(Flatten));

    private async ValueTask WriteOptionGroupsAsync(
        PythonWriter writer,
        Category category)
    {
        await writer.WriteLineAsync($"ootmm_option_groups: list[OptionGroup] = [");
        writer.Indent++;

        foreach (var c in Flatten(category))
        {
            if (
                string.IsNullOrWhiteSpace(c.Name)
                || !c.Settings.Any() && !c.Extra.Any()
            ) { continue; }

            var name = groupNames.GetValueOrDefault(c.Name, c.Name);

            await writer.WriteLineAsync($"OptionGroup(\"{name}\", [");
            writer.Indent++;

            foreach (var s in c.Settings)
            {
                await writer.WriteLineAsync($"{Type(s)},");
            }

            foreach (var (_, type) in c.Extra)
            {
                await writer.WriteLineAsync($"{type},");
            }

            writer.Indent--;
            await writer.WriteLineAsync($"]),");
        }

        writer.Indent--;
        await writer.WriteLineAsync($"]");
    }

    private string Type(dynamic setting)
    {
        var key = ((string)setting.key).AsSpan();
        var initial = char.ToUpper(key[0], CultureInfo.InvariantCulture);
        return string.Concat(new ReadOnlySpan<char>(in initial), key[1..]);
    }

    private string BaseType(dynamic setting) =>
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

    private string ToIdentifier(string name) =>
        IdentifierPattern().Replace(name, "_$1").ToLower(CultureInfo.InvariantCulture);

    private async ValueTask WriteDescriptionAsync(
        PythonWriter writer,
        dynamic setting)
    {
        var segments = new List<string>();
        if (setting.description is string text) { segments.Add(text); }
        else { segments.Add(setting.name); }

        segments.Add(string.Empty);
        segments.AddRange(
            setting.type switch
            {
                "enum" => (setting.values as IEnumerable<dynamic> ?? [])
                    .Where(value => value.description is not Undefined)
                    .Select(value => $"{value.name}: {value.description}"),
                "set" => (setting.values as IEnumerable<dynamic> ?? [])
                    .Select(value => $"{value.value.ToLowerInvariant()}: {value.name}"),
                _ => [],
            });

        var lines = segments.SelectMany(s => NewLinePattern().Split(s)).ToList();
        if (lines.Count is 0) { return; }

        var last = default(string);
        var empty = true;
        foreach (var line in segments)
        {
            if (line != last && line is not "")
            {
                switch (last)
                {
                    case null: await writer.WriteLineAsync("\"\"\""); break;
                    case "": await writer.WriteLineAsync(string.Empty); break;
                }

                await writer.WriteLineAsync(line);
                empty = false;
            }

            last = line;
        }

        if (!empty) { await writer.WriteLineAsync("\"\"\""); }

        await writer.WriteLineAsync();
    }

    private async ValueTask WriteRangeAsync(PythonWriter writer, dynamic setting)
    {
        await writer.WriteLineAsync($"range_start = {Check(setting.min)}");
        await writer.WriteLineAsync($"range_end = {Check(setting.max)}");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync($"default = {Check(setting.@default)}");
        await writer.WriteLineAsync();
        return;

        static object Check(dynamic value)
        {
            if (value is not int)
            {
                throw new InvalidOperationException(
                    "Dynamic settings are not supported by Archipelago.");
            }

            return value;
        }
    }

    private async ValueTask WriteChoiceAsync(
        PythonWriter writer,
        dynamic setting)
    {
        var values = (setting.values as IEnumerable<dynamic> ?? [])
            .Select(
                (value, index) => (
                    Index: index,
                    Identifier: (string)value.value switch
                    {
                        "random" => "randomized",
                        var name => ToIdentifier(name),
                    },
                    Name: (string)value.name
                ))
            .ToArray();

        if (values.Length != 0)
        {
            foreach (var value in values)
            {
                await writer.WriteLineAsync(
                    $"option_{value.Identifier} = {value.Index}");
            }

            await writer.WriteLineAsync();
            var defaultName = setting.@default as string;
            var defaultIndex = values
                .Where(value => value.Name == defaultName)
                .Select(value => value.Index)
                .SingleOrDefault();
            await writer.WriteLineAsync($"default = {defaultIndex}");
            await writer.WriteLineAsync();

            await writer.WriteLineAsync("display_names = [");
            writer.Indent++;

            foreach (var value in values)
            {
                await writer.WriteLineAsync($"\"{value.Name}\",");
            }

            writer.Indent--;
            await writer.WriteLineAsync("]");
            await writer.WriteLineAsync();
        }
    }

    private async ValueTask WriteOptionSetAsync(
        PythonWriter writer,
        dynamic setting)
    {
        await writer.WriteLineAsync("valid_keys = {");
        writer.Indent++;

        var validKeys = setting.values as IEnumerable<dynamic> ?? [];
        foreach (var key in validKeys)
        {
            await writer.WriteLineAsync($"\"{key.value.ToLowerInvariant()}\",");
        }

        writer.Indent--;
        await writer.WriteLineAsync("}");
        await writer.WriteLineAsync();
    }

    private async ValueTask WriteConditionAsync(
        PythonWriter writer,
        dynamic setting)
    {
        if (setting.cond is not Undefined)
        {
            await writer.WriteLineAsync($"# condition: {setting.cond.toString()}");
            await writer.WriteLineAsync();
        }
    }

    [GeneratedRegex("(?<![A-Z])\\B([A-Z]+)")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("\r?\n|<br\\s*/?>")]
    private static partial Regex NewLinePattern();

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
            if (name is null) { return result; }

            foreach (var part in name.Split('.'))
            {
                if (!result.map.TryGetValue(part, out var category))
                {
                    category = new(
                        result.Name is null ? part : $"{result.Name}.{part}");
                    result.map.Add(part, category);
                    result.Categories.Add(category);
                }

                result = category;
            }

            return result;
        }

        private void Add(dynamic setting) =>
            Get(setting.category as string).Settings.Add(setting);

        public void AddRange(IEnumerable<dynamic> settings)
        {
            foreach (var setting in settings) { Add(setting); }
        }
    }
}