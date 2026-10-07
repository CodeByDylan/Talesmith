using Avalonia.Controls;
using Talesmith.Editor.Panels;

namespace Talesmith.Editor.Console;

/// <summary>The Console panel.</summary>
public sealed class ConsolePanel(ConsoleViewModel viewModel) : IEditorPanel
{
    public Control CreateContent() => new ConsoleView { DataContext = viewModel };
}
