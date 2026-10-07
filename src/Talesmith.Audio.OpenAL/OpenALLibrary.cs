using Silk.NET.Core.Loader;
using Silk.NET.OpenAL;

namespace Talesmith.Audio.OpenAL;

/// <summary>Loads the OpenAL Soft library that ships with the application.</summary>
internal static class OpenALLibrary
{
    static OpenALLibrary()
    {
        // Silk.NET probes runtimes/<distro-specific RID>/native, which misses the linux-x64 folder on many distributions.
        if (PathResolver.Default is DefaultPathResolver resolver)
            resolver.Resolvers.Add(InNativeSearchDirectories);
    }

    /// <exception cref="AudioDeviceException">The library could not be loaded.</exception>
    public static (ALContext Context, AL Al) Load()
    {
        try
        {
            return (ALContext.GetApi(soft: true), AL.GetApi(soft: true));
        }
        catch (Exception ex) when (ex is DllNotFoundException or FileNotFoundException or BadImageFormatException)
        {
            throw new AudioDeviceException("The OpenAL Soft library could not be loaded.", ex);
        }
    }

    private static IEnumerable<string> InNativeSearchDirectories(string name)
    {
        var directories = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string ?? string.Empty;
        foreach (var directory in directories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(directory, name);
    }
}
