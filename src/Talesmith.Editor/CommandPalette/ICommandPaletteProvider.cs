using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Editor.CommandPalette;

/// <summary>Adds results to the command palette (Ctrl+K), such as entities to go to or assets to open.</summary>
/// <remarks>
/// Register with <see cref="CommandPaletteServiceCollectionExtensions.AddCommandPaletteProvider{T}"/>. Without a prefix the palette shows
/// commands and the best few results of every provider; typing a provider's <see cref="Prefix"/> first shows only that provider's results.
/// Search runs on the UI thread on every key press, so keep it fast; <see cref="FuzzyMatch"/> scores names the way the palette does.
/// </remarks>
public interface ICommandPaletteProvider
{
    /// <summary>A character that limits the palette to this provider when typed first, such as '@' for entities; null for none.</summary>
    char? Prefix { get; }

    /// <summary>What the results are, such as "Entities", shown with each result and in the prefix hints.</summary>
    string Category { get; }

    /// <summary>Finds results for a query without the prefix; an empty query may return a few suggestions.</summary>
    IEnumerable<PaletteItem> Search(string query, int limit);
}

/// <summary>A result in the command palette.</summary>
/// <param name="Id">A stable id used to list recently run items first, such as "entity:{guid}"; null for items that should not be remembered.</param>
public sealed record PaletteItem(string? Id, string Title, string Category, Action Execute)
{
    /// <summary>A second line of text, such as an asset's folder.</summary>
    public string? Subtitle { get; init; }

    public Geometry? Icon { get; init; }

    public string? GestureText { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>How well the item matches the query; higher is better. See <see cref="FuzzyMatch.Score"/>.</summary>
    public double Score { get; init; }

    public bool HasGesture => GestureText is not null;

    public bool HasSubtitle => Subtitle is not null;
}

public static class CommandPaletteServiceCollectionExtensions
{
    public static IServiceCollection AddCommandPaletteProvider<T>(this IServiceCollection services)
        where T : class, ICommandPaletteProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ICommandPaletteProvider, T>();
        return services;
    }
}
