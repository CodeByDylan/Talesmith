using Talesmith.Runtime.Hosting;

namespace Talesmith.Samples.Cutscenes;

/// <summary>A snapshot of the dialogue on screen: who speaks, how much of the line is revealed and which choices are offered.</summary>
/// <param name="VisibleLength">How many characters of <paramref name="Text"/> are revealed.</param>
public sealed record DialogueView(bool IsOpen, string Speaker, string Text, int VisibleLength, IReadOnlyList<string> Options, int SelectedOption)
{
    public static readonly DialogueView Closed = new(false, string.Empty, string.Empty, 0, [], 0);

    public bool IsFullyRevealed => VisibleLength >= Text.Length;

    public string VisibleText => Text[..Math.Min(VisibleLength, Text.Length)];
}

/// <summary>The dialogue on screen. Lives on the game thread and publishes <see cref="View"/> for the overlay.</summary>
/// <remarks>Cutscenes await <see cref="SayAsync"/> and <see cref="ChooseAsync"/>; input calls <see cref="Advance"/> and <see cref="MoveSelection"/>.</remarks>
public sealed class DialogueState(IGameUi ui)
{
    /// <summary>Characters revealed per second.</summary>
    public const float RevealSpeed = 45;

    private TaskCompletionSource<int>? _pending;
    private float _revealed;

    /// <summary>What the dialogue box shows, for the UI thread.</summary>
    public ViewState<DialogueView> View { get; } = new(ui, DialogueView.Closed);

    public bool IsOpen => View.Value.IsOpen;

    public string Speaker => View.Value.Speaker;

    public string Text => View.Value.Text;

    /// <summary>How many characters of <see cref="Text"/> are revealed.</summary>
    public int VisibleLength => View.Value.VisibleLength;

    public bool IsFullyRevealed => View.Value.IsFullyRevealed;

    public IReadOnlyList<string> Options => View.Value.Options;

    public int SelectedOption => View.Value.SelectedOption;

    /// <summary>Shows a line and completes when the player continues.</summary>
    public Task SayAsync(string speaker, string text) => Show(speaker, text, []);

    /// <summary>Shows a question with options and completes with the chosen option's index.</summary>
    public Task<int> ChooseAsync(string speaker, string text, IReadOnlyList<string> options) => Show(speaker, text, options);

    /// <summary>Reveals the whole line, or continues when it is already revealed.</summary>
    public void Advance()
    {
        if (!IsOpen)
            return;
        if (!IsFullyRevealed)
        {
            Reveal(Text.Length);
            return;
        }

        Complete(Options.Count > 0 ? SelectedOption : 0);
    }

    public void MoveSelection(int delta)
    {
        if (Options.Count > 0 && IsFullyRevealed)
            View.Publish(View.Value with { SelectedOption = (SelectedOption + delta + Options.Count) % Options.Count });
    }

    /// <summary>Picks an option directly, such as with the number keys.</summary>
    public void Choose(int index)
    {
        if (IsOpen && IsFullyRevealed && index >= 0 && index < Options.Count)
            Complete(index);
    }

    /// <summary>Reveals more of the line over time.</summary>
    public void Tick(float deltaSeconds)
    {
        if (IsOpen && !IsFullyRevealed)
            Reveal(_revealed + deltaSeconds * RevealSpeed);
    }

    public void Close()
    {
        if (IsOpen)
            View.Publish(View.Value with { IsOpen = false });
        _pending?.TrySetCanceled();
        _pending = null;
    }

    private Task<int> Show(string speaker, string text, IReadOnlyList<string> options)
    {
        _pending?.TrySetCanceled();
        _pending = new TaskCompletionSource<int>();
        _revealed = 0;
        View.Publish(new DialogueView(true, speaker, text, 0, options, 0));
        return _pending.Task;
    }

    private void Reveal(float characters)
    {
        _revealed = Math.Min(characters, Text.Length);
        if ((int)_revealed != VisibleLength)
            View.Publish(View.Value with { VisibleLength = (int)_revealed });
    }

    private void Complete(int result)
    {
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(result);
    }
}
