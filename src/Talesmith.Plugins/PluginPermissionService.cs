using Microsoft.Extensions.Logging;

namespace Talesmith.Plugins;

/// <summary>Answers permission checks from the permissions plugins declared in their manifests.</summary>
public sealed class PluginPermissionService : IPluginPermissions
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PluginPermissions> _declared;
    private readonly HashSet<(string, PluginPermissions, string)> _seen = [];
    private readonly List<PluginPermissionViolation> _violations = [];
    private readonly PluginPermissionPolicy _policy;
    private readonly ILogger _logger;

    /// <param name="declared">Each known plugin's declared permissions, by id.</param>
    public PluginPermissionService(IReadOnlyDictionary<string, PluginPermissions> declared, PluginPermissionPolicy policy, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(logger);
        _declared = new Dictionary<string, PluginPermissions>(declared, StringComparer.Ordinal);
        _policy = policy;
        _logger = logger;
    }

    public IReadOnlyList<PluginPermissionViolation> Violations
    {
        get
        {
            lock (_gate)
                return [.. _violations];
        }
    }

    public event EventHandler<PluginPermissionViolation>? ViolationRecorded;

    public PluginPermissions GetDeclared(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        lock (_gate)
            return _declared.GetValueOrDefault(pluginId);
    }

    public string? FindPlugin(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return PluginLoadContext.FindPlugin(type.Assembly);
    }

    public bool Check(string pluginId, PluginPermissions permission, string operation)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(operation);
        PluginPermissionViolation violation;
        lock (_gate)
        {
            var missing = permission & ~_declared.GetValueOrDefault(pluginId);
            if (missing == PluginPermissions.None)
                return true;

            var denied = _policy == PluginPermissionPolicy.Enforce;
            if (!_seen.Add((pluginId, missing, operation)))
                return !denied;
            violation = new PluginPermissionViolation(pluginId, missing, operation, denied);
            _violations.Add(violation);
        }

        PluginLog.PermissionViolation(_logger, pluginId, operation, PluginPermissionNames.GetName(violation.Missing), violation.Denied ? "refused" : "allowed");
        ViolationRecorded?.Invoke(this, violation);
        return !violation.Denied;
    }

    public bool Check(Type caller, PluginPermissions permission, string operation)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return FindPlugin(caller) is not { } pluginId || Check(pluginId, permission, operation);
    }
}
