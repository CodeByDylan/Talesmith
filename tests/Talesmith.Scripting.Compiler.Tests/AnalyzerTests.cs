using Talesmith.Scripting.Compiler.Analyzers;

namespace Talesmith.Scripting.Compiler.Tests;

public sealed class AnalyzerTests
{
    [Fact]
    public async Task GameLoopPitfallsInUpdateMethodsAreReported()
    {
        using var project = new TempProject();
        project.Write("Pitfalls.cs", """
            using System.IO;
            using System.Threading;

            namespace TestGame;

            public sealed class Pitfalls : Script
            {
                private readonly List<int> _cache = [];
                private int _score;

                protected override void OnStart()
                {
                    var allowed = new List<int>();
                    Thread.Sleep(1);
                    _ = "Score: " + _score;
                }

                protected override void Update()
                {
                    Thread.Sleep(5);
                    var list = new List<int>();
                    var big = _cache.Where(x => x > 2).ToList();
                    var text = "Score: " + _score;
                    var other = $"Score {_score}";
                    var position = new Vector2(1, 2);
                    if (_score < 0)
                        throw new InvalidOperationException("Negative score " + _score);
                }

                protected override void FixedUpdate() => File.ReadAllText("save.json");

                protected override void LateUpdate() => _ = Task.FromResult(1).Result;

                private async void Save() => await Task.Yield();
            }

            public sealed class Reader : ISystem
            {
                public void Update(in SystemContext context)
                {
                    var buffer = new int[16];
                    Action fire = async () => await Task.Yield();
                }
            }
            """);
        using var compiler = new ScriptCompiler(project.Options);

        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Errors));
        var found = result.Diagnostics.Where(d => d.Id.StartsWith("TS", StringComparison.Ordinal)).Select(d => (d.Id, d.Line)).ToList();
        Assert.Equal(
        [
            (ScriptDiagnosticIds.BlockingCall, 20),
            (ScriptDiagnosticIds.Allocation, 21),
            (ScriptDiagnosticIds.Allocation, 22),
            (ScriptDiagnosticIds.Allocation, 22),
            (ScriptDiagnosticIds.StringBuilding, 23),
            (ScriptDiagnosticIds.StringBuilding, 24),
            (ScriptDiagnosticIds.BlockingCall, 30),
            (ScriptDiagnosticIds.BlockingCall, 32),
            (ScriptDiagnosticIds.AsyncVoid, 34),
            (ScriptDiagnosticIds.Allocation, 41),
            (ScriptDiagnosticIds.AsyncVoid, 42)
        ], found);
        var sleep = result.Diagnostics.First(d => d.Id == ScriptDiagnosticIds.BlockingCall);
        Assert.Equal(ScriptDiagnosticSeverity.Warning, sleep.Severity);
        Assert.Contains("Thread.Sleep", sleep.Message);
        Assert.Contains("Pitfalls.Update", sleep.Message);
        Assert.Equal(ScriptDiagnosticSeverity.Info, result.Diagnostics.First(d => d.Id == ScriptDiagnosticIds.StringBuilding).Severity);
    }

    [Fact]
    public async Task AnalyzersCanBeTurnedOff()
    {
        using var project = new TempProject();
        project.Write("Sleeper.cs", "namespace TestGame; public sealed class Sleeper : Script { protected override void Update() => System.Threading.Thread.Sleep(1); }");
        using var compiler = new ScriptCompiler(project.Options with { RunAnalyzers = false });

        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("TS", StringComparison.Ordinal));
    }
}
