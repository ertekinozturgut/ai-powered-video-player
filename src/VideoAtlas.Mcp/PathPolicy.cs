namespace VideoAtlas.Mcp;

public sealed class PathPolicy
{
    private readonly string[] roots;
    private readonly TimeProvider clock;
    private readonly StringComparison comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public PathPolicy(IEnumerable<string> allowedRoots, TimeProvider clock)
    {
        this.clock = clock;
        roots = allowedRoots.Select(Path.GetFullPath).Distinct(StringComparerForPlatform()).ToArray();
        if (roots.Length == 0) throw new InvalidOperationException("At least one allowed root is required.");
    }

    public IReadOnlyList<string> AllowedRoots => roots;

    public static PathPolicy FromEnvironment(TimeProvider clock)
    {
        var configured = Environment.GetEnvironmentVariable("VIDEOATLAS_MCP_ALLOWED_ROOTS");
        var values = string.IsNullOrWhiteSpace(configured)
            ? [Environment.CurrentDirectory]
            : configured.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new(values, clock);
    }

    public string ReadFile(string path)
    {
        var full = EnsureAllowed(path);
        if (!File.Exists(full)) throw new FileNotFoundException("The requested media or transcript file does not exist.", full);
        return full;
    }

    public string NewOutputDirectory(string? requested, string sourcePath)
    {
        var full = string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(Path.GetDirectoryName(sourcePath)!, ".videoatlas-mcp",
                $"{Path.GetFileNameWithoutExtension(sourcePath)}-{clock.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}")
            : EnsureAllowed(requested);
        full = EnsureAllowed(full);
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            throw new IOException("The output directory must be new or empty to prevent overwriting existing analysis files.");
        Directory.CreateDirectory(full);
        return full;
    }

    public string WriteFile(string path)
    {
        var full = EnsureAllowed(path);
        var parent = Path.GetDirectoryName(full) ?? throw new IOException("The output path has no parent directory.");
        Directory.CreateDirectory(parent);
        return full;
    }

    private string EnsureAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        var full = Path.GetFullPath(path);
        if (!roots.Any(root => IsWithin(root, full)))
            throw new UnauthorizedAccessException($"Path is outside VIDEOATLAS_MCP_ALLOWED_ROOTS: {full}");
        return full;
    }

    private bool IsWithin(string root, string candidate) =>
        candidate.Equals(root, comparison) || candidate.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);

    private static StringComparer StringComparerForPlatform() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
