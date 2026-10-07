using System.Text.RegularExpressions;

namespace Talesmith.Screenshots.Docs;

/// <summary>Every documentation section and its screenshots. Add a screenshot to its section's list, such as <see cref="GuideShots"/>.</summary>
internal static partial class DocsCatalog
{
    public static IReadOnlyList<DocsSection> Sections { get; } = Validate(
    [
        new("home", HomeShots.All),
        new("guide", GuideShots.All),
        new("scripting", ScriptingShots.All),
        new("plugins", PluginsShots.All),
        new("developers", DevelopersShots.All),
    ]);

    /// <summary>Gets the name pages use, such as <c>guide/project-hub</c>.</summary>
    public static string FullName(DocsSection section, DocsShot shot) => $"{section.Id}/{shot.Name}";

    private static IReadOnlyList<DocsSection> Validate(IReadOnlyList<DocsSection> sections)
    {
        foreach (var section in sections)
        {
            foreach (var shot in section.Shots)
            {
                if (!KebabCase().IsMatch(shot.Name))
                    throw new InvalidOperationException($"The screenshot name '{FullName(section, shot)}' must be kebab case, such as 'project-hub'.");
                if (string.IsNullOrWhiteSpace(shot.Alt))
                    throw new InvalidOperationException($"The screenshot '{FullName(section, shot)}' needs alt text.");
            }

            if (section.Shots.GroupBy(s => s.Name).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
                throw new InvalidOperationException($"The screenshot name '{section.Id}/{duplicate.Key}' is used twice.");
        }

        return sections;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();
}
