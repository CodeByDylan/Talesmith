using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talesmith.Samples.Cutscenes;

/// <summary>A cutscene: a list of steps played in order, with labels to jump between.</summary>
public sealed record CutsceneScript(IReadOnlyList<CutsceneStep> Steps)
{
    public const string Extension = ".cutscene";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <exception cref="JsonException">The script is not valid.</exception>
    public static CutsceneScript Parse(Stream stream) =>
        JsonSerializer.Deserialize<CutsceneScript>(stream, JsonOptions) ?? throw new JsonException("The cutscene is empty.");

    /// <summary>The step index of every label.</summary>
    public IReadOnlyDictionary<string, int> Labels { get; } = Steps
        .Select((step, index) => (step, index))
        .Where(x => x.step is LabelStep)
        .ToDictionary(x => ((LabelStep)x.step).Name, x => x.index, StringComparer.OrdinalIgnoreCase);
}

/// <summary>One step of a cutscene, chosen in JSON by its "type".</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SayStep), "say")]
[JsonDerivedType(typeof(ChoiceStep), "choice")]
[JsonDerivedType(typeof(CameraStep), "camera")]
[JsonDerivedType(typeof(CameraBackStep), "cameraBack")]
[JsonDerivedType(typeof(WaitStep), "wait")]
[JsonDerivedType(typeof(SoundStep), "sound")]
[JsonDerivedType(typeof(LabelStep), "label")]
[JsonDerivedType(typeof(GotoStep), "goto")]
[JsonDerivedType(typeof(EndStep), "end")]
public abstract record CutsceneStep;

/// <summary>Shows a line of dialogue and waits for the player to continue.</summary>
public sealed record SayStep(string Speaker, string Text) : CutsceneStep;

/// <summary>Asks a question and jumps to the label of the chosen option.</summary>
public sealed record ChoiceStep(string Speaker, string Text, IReadOnlyList<ChoiceOption> Options) : CutsceneStep;

public sealed record ChoiceOption(string Text, string Goto);

/// <summary>Pans the camera to the map object with this name.</summary>
public sealed record CameraStep(string Target, float Duration = 1.2f) : CutsceneStep;

/// <summary>Pans the camera back to what it followed before the cutscene.</summary>
public sealed record CameraBackStep(float Duration = 1f) : CutsceneStep;

public sealed record WaitStep(float Seconds) : CutsceneStep;

public sealed record SoundStep(string Path, float Volume = 1) : CutsceneStep;

public sealed record LabelStep(string Name) : CutsceneStep;

public sealed record GotoStep(string Label) : CutsceneStep;

public sealed record EndStep : CutsceneStep;
