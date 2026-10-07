using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;

namespace Talesmith.Editor.Scripting;

/// <summary>Plays with the latest scripts: waits for a running compilation, refuses to play while the scripts have errors, and hands the
/// compiled scripts to the play session.</summary>
public sealed class ScriptPlayContributor(IScriptService scripts) : IPlaySessionContributor
{
    public async ValueTask<string?> PrepareAsync(CancellationToken cancellationToken)
    {
        await scripts.WhenIdle.WaitAsync(cancellationToken);
        if (!scripts.HasErrors)
            return null;
        var errors = scripts.LastResult?.Errors.Count() ?? 0;
        return $"The scripts have {errors} compile {(errors == 1 ? "error" : "errors")}. Fix {(errors == 1 ? "it" : "them")} to play; the console lists where.";
    }

    public GameSessionRequest Contribute(GameSessionRequest request) => request with { Scripts = scripts.Assembly };
}
