using Avalonia.Media;

namespace Talesmith.Editor.Projects.Templates;

/// <summary>A starting point for new projects, listed in the hub.</summary>
/// <remarks>Register implementations as <see cref="IProjectTemplate"/> singletons in the application's services.</remarks>
public interface IProjectTemplate
{
    /// <summary>A stable identifier such as "platformer".</summary>
    string Id { get; }

    string Name { get; }

    string Description { get; }

    /// <summary>Short labels shown on the template's card, such as "Hex grid" or "Physics".</summary>
    IReadOnlyList<string> Tags { get; }

    Geometry Icon { get; }

    /// <summary>The two colors of the card's artwork gradient.</summary>
    (Color From, Color To) Colors { get; }

    /// <summary>Lower orders are listed first.</summary>
    int Order { get; }

    /// <summary>Writes the template's files; the folder structure already exists.</summary>
    Task CreateAsync(ProjectScaffold scaffold, CancellationToken cancellationToken);
}
