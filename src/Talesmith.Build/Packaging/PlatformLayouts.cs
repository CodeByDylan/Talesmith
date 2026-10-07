using Talesmith.Build.Pipeline;

namespace Talesmith.Build.Packaging;

/// <summary>A platform's own files in a build, next to the executable and the <c>game</c> folder.</summary>
public interface IPlatformLayout
{
    /// <summary>Writes the platform's own files once the executable and game are in place.</summary>
    Task FinishAsync(BuildContext context, CancellationToken cancellationToken);
}

internal static class PlatformLayouts
{
    public static IPlatformLayout For(BuildTarget target) => target.Platform switch
    {
        BuildPlatform.Linux => new LinuxLayout(),
        _ => new WindowsLayout()
    };
}

/// <summary>An executable next to its libraries and a <c>game</c> folder.</summary>
internal sealed class WindowsLayout : IPlatformLayout
{
    public Task FinishAsync(BuildContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Like Windows, plus the icon as <c>&lt;name&gt;.png</c> for desktop entries.</summary>
internal sealed class LinuxLayout : IPlatformLayout
{
    public async Task FinishAsync(BuildContext context, CancellationToken cancellationToken)
    {
        if (context.Icon is { } icon)
            await File.WriteAllBytesAsync(Path.Combine(context.StagingDirectory, context.Name + ".png"), icon, cancellationToken).ConfigureAwait(false);
    }
}
