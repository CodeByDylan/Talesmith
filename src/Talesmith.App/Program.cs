using Avalonia;
using Talesmith.App;
using Talesmith.Avalonia.Hosting;
using Talesmith.Build;

NativeLibraryIsolation.Apply();

if (BuildCommandLine.Handles(args))
    return BuildCommandLine.RunAsync(args, Console.Out, Console.Error).GetAwaiter().GetResult();

// Vulkan presents through the window's compositor; FIFO keeps frames in step with the display, as the player does.
if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("MESA_VK_WSI_PRESENT_MODE") is null)
    Environment.SetEnvironmentVariable("MESA_VK_WSI_PRESENT_MODE", "fifo");

var builder = AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Vulkan, X11RenderingMode.Glx, X11RenderingMode.Software] })
    .WithInterFont()
    .LogToTrace();

#if DEBUG
builder = builder.WithDeveloperTools();
#endif

return builder.StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
