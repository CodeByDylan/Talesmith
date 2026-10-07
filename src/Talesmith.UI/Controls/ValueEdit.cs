using Avalonia.Interactivity;

namespace Talesmith.UI.Controls;

/// <summary>Routed events that bracket an interactive edit, such as a drag on a number field, so callers can group its changes into one undo step.</summary>
/// <remarks>Both events bubble, so a panel can handle them for every editor it contains.</remarks>
public static class ValueEdit
{
    /// <summary>Raised before the first change of an edit.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> StartedEvent =
        RoutedEvent.Register<RoutedEventArgs>("EditStarted", RoutingStrategies.Bubble, typeof(ValueEdit));

    /// <summary>Raised after the last change of an edit.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> CompletedEvent =
        RoutedEvent.Register<RoutedEventArgs>("EditCompleted", RoutingStrategies.Bubble, typeof(ValueEdit));

    public static void AddStartedHandler(Interactive element, EventHandler<RoutedEventArgs> handler) => element.AddHandler(StartedEvent, handler);

    public static void RemoveStartedHandler(Interactive element, EventHandler<RoutedEventArgs> handler) => element.RemoveHandler(StartedEvent, handler);

    public static void AddCompletedHandler(Interactive element, EventHandler<RoutedEventArgs> handler) => element.AddHandler(CompletedEvent, handler);

    public static void RemoveCompletedHandler(Interactive element, EventHandler<RoutedEventArgs> handler) => element.RemoveHandler(CompletedEvent, handler);

    internal static void RaiseStarted(Interactive source) => source.RaiseEvent(new RoutedEventArgs(StartedEvent, source));

    internal static void RaiseCompleted(Interactive source) => source.RaiseEvent(new RoutedEventArgs(CompletedEvent, source));

    /// <summary>Raises <see cref="StartedEvent"/>, applies a change and raises <see cref="CompletedEvent"/>.</summary>
    internal static void Apply(Interactive source, Action change)
    {
        RaiseStarted(source);
        change();
        RaiseCompleted(source);
    }
}
