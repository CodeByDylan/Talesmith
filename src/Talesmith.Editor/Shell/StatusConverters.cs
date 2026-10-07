using Avalonia.Data.Converters;

namespace Talesmith.Editor.Shell;

/// <summary>Converters that turn a <see cref="StatusKind"/> into style classes.</summary>
public static class StatusConverters
{
    public static FuncValueConverter<StatusKind, bool> IsSuccess { get; } = new(kind => kind == StatusKind.Success);

    public static FuncValueConverter<StatusKind, bool> IsWarning { get; } = new(kind => kind == StatusKind.Warning);

    public static FuncValueConverter<StatusKind, bool> IsError { get; } = new(kind => kind == StatusKind.Error);

    public static FuncValueConverter<StatusKind, bool> IsBusy { get; } = new(kind => kind == StatusKind.Busy);
}
