using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Rendering.Lighting;

namespace Talesmith.Rendering;

/// <summary>Everything to draw for one frame: camera, draws, batches, lighting and post effects. Built on the game thread, consumed by a renderer.</summary>
/// <remarks>
/// <para>Call <see cref="Begin(in Camera2D, in ViewLayout, Color, Color, double, Texture)"/>, add draws, then <see cref="Finish"/>. Draws
/// are ordered by layer, then sort key, then submission order; use the sort key for depth sorting such as drawing entities by their Y position. Consecutive draws with the same texture,
/// material, layer, space and sort key merge as they are added, and <see cref="Finish"/> sorts only when draws arrived out of order.</para>
/// <para>Renderers draw everything, including lighting and post effects, inside <see cref="ViewLayout.ViewRect"/> of <see cref="View"/>
/// and fill the rest of the target with <see cref="BorderColor"/>.</para>
/// <para>Frames are reused through a <see cref="FrameExchange"/>, so building one does not allocate once its buffers have grown.</para>
/// </remarks>
public sealed class RenderFrame
{
    private static readonly Rect2 UnitSource = new(0, 0, 1, 1);

    private SpriteInstance[] _instances = new SpriteInstance[4096];
    private SpriteInstance[] _sortedInstances = [];
    private Command[] _commands = new Command[1024];
    private Int128[] _sortKeys = [];
    private DrawBatch[] _batches = new DrawBatch[256];
    private readonly List<PostEffectInstance> _postEffects = [];
    private int _instanceCount;
    private int _commandCount;
    private int _batchCount;
    private bool _inOrder = true;
    private bool _sorted;
    private bool _finished;

    /// <summary>The camera the frame is drawn through, with its zoom in device pixels per world unit.</summary>
    public Camera2D Camera { get; private set; }

    /// <summary>Where the frame is drawn inside the render target.</summary>
    public ViewLayout View { get; private set; }

    /// <summary>The color behind everything inside the view.</summary>
    public Color ClearColor { get; private set; }

    /// <summary>The color of the render target outside the view.</summary>
    public Color BorderColor { get; private set; }

    /// <summary>Game time in seconds, available to shaders.</summary>
    public double Time { get; private set; }

    /// <summary>The renderer's 1×1 white texture, used by the shape helpers.</summary>
    public Texture WhiteTexture { get; private set; }

    /// <summary>The transform from world space to device pixels relative to the top-left of the view.</summary>
    public Matrix3x2 ViewMatrix { get; private set; }

    /// <summary>The transform from view units, the space of <see cref="RenderSpace.Screen"/> draws, to device pixels relative to the top-left of the view.</summary>
    public Matrix3x2 ScreenMatrix { get; private set; }

    /// <summary>The world area visible in the view.</summary>
    public Rect2 VisibleBounds { get; private set; }

    /// <summary>Instances in draw order; valid after <see cref="Finish"/>.</summary>
    public ReadOnlySpan<SpriteInstance> Instances => _sorted ? _sortedInstances.AsSpan(0, _instanceCount) : _instances.AsSpan(0, _instanceCount);

    /// <summary>Batches in draw order; valid after <see cref="Finish"/>.</summary>
    public ReadOnlySpan<DrawBatch> Batches => _batches.AsSpan(0, _batchCount);

    public IReadOnlyList<PostEffectInstance> PostEffects => _postEffects;

    /// <summary>The number of sprites drawn individually, excluding mesh contents.</summary>
    public int SpriteCount => _instanceCount;

    /// <summary>The number of instances drawn from meshes.</summary>
    public int MeshInstanceCount { get; private set; }

    /// <summary>The frame's lights, occluders and ambient light; off unless a lighting system enables it.</summary>
    public LightingFrame Lighting { get; } = new();

    /// <summary>Starts a new frame that fills a whole target of <paramref name="targetSize"/> pixels, with screen space in pixels.</summary>
    public void Begin(in Camera2D camera, Vector2 targetSize, Color clearColor, double time, Texture whiteTexture) =>
        Begin(camera, ViewLayout.Whole(targetSize), clearColor, clearColor, time, whiteTexture);

