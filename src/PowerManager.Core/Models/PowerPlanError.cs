namespace PowerManager.Core.Models;

public enum PowerPlanErrorKind
{
    PlanNotFound,
    AccessDenied,
    Unsupported,
    PolicyRestricted,
    NativeFailure,
}

public sealed class PowerPlanException : Exception
{
    public PowerPlanException(PowerPlanErrorKind kind, string operation, int nativeError)
        : base($"Power plan operation '{operation}' failed with Win32 error {nativeError}.")
    {
        Kind = kind;
        Operation = operation;
        NativeError = nativeError;
    }

    public PowerPlanErrorKind Kind { get; }

    public string Operation { get; }

    public int NativeError { get; }
}
