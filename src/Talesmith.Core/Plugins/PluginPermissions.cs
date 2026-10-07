namespace Talesmith.Plugins;

/// <summary>What a plugin declares it needs beyond registering ordinary services.</summary>
[Flags]
public enum PluginPermissions
{
    None = 0,

    /// <summary>Reads or writes files and folders directly instead of going through the asset manager.</summary>
    FileSystem = 1 << 0,

    Network = 1 << 1,

    /// <summary>Starts other processes.</summary>
    ProcessExecution = 1 << 2,

    /// <summary>Adds editor panels, commands, inspectors or tools; required for an editor assembly.</summary>
    EditorUi = 1 << 3,

    /// <summary>Registers systems, scenes or scene listeners that read and change the running scene.</summary>
    RuntimeScene = 1 << 4,

    /// <summary>Creates, changes or deletes asset files.</summary>
    AssetWrite = 1 << 5,

    /// <summary>Replaces the renderer, adds render passes or talks to Vulkan or Skia directly.</summary>
    RenderBackend = 1 << 6
}
