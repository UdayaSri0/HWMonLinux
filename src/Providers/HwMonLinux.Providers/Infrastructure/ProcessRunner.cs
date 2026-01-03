using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using HwMonLinux.Providers.Abstractions;

namespace HwMonLinux.Providers.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly TimeSpan _timeout;

    public ProcessRunner(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stdoutBuilder.AppendLine(args.Data);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stderrBuilder.AppendLine(args.Data);
                }
            };

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(_timeout);

            if (!process.Start())
            {
                return new ProcessResult(-1, string.Empty, $"Failed to start {fileName}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Ignore issues when trying to kill the process.
                    }
                }

                throw;
            }

            return new ProcessResult(
                process.ExitCode,
                stdoutBuilder.ToString(),
                stderrBuilder.ToString());
        }
        catch (Exception ex)
        {
            return ProcessResult.FromException(ex);
        }
    }

    public ValueTask<bool> IsAvailableAsync(string fileName, CancellationToken cancellationToken)
    {
        if (Path.IsPathRooted(fileName))
        {
            return new ValueTask<bool>(File.Exists(fileName));
        }

        var searchPaths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in searchPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(path, fileName);
            if (File.Exists(fullPath) || (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(fullPath + ".exe")))
            {
                return new ValueTask<bool>(true);
            }
        }

        return new ValueTask<bool>(false);
    }
}
