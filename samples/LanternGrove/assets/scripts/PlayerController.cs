using Talesmith.Runtime.Scenes;

namespace LanternGrove;

/// <summary>Runs with Move, jumps with Jump (higher while held), dashes with Dash and returns to safe ground after a fall.</summary>
public sealed class PlayerController : Script
{
    [Header("Running")]
    [Range(0, 800)]
    public float RunSpeed = 320;

    [Range(0, 10000)]
    [Tooltip("How quickly the player reaches running speed on the ground, in units per second squared.")]
    public float Acceleration = 2800;

    [Range(0, 10000)]
    public float AirAcceleration = 1600;

    [Header("Jumping")]
    [Range(0, 2000)]
    public float JumpSpeed = 860;

    [Range(0, 6000)]
    public float Gravity = 2300;

    [Range(1, 4)]
    [Tooltip("Gravity is multiplied by this while falling or once the jump button is released, for snappier jumps.")]
    public float FallGravityScale = 1.6f;

    [Range(0, 3000)]
    public float MaxFallSpeed = 1150;

    [Range(0, 0.5)]
    [Tooltip("Seconds after walking off a ledge during which a jump still works.")]
    public float CoyoteTime = 0.1f;

    [Range(0, 0.5)]
    [Tooltip("Seconds a jump pressed just before landing is remembered.")]
    public float JumpBuffer = 0.12f;

    [Header("Dash")]
    [Range(0, 2000)]
    public float DashSpeed = 820;

    [Range(0, 1)]
    public float DashTime = 0.16f;

    [Range(0, 3)]
    public float DashCooldown = 0.45f;

    [Header("Falling")]
    [Tooltip("Below this height the player returns to the last ground it stood on.")]
    public float FallLimit = 832;

    private Vector2 _velocity;
    private Vector2 _safeGround;
    private float _coyote;
    private float _buffer;
    private float _dashLeft;
    private float _dashWait;
    private bool _dashQueued;
    private bool _airDashUsed;
    private bool _jumpHeld;

    /// <summary>1 when facing right, -1 when facing left.</summary>
    public int Facing { get; private set; } = 1;

    public bool IsGrounded { get; private set; }

    public Vector2 Velocity => _velocity;

    protected override void OnStart() => _safeGround = Position;

    protected override void Update()
    {
        if (Input.WasPressed("Jump"))
            _buffer = JumpBuffer;
        if (Input.WasPressed("Dash"))
            _dashQueued = true;
        _jumpHeld = Input.IsDown("Jump");
        if (Input.WasPressed("Restart"))
            _ = Scenes.ReloadAsync(SceneTransition.Default);

        ref var sprite = ref GetComponent<Sprite>();
        sprite.FlipX = Facing < 0;
        ref var animator = ref GetComponent<SpriteAnimator>();
        var animation = _dashLeft > 0 ? "run"
            : !IsGrounded ? _velocity.Y < 0 ? "jump" : "fall"
            : MathF.Abs(_velocity.X) > 20 ? "run" : "idle";
        if (animator.Animation != animation)
            animator.Animation = animation;
    }

    protected override void FixedUpdate()
    {
        var dt = Time.DeltaTime;
        var move = Input.Vector("Move").X;
        if (move != 0)
            Facing = move > 0 ? 1 : -1;

        _coyote = IsGrounded ? CoyoteTime : _coyote - dt;
        _buffer -= dt;
        _dashWait -= dt;

        if (_dashQueued && _dashWait <= 0 && !_airDashUsed)
        {
            _dashLeft = DashTime;
            _dashWait = DashCooldown;
            _airDashUsed = !IsGrounded;
            _velocity = new Vector2(Facing * DashSpeed, 0);
            Audio.Play("audio/jump.wav", new SoundOptions(Volume: 0.35f, Pitch: 1.6f));
        }

        _dashQueued = false;
        if (_dashLeft > 0)
        {
            _dashLeft -= dt;
        }
        else
        {
            var target = move * RunSpeed;
            var rate = IsGrounded ? Acceleration : AirAcceleration;
            _velocity.X = MoveTowards(_velocity.X, target, rate * dt);

            var falling = _velocity.Y > 0 || !_jumpHeld;
            _velocity.Y = MathF.Min(_velocity.Y + Gravity * (falling ? FallGravityScale : 1) * dt, MaxFallSpeed);
            if (_buffer > 0 && _coyote > 0)
            {
                _velocity.Y = -JumpSpeed;
                _buffer = 0;
                _coyote = 0;
                Audio.Play("audio/jump.wav", new SoundOptions(Volume: 0.5f));
            }
        }

        var hits = Physics.MoveCharacter(Entity, _velocity * dt);
        if ((hits & CharacterCollisions.Above) != 0 && _velocity.Y < 0)
            _velocity.Y = 0;
        if ((hits & CharacterCollisions.Sides) != 0)
            _velocity.X = 0;

        IsGrounded = GetComponent<CharacterController2D>().IsGrounded;
        if (IsGrounded)
        {
            _velocity.Y = MathF.Min(_velocity.Y, 0);
            _airDashUsed = false;
            if (OnSolidGround())
                _safeGround = Position;
        }

        if (Position.Y > FallLimit)
            Respawn();
    }

    private void Respawn()
    {
        Audio.Play("audio/fall.wav", new SoundOptions(Volume: 0.5f));
        Position = _safeGround;
        _velocity = Vector2.Zero;
        _dashLeft = 0;
    }

    private bool OnSolidGround()
    {
        var filter = QueryFilter.Default with { Ignore = Entity };
        var feet = Position + new Vector2(0, -4);
        return Physics.RayCast(feet + new Vector2(-24, 0), Vector2.UnitY, 16, filter, out _)
               && Physics.RayCast(feet + new Vector2(24, 0), Vector2.UnitY, 16, filter, out _);
    }

    private static float MoveTowards(float current, float target, float maxDelta) =>
        MathF.Abs(target - current) <= maxDelta ? target : current + MathF.Sign(target - current) * maxDelta;
}
