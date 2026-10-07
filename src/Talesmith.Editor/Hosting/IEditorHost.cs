using Talesmith.Editor.Projects;

namespace Talesmith.Editor.Hosting;

/// <summary>The application around the editor: shows the hub, opens projects in an editor window and closes them.</summary>
public interface IEditorHost
{
    /// <summary>The open project, or null while the hub is shown.</summary>
    EditorProject? CurrentProject { get; }

    /// <summary>Opens a project, closing the open one first; returns false when the user cancelled closing it.</summary>
    /// <exception cref="IOException">The folder is not a project.</exception>
    Task<bool> OpenProjectAsync(string folder);

    /// <summary>Closes the open project and shows the hub; returns false when the user cancelled.</summary>
    Task<bool> CloseProjectAsync();
}

/// <summary>Lets a service veto closing the project or the editor, such as to ask about unsaved changes.</summary>
/// <remarks>Register implementations as <see cref="ICloseGuard"/> singletons; they are asked in registration order.</remarks>
public interface ICloseGuard
{
    /// <summary>Returns false to keep the project open.</summary>
    Task<bool> CanCloseAsync();
}
