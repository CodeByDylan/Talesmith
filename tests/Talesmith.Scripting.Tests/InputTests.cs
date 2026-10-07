using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Input;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Scripting.Tests;

public sealed class InputTests
{
    [Theory]
    [InlineData(640, 400, true, 320, 180)]
    [InlineData(640, 10, false, 320, -15)]
    [InlineData(640, 780, false, 320, 370)]
    public async Task ScriptsReadTheMouseInViewUnitsAndWhetherItIsOverTheView(float x, float y, bool overView, float viewX, float viewY)
    {
        var settings = new GameSettings { View = new ViewSettings { Width = 640, Height = 360, ScaleMode = ViewScaleMode.Fit } };
        await using var test = await ScriptTestGame.StartAsync(settings: settings);
        test.Game.Viewport.Resize(new Vector2(1280, 800), 1);
        var probe = new MouseProbe();
        test.World.Create(new Transform(), new ScriptComponent(probe));

        test.Game.Services.GetRequiredService<IInputSink>().MouseMove(new Vector2(x, y));
        test.Tick(2);

        Assert.Equal(overView, probe.IsOverView);
        Assert.Equal(new Vector2(viewX, viewY), probe.ViewPosition);
    }

    private sealed class MouseProbe : Script
    {
        public bool IsOverView;

        public Vector2 ViewPosition;

        protected override void Update()
        {
            IsOverView = Input.IsMouseOverView;
            ViewPosition = Input.MouseViewPosition;
        }
    }
}
