using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Editor.Plugins;
using Talesmith.UI.Controls;
using Talesmith.UI.Docking;

namespace Talesmith.Editor.Panels;

/// <summary>The registered panels; supplies them to the dock workspace, creating each the first time it is shown.</summary>
public sealed partial class PanelRegistry : IDockContentProvider
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PanelRegistry> _logger;
    private readonly EditorPluginGuard _plugins;
    private readonly Dictionary<string, EditorPanelRegistration> _registrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PanelHost> _hosts = new(StringComparer.Ordinal);

    public PanelRegistry(IEnumerable<EditorPanelRegistration> registrations, IServiceProvider services, ILogger<PanelRegistry> logger, EditorPluginGuard plugins)
    {
        _services = services;
        _logger = logger;
        _plugins = plugins;
        foreach (var registration in registrations)
            _registrations[registration.Info.Id] = registration;
    }

    /// <summary>Every panel's info, by location and order.</summary>
    public IReadOnlyList<EditorPanelInfo> Panels => [.. _registrations.Values.Select(r => r.Info).OrderBy(i => i.Location).ThenBy(i => i.Order)];

    public bool Contains(string panelId) => _registrations.ContainsKey(panelId);

    public EditorPanelInfo? Find(string panelId) => _registrations.GetValueOrDefault(panelId)?.Info;

    /// <summary>Gets a panel instance, creating it if needed; null when no panel has the id.</summary>
    public IEditorPanel? GetInstance(string panelId) => _registrations.TryGetValue(panelId, out var registration) ? Resolve(registration) : null;

    private IEditorPanel Resolve(EditorPanelRegistration registration) => registration.ServiceKey is { } key
        ? (IEditorPanel)_services.GetRequiredKeyedService(registration.PanelType, key)
        : (IEditorPanel)_services.GetRequiredService(registration.PanelType);

    public IDockPanel? GetPanel(string panelId)
    {
        if (_hosts.TryGetValue(panelId, out var host))
            return host;
        if (!_registrations.TryGetValue(panelId, out var registration))
            return null;
        host = new PanelHost(registration.Info, () => Create(registration));
        _hosts[panelId] = host;
        return host;
    }

    private (Control Content, Control? Actions) Create(EditorPanelRegistration registration)
    {
        try
        {
            var panel = Resolve(registration);
            return (panel.CreateContent(), panel.CreateHeaderActions());
        }
        catch (Exception ex)
        {
            if (_plugins.FindPlugin(registration.PanelType) is { } plugin)
                _plugins.Report(plugin, $"show its panel \"{registration.Info.Title}\"", ex);
            else
                LogPanelFailed(_logger, ex, registration.Info.Id);
            return (new EmptyState { Icon = UI.Icons.AlertTriangle, Title = $"{registration.Info.Title} could not be shown", Hint = ex.Message }, null);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The panel {Panel} could not be created")]
    private static partial void LogPanelFailed(ILogger logger, Exception exception, string panel);

    private sealed class PanelHost(EditorPanelInfo info, Func<(Control Content, Control? Actions)> create) : IDockPanel
    {
        private (Control Content, Control? Actions)? _created;

        public string Id => info.Id;

        public string Title => info.Title;

        public Geometry? Icon => info.Icon;

        public bool CanClose => info.CanClose;

        public Control Content => (_created ??= create()).Content;

        public Control? HeaderActions => (_created ??= create()).Actions;
    }
}
