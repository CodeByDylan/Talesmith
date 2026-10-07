using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Console;
using Talesmith.Editor.Projects;
using Talesmith.Editor.TileMaps;
using Talesmith.Scripting;

namespace Talesmith.Editor.Scripting;

/// <summary>Keeps the edit game on the latest compiled scripts, so the inspector and the scene view know their types: swaps them in when that
/// is safe, and otherwise replaces the edit game with one built with them, such as for scripts that declare systems or components.</summary>
/// <remarks>The edit game is not replaced while tile maps have unsaved edits, since the new game would load the maps from their files.</remarks>
public sealed class EditSessionScripts : IDisposable
{
    private readonly IScriptService _scripts;
    private readonly IProjectService _project;
    private readonly TileMapDocuments _maps;
    private readonly IConsole _console;
    private ScriptAssembly? _applied;
    private IDisposable? _lease;
    private bool _updating;
    private bool _waitingForMaps;
    private bool _disposed;

    public EditSessionScripts(IScriptService scripts, IProjectService project, TileMapDocuments maps, IConsole console)
    {
        _scripts = scripts;
        _project = project;
        _maps = maps;
        _console = console;
        scripts.AssemblyChanged += OnAssemblyChanged;
        project.StatusChanged += OnChanged;
        maps.StateChanged += OnChanged;
        Update();
    }

    public void Dispose()
    {
        _disposed = true;
        _scripts.AssemblyChanged -= OnAssemblyChanged;
        _project.StatusChanged -= OnChanged;
        _maps.StateChanged -= OnChanged;
        _lease?.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private void OnAssemblyChanged(object? sender, ScriptAssembly e) => Update();

    private async void Update()
    {
        if (_updating || _disposed || _waitingForMaps && _maps.HasUnsavedMaps)
            return;
        _updating = true;
        try
        {
            while (!_disposed && _scripts.Assembly is { } assembly && !ReferenceEquals(assembly, _applied) && _project.EditSession is { } session)
            {
                var lease = _scripts.Use(assembly);
                try
                {
                    var reloader = session.Game.Services.GetRequiredService<ScriptReloader>();
                    if (!reloader.Check(assembly).RestartRequired)
                        reloader.Reload(assembly);
                    else if (!await ReplaceAsync(assembly))
                    {
                        lease.Dispose();
                        return;
                    }
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }

                _applied = assembly;
                _lease?.Dispose();
                _lease = lease;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _console.Error($"The scene view could not load the compiled scripts: {ex.Message}", ex, ConsoleSource.Script);
        }
        finally
        {
            _updating = false;
        }
    }

    private async Task<bool> ReplaceAsync(ScriptAssembly assembly)
    {
        if (_maps.HasUnsavedMaps)
        {
            if (!_waitingForMaps)
                _console.Info("The scene view loads the changed scripts once the tile maps with unsaved edits are saved", ConsoleSource.Script);
            _waitingForMaps = true;
            return false;
        }

        _waitingForMaps = false;
        await _project.ReplaceEditSessionAsync(assembly);
        return true;
    }
}
