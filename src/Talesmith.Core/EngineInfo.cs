namespace Talesmith;

/// <summary>Version information shared by the engine and its plugins.</summary>
public static class EngineInfo
{
    /// <summary>The engine release.</summary>
    public static Version Version { get; } = typeof(EngineInfo).Assembly.GetName().Version ?? new Version(0, 1, 0);

    /// <summary>The version of the public contracts plugins compile against; it changes only when contracts break.</summary>
    public const int ContractVersion = 1;
}
