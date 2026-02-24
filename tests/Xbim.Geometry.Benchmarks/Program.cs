using BenchmarkDotNet.Running;

namespace Xbim.Geometry.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args);
    }
}
