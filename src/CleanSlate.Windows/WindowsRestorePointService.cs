using CleanSlate.Core.Interfaces;

namespace CleanSlate.Windows;

/// <summary>
/// Creates a System Restore point through SRSetRestorePointW. Failure is never fatal: the caller reports it and
/// the user decides whether to continue. Windows itself may skip creation if one was made in the last 24 hours.
/// </summary>
public sealed class WindowsRestorePointService : IRestorePointService
{
    private readonly IPrivilegeInfo _privileges;

    public WindowsRestorePointService(IPrivilegeInfo privileges)
    {
        _privileges = privileges;
    }

    public Task<RestorePointResult> TryCreateAsync(string description, CancellationToken cancellationToken)
    {
        if (!_privileges.IsElevated)
        {
            return Task.FromResult(new RestorePointResult(false,
                "No restore point was created: Windows only allows restore points from an administrator session. A verified backup of each repaired entry is still saved."));
        }

        return Task.Run(() => Create(description), cancellationToken);
    }

    private static RestorePointResult Create(string description)
    {
        try
        {
            var begin = new NativeMethods.RESTOREPOINTINFOW
            {
                dwEventType = NativeMethods.BeginSystemChange,
                dwRestorePtType = NativeMethods.ModifySettings,
                szDescription = description.Length > 255 ? description[..255] : description
            };

            if (!NativeMethods.SRSetRestorePointW(ref begin, out var status) || status.nStatus != 0)
            {
                return new RestorePointResult(false, status.nStatus == NativeMethods.ErrorServiceDisabled
                    ? "No restore point was created: System Protection is turned off for this PC."
                    : $"No restore point was created (Windows status {status.nStatus}).");
            }

            var end = new NativeMethods.RESTOREPOINTINFOW
            {
                dwEventType = NativeMethods.EndSystemChange,
                dwRestorePtType = NativeMethods.ModifySettings,
                llSequenceNumber = status.llSequenceNumber,
                szDescription = string.Empty
            };
            NativeMethods.SRSetRestorePointW(ref end, out _);
            return new RestorePointResult(true, "A restore point was requested. Windows may skip it if another was created within the last 24 hours.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.Runtime.InteropServices.MarshalDirectiveException)
        {
            return new RestorePointResult(false, "Restore points are not available on this system.");
        }
    }
}
