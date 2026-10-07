using Talesmith.Editor.Undo;

namespace Talesmith.Editor.Tests.Undo;

public sealed class UndoServiceTests
{
    private readonly ManualTime _time = new();
    private readonly UndoService _undo;
    private readonly object _document = new();
    private readonly List<int> _values = [];

    public UndoServiceTests() => _undo = new UndoService(_time);

    [Fact]
    public void UndoAndRedoRevertAndReapplyEdits()
    {
        _undo.Execute(Set(1));
        _undo.Execute(Set(2));

        _undo.Undo();
        Assert.Equal([1], _values);
        _undo.Undo();
        Assert.Empty(_values);
        _undo.Redo();
        _undo.Redo();
        Assert.Equal([1, 2], _values);
        Assert.False(_undo.CanRedo);
    }

    [Fact]
    public void NewEditsDiscardTheRedoSteps()
    {
        _undo.Execute(Set(1));
        _undo.Undo();
        _undo.Execute(Set(2));

        Assert.False(_undo.CanRedo);
        Assert.Single(_undo.UndoSteps);
    }

    [Fact]
    public void EditsThatMergeWithinTheWindowBecomeOneStep()
    {
        var counter = new Counter(_document);
        _undo.Execute(counter.Add(1));
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _undo.Execute(counter.Add(2));

        Assert.Single(_undo.UndoSteps);
        Assert.Equal(3, counter.Value);
        _undo.Undo();
        Assert.Equal(0, counter.Value);
    }

    [Fact]
    public void EditsAfterTheWindowStaySeparate()
    {
        var counter = new Counter(_document);
        _undo.Execute(counter.Add(1));
        _time.Advance(TimeSpan.FromSeconds(2));
        _undo.Execute(counter.Add(2));

        Assert.Equal(2, _undo.UndoSteps.Count);
    }

    [Fact]
    public void SealingStopsMerging()
    {
        var counter = new Counter(_document);
        _undo.Execute(counter.Add(1));
        _undo.Seal();
        _undo.Execute(counter.Add(1));

        Assert.Equal(2, _undo.UndoSteps.Count);
    }

    [Fact]
    public void ATransactionIsOneStepNamedByIt()
    {
        using (_undo.BeginTransaction("Paint tiles"))
        {
            _undo.Execute(Set(1));
            using (_undo.BeginTransaction("Inner"))
                _undo.Execute(Set(2));
            Assert.False(_undo.CanUndo);
        }

        var step = Assert.Single(_undo.UndoSteps);
        Assert.Equal("Paint tiles", step.Description);
        _undo.Undo();
        Assert.Empty(_values);
        _undo.Redo();
        Assert.Equal([1, 2], _values);
    }

    [Fact]
    public void ATransactionMergesEditsOfTheSameValue()
    {
        var counter = new Counter(_document);
        using (_undo.BeginTransaction("Drag"))
        {
            for (var i = 0; i < 50; i++)
                _undo.Execute(counter.Add(1));
        }

        var step = Assert.IsType<CompositeCommand>(Assert.Single(_undo.UndoSteps));
        Assert.Single(step.Commands);
    }

    [Fact]
    public void CancellingATransactionRevertsItsEditsAndRecordsNothing()
    {
        var transaction = _undo.BeginTransaction("Drag");
        _undo.Execute(Set(1));
        _undo.Execute(Set(2));
        transaction.Cancel();

        Assert.Empty(_values);
        Assert.False(_undo.CanUndo);
    }

    [Fact]
    public void DocumentsAreDirtyUntilSavedAndCleanAgainWhenUndoneToTheSavePoint()
    {
        Assert.False(_undo.IsDirty(_document));
        _undo.Execute(Set(1));
        Assert.True(_undo.IsDirty(_document));

        _undo.MarkSaved(_document);
        Assert.False(_undo.IsDirty(_document));

        _undo.Execute(Set(2));
        Assert.True(_undo.IsDirty(_document));
        _undo.Undo();
        Assert.False(_undo.IsDirty(_document));
        _undo.Undo();
        Assert.True(_undo.IsDirty(_document));
        _undo.Redo();
        Assert.False(_undo.IsDirty(_document));
    }

    [Fact]
    public void MergingIntoTheSavedStepMakesTheDocumentDirty()
    {
        var counter = new Counter(_document);
        _undo.Execute(counter.Add(1));
        _undo.MarkSaved(_document);
        _undo.Execute(counter.Add(1));

        Assert.Single(_undo.UndoSteps);
        Assert.True(_undo.IsDirty(_document));
    }

    [Fact]
    public void EditsOfOtherDocumentsDoNotMakeADocumentDirty()
    {
        var other = new object();
        _undo.Execute(new DelegateCommand("Other", () => { }, () => { }, other));

        Assert.False(_undo.IsDirty(_document));
        Assert.True(_undo.IsDirty(other));
    }

    [Fact]
    public void ClearingADocumentKeepsItDirty()
    {
        _undo.Execute(Set(1));
        _undo.Clear(_document);

        Assert.False(_undo.CanUndo);
        Assert.True(_undo.IsDirty(_document));
    }

    [Fact]
    public void MoveToJumpsToAPosition()
    {
        _undo.Execute(Set(1));
        _undo.Execute(Set(2));
        _undo.Execute(Set(3));

        _undo.MoveTo(1);
        Assert.Equal([1], _values);
        _undo.MoveTo(3);
        Assert.Equal([1, 2, 3], _values);
    }

    private DelegateCommand Set(int value) =>
        new($"Add {value}", () => _values.Add(value), () => _values.Remove(value), _document);

    private sealed class Counter(object document)
    {
        public int Value { get; set; }

        public IUndoableCommand Add(int amount) => new AddCommand(this, amount, document);

        private sealed class AddCommand(Counter counter, int amount, object document) : IUndoableCommand
        {
            private int _amount = amount;

            public string Description => "Add";

            public object? Document => document;

            public void Apply() => counter.Value += _amount;

            public void Revert() => counter.Value -= _amount;

            public bool TryMerge(IUndoableCommand next)
            {
                if (next is not AddCommand other || other.Counter != counter)
                    return false;
                _amount += other._amount;
                return true;
            }

            private Counter Counter => counter;
        }
    }
}

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan time) => _now += time;
}
