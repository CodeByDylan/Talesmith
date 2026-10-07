using System.Runtime.InteropServices;
using System.Text;

namespace Talesmith.Avalonia.Hosting;

/// <summary>Keeps bundled native libraries from binding to system copies that FreeType loads globally, such as libharfbuzz.</summary>
public static class NativeLibraryIsolation
{
    private const int RtldNow = 0x2;
    private const int RtldDeepBind = 0x8;

    private static bool _applied;

    /// <summary>Loads the bundled HarfBuzz with its own symbols first and every symbol bound; call before Avalonia starts.</summary>
    public static void Apply()
    {
        if (_applied || !OperatingSystem.IsLinux())
            return;
        _applied = true;
        if (FindBundled("libHarfBuzzSharp.so") is not { } path)
            return;
        try
        {
            NativeMethods.DlOpen(Encoding.UTF8.GetBytes(path + '\0'), RtldNow | RtldDeepBind);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    private static string? FindBundled(string fileName)
    {
        var directories = (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(AppContext.BaseDirectory)
            .Append(Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"));
        return directories.Select(d => Path.Combine(d, fileName)).FirstOrDefault(File.Exists);
    }

    private static class NativeMethods
    {
        [DllImport("libc.so.6", EntryPoint = "dlopen")]
        public static extern IntPtr DlOpen(byte[] fileName, int flags);
    }
}
