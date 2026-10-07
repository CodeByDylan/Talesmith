using Talesmith.Avalonia.Hosting;

namespace Talesmith.Player;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        PlayerOptions? options;
        string? error;
        try
        {
            options = PlayerOptions.Parse(args, out error);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (options is null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(PlayerOptions.Usage);
            return 2;
        }

        if (options.ShowHelp)
        {
            Console.WriteLine(PlayerOptions.Usage);
            return 0;
        }

        try
        {
            if (options.Benchmark)
                return HeadlessBenchmark.Run(options);

            return DesktopGame.Run(new DesktopGameOptions
            {
                AssetRoot = options.AssetRoot,
                Renderer = options.Renderer,
                DeveloperTools = options.DeveloperTools,
                CaptureDirectory = options.CaptureDirectory,
                Audio = !options.Mute,
                VulkanCompositing = options.VulkanCompositing,
                MinimumLogLevel = options.LogLevel
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or TimeoutException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
