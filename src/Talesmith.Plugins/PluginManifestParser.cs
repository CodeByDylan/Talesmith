using System.Text.Json;
using System.Text.RegularExpressions;

namespace Talesmith.Plugins;

/// <summary>Turns <c>plugin.json</c> into a <see cref="PluginManifest"/>, collecting every problem into one readable error.</summary>
internal static partial class PluginManifestParser
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly string[] KnownProperties =
    [
        "id", "name", "version", "description", "authors", "license", "homepage", "icon", "assembly", "editorAssembly", "contractVersion",
        "minEngineVersion", "dependencies", "permissions", "extensions", "assets", "enabled"
    ];

    private static readonly string[] DependencyProperties = ["id", "version", "minimumVersion", "optional"];

    public static PluginManifest Parse(ReadOnlyMemory<byte> json, string source)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var errors = new List<string>();
            var manifest = Read(document.RootElement, errors);
            if (errors.Count > 0)
                throw new InvalidDataException($"{source} is invalid:{Environment.NewLine}  - {string.Join($"{Environment.NewLine}  - ", errors)}");
            return manifest!;
        }
    }

    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    private static PluginManifest? Read(JsonElement root, List<string> errors)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("the file must contain a JSON object.");
            return null;
        }

        RejectUnknownProperties(root, KnownProperties, "", errors);

        var id = ReadString(root, "id", errors, required: true);
        if (id is not null && !IsValidId(id))
            errors.Add($"id \"{id}\" must be lowercase letters and digits separated by '.', '-' or '_', such as \"talesmith.cutscenes\".");

        var name = ReadString(root, "name", errors, required: false);
        var version = ReadVersion(root, "version", errors) ?? new Version(1, 0, 0);
        var description = ReadString(root, "description", errors, required: false) ?? "";
        var authors = ReadStringList(root, "authors", "a list of names, such as [\"Ada Lovelace\"]", errors);
        var license = ReadString(root, "license", errors, required: false);
        var homepage = ReadHomepage(root, errors);
        var icon = ReadRelativePath(root, "icon", "an image file inside the plugin folder, such as \"icon.png\"", errors);

        var assembly = ReadAssemblyFile(root, "assembly", errors, required: true);
        var editorAssembly = ReadAssemblyFile(root, "editorAssembly", errors, required: false);
        if (assembly is not null && editorAssembly is not null && string.Equals(assembly, editorAssembly, StringComparison.OrdinalIgnoreCase))
            errors.Add("editorAssembly must be a separate assembly from assembly, so games never load editor code.");

        var contractVersion = ReadContractVersion(root, errors);
        var minEngineVersion = ReadVersion(root, "minEngineVersion", errors);
        var dependencies = ReadDependencies(root, id, errors);
        var permissions = ReadPermissions(root, errors);
        var extensions = ReadExtensions(root, errors);
        var assets = ReadRelativePath(root, "assets", "a folder inside the plugin folder, such as \"assets\"", errors);

        var enabled = true;
        if (root.TryGetProperty("enabled", out var enabledElement))
        {
            if (enabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                enabled = enabledElement.GetBoolean();
            else
                errors.Add("enabled must be true or false.");
        }

        if (errors.Count > 0)
            return null;

        return new PluginManifest
        {
            Id = id!,
            Name = string.IsNullOrEmpty(name) ? id! : name,
            Version = version,
            Description = description,
            Authors = authors,
            License = license,
            Homepage = homepage,
            IconFile = icon,
            AssemblyFile = assembly!,
            EditorAssemblyFile = editorAssembly,
            ContractVersion = contractVersion,
            MinEngineVersion = minEngineVersion,
            Dependencies = dependencies,
            Permissions = permissions,
            Extensions = extensions,
            AssetsFolder = assets,
            Enabled = enabled
        };
    }

    private static void RejectUnknownProperties(JsonElement element, string[] known, string label, List<string> errors)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(known, property.Name) >= 0)
                continue;
            var prefix = label.Length == 0 ? "" : $"{label} has ";
            var suggestion = Suggest(property.Name, known);
            errors.Add(suggestion is null
                ? $"{prefix}unknown property \"{property.Name}\" (expected one of: {string.Join(", ", known)})."
                : $"{prefix}unknown property \"{property.Name}\"; did you mean \"{suggestion}\"?");
        }
    }

    private static string? ReadString(JsonElement parent, string property, List<string> errors, bool required, string label = "")
    {
        var name = label.Length == 0 ? property : $"{label} {property}";
        if (!parent.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            if (required)
                errors.Add($"{name} is required.");
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{name} must be a string.");
            return null;
        }

        var value = element.GetString()!.Trim();
        if (value.Length == 0 && required)
        {
            errors.Add($"{name} must not be empty.");
            return null;
        }

        return value;
    }

    private static Version? ReadVersion(JsonElement root, string property, List<string> errors)
    {
        var text = ReadString(root, property, errors, required: false);
        if (text is null)
            return null;
        if (PluginVersion.TryParse(text, out var version))
            return version;
        errors.Add($"{property} \"{text}\" must be a version such as \"1.2.0\".");
        return null;
    }

    private static List<string> ReadStringList(JsonElement root, string property, string expected, List<string> errors)
    {
        var values = new List<string>();
        if (!root.TryGetProperty(property, out var element) || element.ValueKind == JsonValueKind.Null)
            return values;
        if (element.ValueKind != JsonValueKind.Array || element.EnumerateArray().Any(a => a.ValueKind != JsonValueKind.String))
        {
            errors.Add($"{property} must be {expected}.");
            return values;
        }

        foreach (var item in element.EnumerateArray())
            values.Add(item.GetString()!.Trim());
        return values;
    }

    private static Uri? ReadHomepage(JsonElement root, List<string> errors)
    {
        var text = ReadString(root, "homepage", errors, required: false);
        if (string.IsNullOrEmpty(text))
            return null;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            return uri;
        errors.Add($"homepage \"{text}\" must be an http or https address, such as \"https://example.com/my-plugin\".");
        return null;
    }

    private static string? ReadRelativePath(JsonElement root, string property, string expected, List<string> errors)
    {
        var text = ReadString(root, property, errors, required: false);
        if (string.IsNullOrEmpty(text))
            return null;

        var segments = text.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var valid = !Path.IsPathRooted(text) && !text.Contains(':', StringComparison.Ordinal) && segments.Length > 0 &&
                    segments.All(s => s is not ("." or "..") && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        if (!valid)
        {
            errors.Add($"{property} \"{text}\" must be {expected}, without \"..\" or a drive or root.");
            return null;
        }

        return string.Join('/', segments);
    }

    private static string? ReadAssemblyFile(JsonElement root, string property, List<string> errors, bool required)
    {
        var value = ReadString(root, property, errors, required);
        if (string.IsNullOrEmpty(value))
            return null;
        if (IsAssemblyFileName(value))
            return value;
        errors.Add($"{property} \"{value}\" must be the file name of a .dll in the plugin folder, such as \"MyPlugin.dll\".");
        return null;
    }

    private static int ReadContractVersion(JsonElement root, List<string> errors)
    {
        if (!root.TryGetProperty("contractVersion", out var element))
        {
            errors.Add($"contractVersion is required (this engine uses {EngineInfo.ContractVersion}).");
            return 0;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value) || value < 1)
        {
            errors.Add($"contractVersion must be a whole number such as {EngineInfo.ContractVersion}.");
            return 0;
        }

        return value;
    }

    private static List<PluginDependency> ReadDependencies(JsonElement root, string? ownId, List<string> errors)
    {
        var dependencies = new List<PluginDependency>();
        if (!root.TryGetProperty("dependencies", out var element) || element.ValueKind == JsonValueKind.Null)
            return dependencies;
        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add("dependencies must be a list such as [{ \"id\": \"talesmith.dialogue\", \"version\": \"^1.0\" }].");
            return dependencies;
        }

        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var label = $"dependencies[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{label} must be an object with an id, an optional version range and an optional \"optional\" flag.");
                continue;
            }

            RejectUnknownProperties(item, DependencyProperties, label, errors);
            var id = ReadString(item, "id", errors, required: true, label);
            if (id is null)
                continue;
            if (!IsValidId(id))
                errors.Add($"{label} id \"{id}\" is not a valid plugin id.");
            if (id == ownId)
                errors.Add("a plugin cannot depend on itself.");

            var range = ReadRange(item, label, errors);
            var optional = false;
            if (item.TryGetProperty("optional", out var optionalElement))
            {
                if (optionalElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    optional = optionalElement.GetBoolean();
                else
                    errors.Add($"{label} optional must be true or false.");
            }

            if (dependencies.Exists(d => d.Id == id))
                errors.Add($"dependency \"{id}\" is listed more than once.");
            else if (range is not null)
                dependencies.Add(new PluginDependency(id, range, optional));
        }

        return dependencies;
    }

    private static VersionRange? ReadRange(JsonElement item, string label, List<string> errors)
    {
        var rangeText = ReadString(item, "version", errors, required: false, label);
        var minimumText = ReadString(item, "minimumVersion", errors, required: false, label);
        if (rangeText is not null && minimumText is not null)
        {
            errors.Add($"{label} has both version and minimumVersion; use only version, such as \">={minimumText}\".");
            return null;
        }

        if (minimumText is not null)
        {
            if (PluginVersion.TryParse(minimumText, out var minimum))
                return VersionRange.AtLeast(minimum);
            errors.Add($"{label} minimumVersion \"{minimumText}\" must be a version such as \"1.0.0\".");
            return null;
        }

        if (rangeText is null)
            return VersionRange.Any;
        if (VersionRange.TryParse(rangeText, out var range, out var error))
            return range;
        errors.Add($"{label} version: {error}");
        return null;
    }

    private static PluginPermissions ReadPermissions(JsonElement root, List<string> errors)
    {
        var permissions = PluginPermissions.None;
        foreach (var name in ReadStringList(root, "permissions", $"a list of permission names ({string.Join(", ", PluginPermissionNames.All)})", errors))
        {
            if (!PluginPermissionNames.TryParse(name, out var permission))
            {
                var suggestion = Suggest(name, PluginPermissionNames.All);
                errors.Add(suggestion is null
                    ? $"permission \"{name}\" is unknown (expected one of: {string.Join(", ", PluginPermissionNames.All)})."
                    : $"permission \"{name}\" is unknown; did you mean \"{suggestion}\"?");
            }
            else if ((permissions & permission) != 0)
            {
                errors.Add($"permission \"{name}\" is listed more than once.");
            }

            permissions |= permission;
        }

        return permissions;
    }

    private static List<string> ReadExtensions(JsonElement root, List<string> errors)
    {
        var extensions = new List<string>();
        foreach (var id in ReadStringList(root, "extensions", "a list of extension point ids, such as [\"systems\", \"editor.panels\"]", errors))
        {
            if (!IsValidId(id))
                errors.Add($"extension \"{id}\" must be lowercase letters and digits separated by '.', '-' or '_', such as \"editor.panels\".");
            else if (extensions.Contains(id))
                errors.Add($"extension \"{id}\" is listed more than once.");
            else
                extensions.Add(id);
        }

        return extensions;
    }

    private static bool IsAssemblyFileName(string value) =>
        value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && value.Length > 4 && Path.GetFileName(value) == value &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !value.Contains('\\', StringComparison.Ordinal);

    /// <summary>The closest known name within a small edit distance, to catch typos.</summary>
    private static string? Suggest(string name, IEnumerable<string> known)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in known)
        {
            var distance = Distance(name.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return bestDistance <= Math.Max(1, Math.Min(3, name.Length / 3)) ? best : null;
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1, previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    [GeneratedRegex("^[a-z0-9]+([._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
