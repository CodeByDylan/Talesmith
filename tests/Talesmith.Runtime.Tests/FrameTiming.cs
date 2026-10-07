namespace Talesmith.Runtime.Tests;

/// <summary>Tests that count frames against the clock. They run alone, after the other tests, so no other work delays their frames.</summary>
[CollectionDefinition(nameof(FrameTiming), DisableParallelization = true)]
public sealed class FrameTiming;