    /// <summary>Starts a new frame drawn into the view of <paramref name="view"/>, discarding the previous contents.</summary>
    /// <param name="camera">The scene's camera; the frame draws it with its zoom multiplied by the view's scale.</param>
    public void Begin(in Camera2D camera, in ViewLayout view, Color clearColor, Color borderColor, double time, Texture whiteTexture)
    {
        Camera = view.ScaleCamera(camera);
        View = view;
        ClearColor = clearColor;
        BorderColor = borderColor;
        Time = time;
        WhiteTexture = whiteTexture;
        ViewMatrix = Camera.ViewMatrix(view.ViewRect.Size);
        ScreenMatrix = Matrix3x2.CreateScale(view.Scale);
        VisibleBounds = view.VisibleWorld(Camera);
        _instanceCount = 0;
        _commandCount = 0;
        _batchCount = 0;
        MeshInstanceCount = 0;
        _postEffects.Clear();
        Lighting.Reset();
        _inOrder = true;
        _sorted = false;
        _finished = false;
    }

    /// <summary>Whether world-space bounds overlap the visible area; use it to skip off-screen draws.</summary>
    public bool IsVisible(in Rect2 worldBounds) => VisibleBounds.Intersects(worldBounds);

    public void Draw(Texture texture, Material? material, in SpriteInstance instance, int layer, float sortKey = 0, RenderSpace space = RenderSpace.World)
    {
        EnsureBuilding();
        if (_instanceCount == _instances.Length)
            Array.Resize(ref _instances, _instances.Length * 2);
        _instances[_instanceCount] = instance;
        Append(texture, material ?? Material.Default, layer, sortKey, space, _instanceCount, 1);
        _instanceCount++;
    }

    public void Draw(Texture texture, Material? material, ReadOnlySpan<SpriteInstance> instances, int layer, float sortKey = 0, RenderSpace space = RenderSpace.World)
    {
        EnsureBuilding();
        if (instances.IsEmpty)
            return;
        if (_instanceCount + instances.Length > _instances.Length)
            Array.Resize(ref _instances, Math.Max(_instances.Length * 2, _instanceCount + instances.Length));
        instances.CopyTo(_instances.AsSpan(_instanceCount));
        Append(texture, material ?? Material.Default, layer, sortKey, space, _instanceCount, instances.Length);
        _instanceCount += instances.Length;
    }

    /// <summary>Draws a mesh in world space; meshes outside the visible area are skipped.</summary>
    public void DrawMesh(SpriteMesh mesh, Texture texture, Material? material, int layer, float sortKey = 0)
    {
        EnsureBuilding();
        var instances = mesh.Instances;
        if (instances.Length == 0 || !IsVisible(mesh.Bounds))
            return;
        AddCommand(new Command(layer, sortKey, _commandCount, texture, material ?? Material.Default, RenderSpace.World, 0, instances.Length, instances, mesh.Id, mesh.Version));
        MeshInstanceCount += instances.Length;
    }

    public void FillRect(in Rect2 rect, Color color, int layer, RenderSpace space = RenderSpace.World, float sortKey = 0)
    {
        var transform = Matrix3x2.CreateScale(rect.Size) * Matrix3x2.CreateTranslation(rect.Position);
        Draw(WhiteTexture, null, new SpriteInstance(transform, UnitSource, color), layer, sortKey, space);
    }

    public void DrawLine(Vector2 from, Vector2 to, float thickness, Color color, int layer, RenderSpace space = RenderSpace.World, float sortKey = 0)
    {
        var delta = to - from;
        var length = delta.Length();
        if (length <= 0)
            return;
        var transform = Matrix3x2.CreateTranslation(0, -0.5f)
            * Matrix3x2.CreateScale(length, thickness)
            * Matrix3x2.CreateRotation(MathF.Atan2(delta.Y, delta.X))
            * Matrix3x2.CreateTranslation(from);
        Draw(WhiteTexture, null, new SpriteInstance(transform, UnitSource, color), layer, sortKey, space);
    }

