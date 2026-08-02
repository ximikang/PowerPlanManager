using System.Runtime.InteropServices;

namespace PowerManager.App.Native;

internal static partial class PowerNativeMethods
{
    internal const uint ErrorSuccess = 0;
    internal const uint ErrorAccessDenied = 5;
    internal const uint ErrorInvalidParameter = 87;
    internal const uint ErrorMoreData = 234;
    internal const uint ErrorNoMoreItems = 259;
    internal const uint ErrorNotFound = 1168;
    internal const uint ErrorAccessDisabledByPolicy = 1260;
    internal const uint AccessScheme = 16;

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerEnumerate(
        nint rootPowerKey,
        nint schemeGuid,
        nint subgroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        [Out] byte[] buffer,
        ref uint bufferSize);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerGetActiveScheme(nint rootPowerKey, out nint activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerSetActiveScheme(nint rootPowerKey, in Guid schemeGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerDuplicateScheme(
        nint rootPowerKey,
        in Guid sourceSchemeGuid,
        out nint destinationSchemeGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerReadFriendlyName(
        nint rootPowerKey,
        in Guid schemeGuid,
        nint subgroupOfPowerSettingsGuid,
        nint powerSettingGuid,
        nint buffer,
        ref uint bufferSize);

    [LibraryImport("kernel32.dll")]
    internal static partial nint LocalFree(nint memory);
}
