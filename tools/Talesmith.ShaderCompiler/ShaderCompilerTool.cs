using Vortice.ShaderCompiler;

namespace Talesmith.ShaderCompiler;

/// <summary>Compiles GLSL 450 vertex and fragment shaders to SPIR-V files named <c>&lt;file&gt;.spv</c> next to their sources.</summary>
internal static class ShaderCompilerTool
{
    private const string Usage = """
        Usage: dotnet run --project tools/Talesmith.ShaderCompiler [path ...]

        Compiles GLSL 450 shaders (*.vert, *.frag) to SPIR-V for Vulkan 1.0, writing <file>.spv next to each source.
        Without paths, compiles every src/**/Shaders/*.vert and *.frag file in the repository containing the current directory.
        A directory path compiles every shader in a Shaders folder below it; a file path compiles that file.

        If the bundled shaderc library cannot be loaded, compile with glslc from nixpkgs instead:
          nix shell nixpkgs#shaderc -c glslc --target-env=vulkan1.0 -O <file> -o <file>.spv
        """;

    public static int Run(string[] args)
    {
        if (args is ["-h"] or ["--help"])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        List<string> files;
        try
        {
            files = args.Length == 0 ? FindRepositoryShaders() : FindShaders(args);
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (files.Count == 0)
        {
            Console.Error.WriteLine("No shaders found.");
            return 2;
        }

        try
        {
            return Compile(files);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine($"The shaderc native library could not be loaded: {exception.Message}");
            Console.Error.WriteLine(Usage);
            return 3;
        }
    }

    private static int Compile(List<string> files)
    {
        using var compiler = new Compiler();
        var failures = 0;
        foreach (var file in files)
        {
            var options = new CompilerOptions
            {
                ShaderStage = Path.GetExtension(file) == ".vert" ? ShaderKind.VertexShader : ShaderKind.FragmentShader,
                SourceLanguage = SourceLanguage.GLSL,
                TargetEnv = TargetEnvironmentVersion.Vulkan_1_0,
                OptimizationLevel = OptimizationLevel.Performance,
                WarningsAsErrors = true
            };
            var result = compiler.Compile(File.ReadAllText(file), file, options);
            if (result.Status != CompilationStatus.Success)
            {
                failures++;
                Console.Error.WriteLine($"FAILED {file}: {result.Status}");
                Console.Error.WriteLine(result.ErrorMessage);
                continue;
            }

            var output = file + ".spv";
            var changed = !File.Exists(output) || !File.ReadAllBytes(output).AsSpan().SequenceEqual(result.Bytecode);
            if (changed)
                File.WriteAllBytes(output, result.Bytecode);
            Console.WriteLine($"{(changed ? "compiled " : "unchanged")} {output} ({result.Bytecode.Length} bytes)");
        }

        return failures == 0 ? 0 : 1;
    }

    private static List<string> FindRepositoryShaders()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;
        if (directory is null)
            throw new IOException("Run this inside the Talesmith repository or pass the paths to compile.");
        return FindShaders([Path.Combine(directory.FullName, "src")]);
    }

    private static List<string> FindShaders(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                if (!IsShader(path))
                    throw new IOException($"{path} is not a .vert or .frag file.");
                files.Add(Path.GetFullPath(path));
            }
            else if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
                {
                    if (IsShader(file) && Path.GetFileName(Path.GetDirectoryName(file)) == "Shaders")
                        files.Add(Path.GetFullPath(file));
                }
            }
            else
            {
                throw new IOException($"{path} does not exist.");
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static bool IsShader(string file) => Path.GetExtension(file) is ".vert" or ".frag";
}