    public void DrawRectOutline(in Rect2 rect, float thickness, Color color, int layer, RenderSpace space = RenderSpace.World)
    {
        Span<Vector2> corners = [rect.Position, new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom)];
        DrawPolygonOutline(corners, thickness, color, layer, space);
    }

    /// <summary>Draws a closed outline through the points.</summary>
    public void DrawPolygonOutline(ReadOnlySpan<Vector2> points, float thickness, Color color, int layer, RenderSpace space = RenderSpace.World)
    {
        for (var i = 0; i < points.Length; i++)
            DrawLine(points[i], points[(i + 1) % points.Length], thickness, color, layer, space);
    }

    /// <summary>Applies a post effect after everything is drawn; effects run in the order added.</summary>
    public void AddPostEffect(PostEffect effect)
    {
        if (!effect.Enabled)
            return;
        var p = effect.Parameters;
        _postEffects.Add(new PostEffectInstance(effect.Shader, p[0], p[1], p[2], p[3]));
    }

    /// <summary>Orders draws and builds batches. Call once, after the last draw.</summary>
    public void Finish()
    {
        if (_finished)
            return;
        _finished = true;

        if (!_inOrder)
        {
            if (_sortKeys.Length < _commandCount)
                _sortKeys = new Int128[_commands.Length];
            for (var i = 0; i < _commandCount; i++)
                _sortKeys[i] = SortKey(_commands[i]);
            _sortKeys.AsSpan(0, _commandCount).Sort(_commands.AsSpan(0, _commandCount));
            if (_sortedInstances.Length < _instanceCount)
                _sortedInstances = new SpriteInstance[_instances.Length];
            var next = 0;
            for (var i = 0; i < _commandCount; i++)
            {
                ref var command = ref _commands[i];
                if (command.Mesh is not null)
                    continue;
                Array.Copy(_instances, command.First, _sortedInstances, next, command.Count);
                command.First = next;
                next += command.Count;
            }

            _sorted = true;
        }

        for (var i = 0; i < _commandCount; i++)
        {
            ref readonly var command = ref _commands[i];
            if (command.Mesh is null && _batchCount > 0)
            {
                ref var last = ref _batches[_batchCount - 1];
                if (!last.IsMesh && last.Texture == command.Texture && ReferenceEquals(last.Material, command.Material) && last.Layer == command.Layer
                    && last.Space == command.Space && last.FirstInstance + last.InstanceCount == command.First)
                {
                    last = last with { InstanceCount = last.InstanceCount + command.Count };
                    continue;
                }
            }

            if (_batchCount == _batches.Length)
                Array.Resize(ref _batches, _batches.Length * 2);
            _batches[_batchCount++] = new DrawBatch(command.Texture, command.Material, command.Layer, command.Space,
                command.First, command.Count, command.Mesh, command.MeshId, command.MeshVersion);
        }
    }

    private void Append(Texture texture, Material material, int layer, float sortKey, RenderSpace space, int first, int count)
    {
        if (_commandCount > 0)
        {
            ref var last = ref _commands[_commandCount - 1];
            if (last.Mesh is null && last.Texture == texture && ReferenceEquals(last.Material, material) && last.Layer == layer
                && last.SortKey == sortKey && last.Space == space && last.First + last.Count == first)
            {
                last.Count += count;
                return;
            }
        }

        AddCommand(new Command(layer, sortKey, _commandCount, texture, material, space, first, count, null, 0, 0));
    }

    private void AddCommand(in Command command)
    {
        if (_commandCount > 0)
        {
            ref readonly var last = ref _commands[_commandCount - 1];
            if (command.Layer < last.Layer || (command.Layer == last.Layer && command.SortKey < last.SortKey))
                _inOrder = false;
        }

        if (_commandCount == _commands.Length)
            Array.Resize(ref _commands, _commands.Length * 2);
        _commands[_commandCount++] = command;
    }

    private void EnsureBuilding()
    {
        if (_finished)
            throw new InvalidOperationException("The frame is finished; call Begin before drawing again.");
    }

    private struct Command(int layer, float sortKey, int sequence, Texture texture, Material material, RenderSpace space,
        int first, int count, SpriteInstance[]? mesh, int meshId, int meshVersion)
    {
        public readonly int Layer = layer;
        public readonly float SortKey = sortKey;
        public readonly int Sequence = sequence;
        public readonly Texture Texture = texture;
        public readonly Material Material = material;
        public readonly RenderSpace Space = space;
        public int First = first;
        public int Count = count;
        public readonly SpriteInstance[]? Mesh = mesh;
        public readonly int MeshId = meshId;
        public readonly int MeshVersion = meshVersion;
    }

    // Packs layer, sort key and sequence into one integer whose order is the draw order, so sorting needs no comparer.
    private static Int128 SortKey(in Command command)
    {
        var layer = (ulong)(uint)(command.Layer ^ int.MinValue);
        var bits = BitConverter.SingleToUInt32Bits(command.SortKey);
        var key = (bits & 0x8000_0000u) != 0 ? ~bits : bits | 0x8000_0000u;
        return new Int128((layer << 32) | key, (ulong)(uint)command.Sequence);
    }
}
