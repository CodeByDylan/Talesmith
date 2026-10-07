using Talesmith.Screenshots.Capture;

namespace Talesmith.Screenshots.Docs;

/// <summary>A screenshot on the documentation site, saved as <c>{section}/{Name}.light.webp</c> and <c>.dark.webp</c>.</summary>
/// <param name="Name">The file name within its section, in kebab case, such as <c>project-hub</c>.</param>
/// <param name="Scene">What to render.</param>
/// <param name="Alt">The default alt text, used when a page does not give its own.</param>
internal sealed record DocsShot(string Name, ScreenshotScene Scene, string Alt);

/// <summary>The screenshots of one documentation section, kept in their own folder and manifest.</summary>
/// <param name="Id">The section id and folder name, such as <c>guide</c>.</param>
internal sealed record DocsSection(string Id, IReadOnlyList<DocsShot> Shots);
