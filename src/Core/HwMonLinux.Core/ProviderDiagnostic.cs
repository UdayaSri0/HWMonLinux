using System;

namespace HwMonLinux.Core;

public enum ProviderStatus
{
    Unavailable,
    Success,
    Error
}

public sealed record ProviderDiagnostic(
    string Name,
    ProviderStatus Status,
    int ReadingCount,
    string? Message = null,
    TimeSpan? Duration = null)
{
    public bool IsError => Status == ProviderStatus.Error;
}
