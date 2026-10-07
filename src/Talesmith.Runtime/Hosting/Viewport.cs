using System.Numerics;
using Talesmith.Rendering;

namespace Talesmith.Runtime.Hosting;

/// <summary>The area the game renders into and where its view lies inside it, kept up to date by the host from any thread.</summary>
/// <remarks>Hosts set <see cref="Size"/> and <see cref="DisplayScale"/>; the game thread reads <see cref="Layout"/>, which follows them.</remarks>
public sealed class Viewport
{
    private readonly Lock _lock = new();
    private Vector2 _size = new(1280, 800);
    private float _displayScale = 1;
    private ViewLayout _layout;
    private ViewLayout _unscaledLayout;

    /// <summary>Creates a viewport for games without view settings, which draw on the whole target in logical pixels.</summary>
    public Viewport()
        : this(ViewSettings.Unscaled)
    {
    }

    public Viewport(ViewSettings view)
    {
        ArgumentNullException.ThrowIfNull(view);
        View = view;
        UpdateLayouts();
    }

    /// <summary>The design size and scale mode the layout follows.</summary>
    public ViewSettings View { get; }

    /// <summary>The render target size in device pixels.</summary>
    public Vector2 Size
    {
        get
        {
            lock (_lock)
                return _size;
        }
        set
        {
            lock (_lock)
            {
                _size = Vector2.Max(value, Vector2.One);
                UpdateLayouts();
            }
        }
    }

    /// <summary>Device pixels per logical pixel, such as 2 on a high-density display.</summary>
    public float DisplayScale
    {
        get
        {
            lock (_lock)
                return _displayScale;
        }
        set
        {
            lock (_lock)
            {
                _displayScale = value;
                UpdateLayouts();
            }
        }
    }

    /// <summary>Where the game is drawn inside the render target, following <see cref="View"/>.</summary>
    public ViewLayout Layout
    {
        get
        {
            lock (_lock)
                return _layout;
        }
    }

    /// <summary>The layout of <see cref="ViewScaleMode.None"/>: the whole target in logical pixels, for free cameras such as the editor's.</summary>
    public ViewLayout UnscaledLayout
    {
        get
        {
            lock (_lock)
                return _unscaledLayout;
        }
    }

    /// <summary>Sets the size and display scale together, so the game never sees one without the other.</summary>
    public void Resize(Vector2 size, float displayScale)
    {
        lock (_lock)
        {
            _size = Vector2.Max(size, Vector2.One);
            _displayScale = displayScale;
            UpdateLayouts();
        }
    }

    private void UpdateLayouts()
    {
        _layout = ViewLayout.Compute(_size, _displayScale, View);
        _unscaledLayout = ViewLayout.Compute(_size, _displayScale, ViewSettings.Unscaled);
    }
}
