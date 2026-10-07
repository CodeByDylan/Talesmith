using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Talesmith.Benchmarks;

if (args is ["scale-scene", var folder])
{
    await ScaleScene.WriteAsync(Path.GetFullPath(folder));
    Console.WriteLine($"Wrote {Path.Combine(folder, "scale")} and {Path.Combine(folder, "scale-flyover")}");
    return;
}

// BenchmarkDotNet gives its generated project 2 minutes to build, which a busy machine can take longer than.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, ManualConfig.Create(DefaultConfig.Instance).WithBuildTimeout(TimeSpan.FromMinutes(10)));
