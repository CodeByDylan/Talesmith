namespace Talesmith.EndToEnd.Tests;

/// <summary>A temporary folder for one test, and the repository's samples.</summary>
internal sealed class Workspace : IDisposable
{
    public Workspace() => Folder = Directory.CreateTempSubdirectory("talesmith-e2e-").FullName;

    public string Folder { get; }

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string Sample(string name) => Path.Combine(RepositoryRoot, "samples", name);

    /// <summary>Copies a sample game folder into the workspace, leaving out editor state and compiled scripts.</summary>
    public string CopySample(string name)
    {
        var target = Path.Combine(Folder, name);
        Copy(Sample(name), target);
        return target;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is ".talesmith" or "bin" or "obj")
                continue;
            Copy(directory, Path.Combine(target, name));
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Talesmith.slnx")))
                return folder.FullName;
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
