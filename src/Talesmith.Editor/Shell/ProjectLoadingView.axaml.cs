using System.ComponentModel;
using Avalonia.Controls;
using Talesmith.Avalonia.Loading;

namespace Talesmith.Editor.Shell;

/// <summary>Shows <see cref="ProjectLoadingViewModel"/> over the workspace and fades out once the project has opened.</summary>
public partial class ProjectLoadingView : UserControl
{
    private static readonly TimeSpan FadeOutTime = TimeSpan.FromSeconds(0.25);

    private ProjectLoadingViewModel? _model;

    public ProjectLoadingView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        _model = DataContext as ProjectLoadingViewModel;
        if (_model is not null)
            _model.PropertyChanged += OnModelChanged;
        IsVisible = _model?.IsOpen == true;
    }

    private async void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectLoadingViewModel.IsOpen) || _model is not { IsOpen: false })
            return;
        IsHitTestVisible = false;
        await Fade.ToAsync(this, 0, FadeOutTime);
        IsVisible = false;
    }
}
