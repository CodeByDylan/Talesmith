using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Hub;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Settings;

namespace Talesmith.Editor.Tests.Hub;

public sealed class HubViewTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "talesmith-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void TheNewProjectPageShowsAllOfItsTextInTheSmallestWindow() => Headless.Run(() =>
    {
        using var application = new ServiceCollection()
            .AddTalesmithEditorApplication(new JsonSettingsService(null), new HeadlessGameSessionFactory())
            .BuildServiceProvider();
        var hub = application.GetRequiredService<HubViewModel>();
        hub.ShowNewProjectCommand.Execute(null);
        hub.Location = _folder;
        Directory.CreateDirectory(hub.ProjectPath);
        File.WriteAllText(Path.Combine(hub.ProjectPath, "notes.txt"), "");
        var window = new Window { Width = 860, Height = 560, Content = new HubView { DataContext = hub } };
        window.Show();
        try
        {
            window.UpdateLayout();
            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && t.TextTrimming == TextTrimming.None && !string.IsNullOrEmpty(t.Text))
                .ToList();
            Assert.Contains(texts, t => t.Text == "Top-down hex adventure");
            Assert.Contains(texts, t => t.Text == hub.ValidationError);
            // A wrapped line's trailing space may hang past the edge, which hides nothing.
            foreach (var text in texts)
                Assert.True(text.TextLayout.Width <= text.Bounds.Width + 1, $"\"{text.Text}\" is cut off");
        }
        finally
        {
            window.Close();
        }
    });
}
