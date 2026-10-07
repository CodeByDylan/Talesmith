using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Tests.Controls;

public sealed class NumberFieldTests
{
    private static (Window Window, NumberField Field, TextBox Text, List<string> Events) Show(NumberField field)
    {
        var events = new List<string>();
        field.EditStarted += (_, _) => events.Add("started");
        field.EditCompleted += (_, _) => events.Add($"completed {field.Value}");
        var window = new Window { Content = field, Width = 200, Height = 60 };
        window.Show();
        window.UpdateLayout();
        var text = field.GetVisualDescendants().OfType<TextBox>().Single();
        return (window, field, text, events);
    }

    private static void Press(TextBox box, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = box });

    [Fact]
    public void TypingAnExpressionAndPressingEnterCommitsItAsOneEdit()
    {
        Headless.Run(() =>
        {
            var (window, field, text, events) = Show(new NumberField { Value = 1 });

            field.BeginTextEdit();
            text.Text = "16*2+4";
            Press(text, Key.Enter);

            Assert.Equal(36, field.Value);
            Assert.Equal(["started", "completed 36"], events);
            window.Close();
        });
    }

    [Fact]
    public void EscapeRevertsTheTypedText()
    {
        Headless.Run(() =>
        {
            var (window, field, text, events) = Show(new NumberField { Value = 5 });

            field.BeginTextEdit();
            text.Text = "99";
            Press(text, Key.Escape);
            window.Focus();

            Assert.Equal(5, field.Value);
            Assert.Equal("5", text.Text);
            Assert.Empty(events);
            window.Close();
        });
    }

    [Fact]
    public void ArrowKeysStepWithModifiersAndRespectTheRange()
    {
        Headless.Run(() =>
        {
            var (window, field, text, _) = Show(new NumberField { Value = 0.5, Step = 0.1, Minimum = 0, Maximum = 1 });

            field.BeginTextEdit();
            Press(text, Key.Up);
            Assert.Equal(0.6, field.Value, 9);
            Press(text, Key.Up, KeyModifiers.Shift);
            Assert.Equal(0.61, field.Value, 9);
            Press(text, Key.Up, KeyModifiers.Control);
            Assert.Equal(1, field.Value, 9);
            window.Close();
        });
    }

    [Fact]
    public void InvalidTextIsRejectedAndValuesAreClamped()
    {
        Headless.Run(() =>
        {
            var (window, field, text, _) = Show(new NumberField { Value = 3, Minimum = 0, Maximum = 10, IsInteger = true });

            field.BeginTextEdit();
            text.Text = "abc";
            Press(text, Key.Enter);
            Assert.Equal(3, field.Value);

            text.Text = "7.6 * 2";
            Press(text, Key.Enter);
            Assert.Equal(10, field.Value);
            window.Close();
        });
    }
}
