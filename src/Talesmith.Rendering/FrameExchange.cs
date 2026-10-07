namespace Talesmith.Rendering;

/// <summary>Hands finished frames from the game thread to a render thread without either waiting for the other.</summary>
/// <remarks>
/// Three frames rotate: one being built, one ready and one being rendered. When the game produces frames faster than they are rendered,
/// older ready frames are rebuilt rather than queued, so rendering always shows the newest state. When nothing new is ready, the render
/// thread receives the frame it rendered last, so a repaint can redraw it.
/// </remarks>
public sealed class FrameExchange
{
    private readonly Lock _lock = new();
    private readonly RenderFrame[] _frames = [new(), new(), new()];
    private int _writing = -1;
    private int _ready = -1;
    private int _reading = -1;
    private int _lastRead = -1;

    /// <summary>Gets a frame to build; pass it to <see cref="Publish"/> when finished.</summary>
    public RenderFrame BeginWrite()
    {
        lock (_lock)
        {
            if (_writing >= 0)
                throw new InvalidOperationException("The previous frame has not been published.");
            for (var i = 0; i < _frames.Length; i++)
            {
                if (i != _ready && i != _reading && i != _lastRead)
                {
                    _writing = i;
                    return _frames[i];
                }
            }

            for (var i = 0; i < _frames.Length; i++)
            {
                if (i != _ready && i != _reading)
                {
                    _writing = i;
                    return _frames[i];
                }
            }

            throw new InvalidOperationException("No frame is free.");
        }
    }

    /// <summary>Makes the frame from <see cref="BeginWrite"/> the newest frame to render.</summary>
    public void Publish(RenderFrame frame)
    {
        frame.Finish();
        lock (_lock)
        {
            if (_writing < 0 || !ReferenceEquals(_frames[_writing], frame))
                throw new InvalidOperationException("The frame was not obtained from BeginWrite.");
            _ready = _writing;
            _writing = -1;
            if (_lastRead == _ready)
                _lastRead = -1;
        }
    }

    /// <summary>Whether a frame newer than the last rendered one is waiting.</summary>
    public bool HasNewFrame
    {
        get
        {
            lock (_lock)
                return _ready >= 0;
        }
    }

    /// <summary>Gets the newest frame to render, or the last rendered one when nothing new is ready; null before the first frame.</summary>
    public RenderFrame? BeginRead() => BeginRead(out _);

    /// <summary>Gets the newest frame to render, or the last rendered one when nothing new is ready; null before the first frame.</summary>
    /// <param name="isNew">Whether the frame was published since the last read, rather than read again.</param>
    public RenderFrame? BeginRead(out bool isNew)
    {
        lock (_lock)
        {
            if (_reading >= 0)
                throw new InvalidOperationException("The previous frame has not been released.");
            isNew = _ready >= 0;
            if (_ready >= 0)
            {
                _reading = _ready;
                _ready = -1;
            }
            else if (_lastRead >= 0)
            {
                _reading = _lastRead;
            }
            else
            {
                return null;
            }

            return _frames[_reading];
        }
    }

    /// <summary>Releases the frame from <see cref="BeginRead(out bool)"/>.</summary>
    public void EndRead(RenderFrame frame)
    {
        lock (_lock)
        {
            if (_reading < 0 || !ReferenceEquals(_frames[_reading], frame))
                throw new InvalidOperationException("The frame was not obtained from BeginRead.");
            _lastRead = _reading;
            _reading = -1;
        }
    }
}
