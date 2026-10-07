using System.Buffers.Binary;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Checks that SPIR-V is well formed and fits the custom fragment shader contract before a driver sees it.</summary>
internal static class SpirVInspector
{
    private const uint Magic = 0x07230203;
    private const int HeaderWords = 5;
    private const ushort OpEntryPoint = 15;
    private const ushort OpDecorate = 71;
    private const uint ExecutionModelFragment = 4;
    private const uint DecorationBinding = 33;
    private const uint DecorationDescriptorSet = 34;

    /// <summary>Returns why the code cannot be used as a custom fragment shader, or null when it can.</summary>
    public static string? FindFragmentShaderProblem(ReadOnlySpan<byte> code)
    {
        if (code.Length < HeaderWords * 4 || code.Length % 4 != 0)
            return "SPIR-V must be a whole number of 32-bit words and contain a header.";
        if (Word(code, 0) != Magic)
            return "The data does not start with the SPIR-V magic number (it may be GLSL source or big-endian).";

        var wordCount = code.Length / 4;
        var hasFragmentMain = false;
        for (var index = HeaderWords; index < wordCount;)
        {
            var first = Word(code, index);
            var length = (int)(first >> 16);
            var opcode = (ushort)first;
            if (length == 0 || index + length > wordCount)
                return $"Instruction at word {index} has an invalid length.";

            if (opcode == OpEntryPoint && length >= 4 && Word(code, index + 1) == ExecutionModelFragment)
                hasFragmentMain |= ReadString(code, index + 3, index + length) == "main";
            else if (opcode == OpDecorate && length >= 4)
            {
                var decoration = Word(code, index + 2);
                var value = Word(code, index + 3);
                if (decoration == DecorationDescriptorSet && value > 1)
                    return $"Descriptor set {value} is not available; use set 0 for the image and set 1 for the Effect block.";
                if (decoration == DecorationBinding && value != 0)
                    return $"Binding {value} is not available; use binding 0.";
            }

            index += length;
        }

        return hasFragmentMain ? null : "No fragment shader entry point named main.";
    }

    private static uint Word(ReadOnlySpan<byte> code, int index) => BinaryPrimitives.ReadUInt32LittleEndian(code[(index * 4)..]);

    private static string ReadString(ReadOnlySpan<byte> code, int startWord, int endWord)
    {
        var bytes = code[(startWord * 4)..(endWord * 4)];
        var terminator = bytes.IndexOf((byte)0);
        return System.Text.Encoding.UTF8.GetString(terminator < 0 ? bytes : bytes[..terminator]);
    }
}
