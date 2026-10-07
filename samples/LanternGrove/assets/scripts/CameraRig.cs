namespace LanternGrove;

/// <summary>Moves the point the camera follows: above the player and a little ahead of where it faces.</summary>
public sealed class CameraRig : Script
{
    public Entity Player;

    [Range(0, 400)]
    public float LookAhead = 60;

    [Range(0, 400)]
    public float Above = 55;

    [Range(0, 20)]
    [Tooltip("How quickly the look-ahead swings around when the player turns.")]
    public float TurnSharpness = 2.5f;

    private PlayerController? _controller;
    private float _ahead;

    protected override void OnStart() => _controller = GetScript<PlayerController>(Player);

    protected override void LateUpdate()
    {
        if (_controller is null || !World.IsAlive(Player))
            return;
        _ahead = MathHelper.Damp(_ahead, _controller.Facing * LookAhead, TurnSharpness, Time.DeltaTime);
        Position = GetComponent<Transform>(Player).Position + new Vector2(_ahead, -Above);
    }
}
