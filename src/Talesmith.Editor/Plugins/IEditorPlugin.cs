using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Editor.Plugins;

/// <summary>Extends the editor from a plugin's editor assembly: registers panels, commands, viewport tools, property editors and more.</summary>
/// <remarks>
/// <para>The editor creates one instance of every public, non-abstract <see cref="IEditorPlugin"/> class in each enabled plugin's
/// <c>editorAssembly</c> when a project opens, and calls <see cref="ConfigureServices"/> before the editor's services are
/// built. Register with the same extension methods the editor uses for its own features:</para>
/// <list type="bullet">
/// <item><c>services.AddEditorPanel&lt;T&gt;(new EditorPanelInfo(...))</c> for a dock panel; the same id replaces a built-in panel.</item>
/// <item><c>services.AddEditorCommands&lt;T&gt;()</c> for commands, menu entries and app bar buttons.</item>
/// <item><c>services.AddViewportTool&lt;T&gt;()</c> for a scene viewport tool.</item>
/// <item><c>services.AddPropertyEditor&lt;T&gt;()</c> for inspector property editors.</item>
/// <item><c>services.AddCommandPaletteProvider&lt;T&gt;()</c> for command palette results.</item>
/// <item><c>services.AddEntityIconProvider&lt;T&gt;()</c> for viewport icons of entities without visuals.</item>
/// <item><c>services.AddAssetKind(kind, extensions)</c> for new asset kinds in the editor.</item>
/// </list>
/// </remarks>
public interface IEditorPlugin
{
    void ConfigureServices(IServiceCollection services);
}
