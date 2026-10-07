using System.Runtime.InteropServices;

namespace Talesmith.Avalonia.Hosting;

/// <summary>Asks Mesa's Vulkan drivers to present windows in FIFO mode, so the window's compositor waits for each display refresh.</summary>
/// <remarks>Other drivers ignore <see cref="Variable"/>; <c>FrameLoop</c> detects compositors that still outpace the display.</remarks>
internal static partial class PresentMode
{
    public const string Variable = "MESA_VK_WSI_PRESENT_MODE";

    /// <summary>Sets <see cref="Variable"/> to "fifo" unless it is already set, so a player can still choose another mode.</summary>
    /// <returns>Whether the variable was set; call before Avalonia starts.</returns>
    public static bool PreferFifo()
    {
        if (!OperatingSystem.IsLinux() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
            return false;

        // .NET keeps its own copy of the environment on Unix, which native code does not see, so the variable goes through libc.
        if (SetEnvironmentVariable(Utf8(Variable), Utf8("fifo"), 0) != 0)
            return false;
        Environment.SetEnvironmentVariable(Variable, "fifo");
        return true;
    }

    private static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text + "\0");

    /// <summary>libc setenv, with null-terminated UTF-8 name and value.</summary>
    [DllImport("libc", EntryPoint = "setenv", ExactSpelling = true)]
    private static extern int SetEnvironmentVariable(byte[] name, byte[] value, int overwrite);
}
