using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Projects;

namespace Talesmith.Editor.Hub;

/// <summary>A project in the hub: a recent project or a sample.</summary>
public sealed class ProjectCardViewModel
{
    public ProjectCardViewModel(string path, string name, DateTime? lastOpenedUtc, bool isSample, IAsyncRelayCommand<ProjectCardViewModel> open,
        IRelayCommand<ProjectCardViewModel>? remove)
    {
        Path = path;
        Name = name;
        LastOpenedUtc = lastOpenedUtc;
        IsSample = isSample;
        OpenCommand = open;
        RemoveCommand = remove;
        Exists = EditorProject.IsProject(path);
        Artwork = ProjectArtwork.Gradient(name);
        Thumbnail = ProjectArtwork.LoadThumbnail(path);
    }

    public string Path { get; }

    public string Name { get; }

    public DateTime? LastOpenedUtc { get; }

    public bool IsSample { get; }

    /// <summary>Whether the folder still holds the project.</summary>
    public bool Exists { get; }

    public bool IsMissing => !Exists;

    public IBrush Artwork { get; }

    public Bitmap? Thumbnail { get; }

    public bool HasThumbnail => Thumbnail is not null;

    public string Initials => ProjectArtwork.Initials(Name);

    /// <summary>The path with the home folder shortened to ~.</summary>
    public string DisplayPath => ProjectArtwork.DisplayPath(Path);

    public string LastOpenedText => LastOpenedUtc is { } time ? FormatAge(DateTime.UtcNow - time) : IsSample ? "Sample project" : "";

    public IAsyncRelayCommand<ProjectCardViewModel> OpenCommand { get; }

    public IRelayCommand<ProjectCardViewModel>? RemoveCommand { get; }

    public bool CanRemove => RemoveCommand is not null;

    public static string FormatAge(TimeSpan age) => age.TotalMinutes switch
    {
        < 1 => "Just now",
        < 60 => $"{(int)age.TotalMinutes} min ago",
        < 60 * 24 => Plural((int)age.TotalHours, "hour"),
        < 60 * 24 * 2 => "Yesterday",
        < 60 * 24 * 30 => Plural((int)age.TotalDays, "day"),
        < 60 * 24 * 365 => Plural((int)(age.TotalDays / 30), "month"),
        _ => Plural((int)(age.TotalDays / 365), "year")
    };

    private static string Plural(int count, string unit) => string.Create(CultureInfo.CurrentCulture, $"{count} {unit}{(count == 1 ? "" : "s")} ago");
}
