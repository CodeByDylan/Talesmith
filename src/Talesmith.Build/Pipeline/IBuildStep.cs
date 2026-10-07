namespace Talesmith.Build.Pipeline;

/// <summary>One step of a build, such as compiling scripts or packing content.</summary>
/// <remarks>Steps log problems through <see cref="BuildContext.Write"/>; the build stops after a step that logged errors.</remarks>
public interface IBuildStep
{
    /// <summary>What the step does, shown while it runs, such as "Compiling scripts".</summary>
    string Title { get; }

    /// <summary>Whether the step takes part in this build.</summary>
    bool AppliesTo(BuildContext context) => true;

    Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken);
}
