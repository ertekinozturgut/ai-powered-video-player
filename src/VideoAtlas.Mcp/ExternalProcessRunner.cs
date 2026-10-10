using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VideoAtlas.Mcp;

public sealed class ExternalProcessRunner(ILogger<ExternalProcessRunner> logger)
{
    public async Task<(string Output, string Error)> RunAsync(
        string executable, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            if (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException($"{executable} exceeded the {timeout.TotalMinutes:0}-minute time limit.");
            throw;
        }

        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
        {
            logger.LogWarning("Process {Executable} exited with code {ExitCode}: {Error}", executable, process.ExitCode, Safe(error));
            throw new InvalidOperationException($"{Path.GetFileName(executable)} failed with exit code {process.ExitCode}: {Safe(error)}");
        }
        return (output, error);
    }

    private static string Safe(string value)
    {
        var clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 1000 ? clean : clean[..1000];
    }
}
