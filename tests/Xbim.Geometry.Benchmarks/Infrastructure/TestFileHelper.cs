namespace Xbim.Geometry.Benchmarks.Infrastructure;

/// <summary>
/// Resolves test file paths relative to the benchmark output directory.
/// Test files are linked from Xbim.Geometry.IntegrationTests/TestFiles/.
/// </summary>
public static class TestFileHelper
{
    /// <summary>
    /// Resolves a relative test file path (e.g. "Ifc4TestFiles/extruded-solid.ifc")
    /// to its full path under the TestFiles directory in the output folder.
    /// </summary>
    public static string Resolve(string relativePath)
    {
        // Support absolute paths for external test files
        if (Path.IsPathRooted(relativePath))
        {
            if (!File.Exists(relativePath))
                throw new FileNotFoundException($"Test file not found: {relativePath}", relativePath);
            return relativePath;
        }

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var fullPath = Path.Combine(baseDir, "TestFiles", relativePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                $"Test file not found: {fullPath}. Ensure the file is included in the IntegrationTests TestFiles directory.",
                fullPath);

        return fullPath;
    }
}
