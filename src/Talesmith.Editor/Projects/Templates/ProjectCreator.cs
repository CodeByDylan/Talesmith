namespace Talesmith.Editor.Projects.Templates;

/// <summary>Creates project folders from templates.</summary>
public sealed class ProjectCreator(IEnumerable<IProjectTemplate> templates)
{
    public IReadOnlyList<IProjectTemplate> Templates { get; } = [.. templates.OrderBy(t => t.Order)];

    /// <summary>Turns a project name into a folder name: letters, digits, spaces, dashes and underscores.</summary>
    public static string ToFolderName(string name)
    {
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '-').ToArray();
        var folder = new string(chars).Trim(' ', '-');
        return folder.Length == 0 ? "New Project" : folder;
    }

    /// <summary>Why a project cannot be created at a location, or null when it can.</summary>
    public static string? Validate(string parentFolder, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a name for the project.";
        if (string.IsNullOrWhiteSpace(parentFolder))
            return "Choose where to create the project.";
        var folder = Path.Combine(parentFolder, ToFolderName(name));
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            return $"The folder \"{folder}\" already exists and is not empty.";
        return null;
    }

    /// <summary>Creates a project in a new folder named after the project inside <paramref name="parentFolder"/>; returns the project folder.</summary>
    /// <exception cref="IOException">The folder exists and is not empty, or files cannot be written.</exception>
    public async Task<string> CreateAsync(IProjectTemplate template, string parentFolder, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (!Templates.Contains(template))
            throw new ArgumentException($"The template '{template.Id}' is not registered.", nameof(template));
        if (Validate(parentFolder, name) is { } problem)
            throw new IOException(problem);
        var folder = Path.GetFullPath(Path.Combine(parentFolder, ToFolderName(name)));
        var scaffold = new ProjectScaffold(folder, name.Trim());
        try
        {
            await Task.Run(async () =>
            {
                scaffold.CreateStructure();
                await template.CreateAsync(scaffold, cancellationToken);
            }, cancellationToken);
        }
        catch
        {
            TryDelete(folder);
            throw;
        }

        return folder;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
