using System.Runtime.InteropServices;

namespace HwMonLinux.Providers.Abstractions;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken);

    ValueTask<bool> IsAvailableAsync(string fileName, CancellationToken cancellationToken);
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;

    public static ProcessResult FromException(Exception ex) =>
        new(-1, string.Empty, ex.Message);
}
