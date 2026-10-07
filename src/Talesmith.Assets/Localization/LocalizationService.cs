using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Events;

namespace Talesmith.Assets.Localization;

/// <summary>Translates text keys into the player's language.</summary>
/// <remarks>
/// Lookups try the current language, then its parent (such as "pt" for "pt-BR"), then the fallback language and its parent; a key
/// without any translation returns the key itself, so missing text is visible rather than blank.
/// </remarks>
public interface ILocalization
{
    /// <summary>The language text is shown in, such as "en" or "pt-BR".</summary>
    string CurrentLanguage { get; set; }

    /// <summary>The language used for keys that the current language does not translate.</summary>
    string FallbackLanguage { get; set; }

    /// <summary>Every language some string table translates into.</summary>
    IReadOnlyList<string> AvailableLanguages { get; }

    string Get(string key);

    bool TryGet(string key, out string text);

    /// <summary>Gets a text and fills in its placeholders, such as "{0} coins", with the current language's number and date formats.</summary>
    string Format(string key, params object?[] arguments);

    /// <summary>Adds a string table; later tables win for keys that several define.</summary>
    void AddTable(StringTable table);

    bool RemoveTable(StringTable table);

    /// <summary>Raised after the language or the string tables changed, so shown text can be refreshed.</summary>
    event EventHandler? Changed;
}

/// <summary>Where <see cref="LocalizationService"/> starts.</summary>
/// <param name="Language">The initial language; null uses the operating system's UI language.</param>
/// <param name="LoadAllTables">Whether every .tloc file in the asset folder is loaded when the service is created.</param>
public sealed record LocalizationOptions(string? Language = null, string FallbackLanguage = "en", bool LoadAllTables = true);

/// <summary>The default <see cref="ILocalization"/>, backed by <see cref="StringTable"/>s.</summary>
/// <remarks>
/// With <see cref="LocalizationOptions.LoadAllTables"/>, every .tloc file is loaded synchronously when the service is created, in path
/// order. Reloaded string tables replace their old versions. Thread-safe.
/// </remarks>
public sealed partial class LocalizationService : ILocalization, IDisposable
{
    private readonly List<StringTable> _tables = [];
    private readonly Lock _lock = new();
    private readonly ILogger _logger;
    private readonly IDisposable? _reloadSubscription;
    private string _language;
    private string _fallback;

    public LocalizationService(LocalizationOptions? options = null, IAssetManager? assets = null, IEventBus? events = null, ILogger<LocalizationService>? logger = null)
    {
        options ??= new LocalizationOptions();
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _language = options.Language ?? CultureInfo.CurrentUICulture.Name;
        if (_language.Length == 0)
            _language = options.FallbackLanguage;
        _fallback = options.FallbackLanguage;
        if (assets is not null && options.LoadAllTables)
            LoadAll(assets);
        _reloadSubscription = events?.Subscribe<AssetReloaded>(OnReloaded);
    }

    public event EventHandler? Changed;

    public string CurrentLanguage
    {
        get
        {
            lock (_lock)
                return _language;
        }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            lock (_lock)
            {
                if (string.Equals(_language, value, StringComparison.OrdinalIgnoreCase))
                    return;
                _language = value;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string FallbackLanguage
    {
        get
        {
            lock (_lock)
                return _fallback;
        }
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            lock (_lock)
            {
                if (string.Equals(_fallback, value, StringComparison.OrdinalIgnoreCase))
                    return;
                _fallback = value;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<string> AvailableLanguages
    {
        get
        {
            lock (_lock)
                return [.. _tables.SelectMany(table => table.Languages).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        }
    }

    public string Get(string key) => TryGet(key, out var text) ? text : key;

    public bool TryGet(string key, out string text)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_lock)
        {
            foreach (var language in Candidates(_language, _fallback))
            {
                for (var i = _tables.Count - 1; i >= 0; i--)
                {
                    if (_tables[i].TryGet(key, language, out var found))
                    {
                        text = found;
                        return true;
                    }
                }
            }
        }

        text = key;
        return false;
    }

    public string Format(string key, params object?[] arguments)
    {
        var format = Get(key);
        try
        {
            return string.Format(GetCulture(CurrentLanguage), format, arguments);
        }
        catch (FormatException ex)
        {
            LogInvalidFormat(key, ex);
            return format;
        }
    }

    public void AddTable(StringTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        lock (_lock)
            _tables.Add(table);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool RemoveTable(StringTable table)
    {
        bool removed;
        lock (_lock)
            removed = _tables.Remove(table);
        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    public void Dispose() => _reloadSubscription?.Dispose();

    private void LoadAll(IAssetManager assets)
    {
        foreach (var path in assets.Source.List(string.Empty, "*.tloc", recursive: true).Order(StringComparer.Ordinal))
        {
            try
            {
                _tables.Add(assets.Load<StringTable>(path));
            }
            catch (AssetException ex)
            {
                LogTableFailed(path, ex);
            }
        }
    }

    private void OnReloaded(ref AssetReloaded e)
    {
        if (e is not { OldAsset: StringTable old, NewAsset: StringTable replacement })
            return;

        lock (_lock)
        {
            var index = _tables.IndexOf(old);
            if (index < 0)
                return;
            _tables[index] = replacement;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IEnumerable<string> Candidates(string language, string fallback)
    {
        yield return language;
        if (Parent(language) is { } parent)
            yield return parent;
        if (string.Equals(fallback, language, StringComparison.OrdinalIgnoreCase))
            yield break;
        yield return fallback;
        if (Parent(fallback) is { } fallbackParent)
            yield return fallbackParent;
    }

    private static string? Parent(string language)
    {
        var dash = language.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 ? language[..dash] : null;
    }

    private static CultureInfo GetCulture(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The string table {Path} could not be loaded")]
    private partial void LogTableFailed(string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The text of {Key} is not a valid format string")]
    private partial void LogInvalidFormat(string key, Exception exception);
}
