using NotchHub.Core;
using NotchHub.Core.Contracts;
using NotchHub.Platform.Machine.Interop;

namespace NotchHub.Platform.Machine.Power;

/// <summary>Lock, sleep, hibernate, sign out, restart and shut down through Win32 (shutdown privilege enabled on demand).</summary>
public sealed unsafe class PowerService : IPowerService
{
    private readonly Lazy<(bool Sleep, bool Hibernate, bool ModernStandby)> _caps = new(QueryCapabilities);

    public bool IsSupported(PowerAction action) => action switch
    {
        PowerAction.Sleep => _caps.Value.Sleep || _caps.Value.ModernStandby,
        PowerAction.Hibernate => _caps.Value.Hibernate,
        _ => true,
    };

    public Task<bool> ExecuteAsync(PowerAction action)
    {
        if (!IsSupported(action)) return Task.FromResult(false);
        return Task.Run(() =>
        {
            Log.Info($"Power: {action}");
            try
            {
                return action switch
                {
                    PowerAction.Lock => User32.LockWorkStation(),
                    PowerAction.Sleep => Sleep(),
                    PowerAction.Hibernate => PowrProf.SetSuspendState(1, 0, 0) != 0,
                    PowerAction.SignOut => User32.ExitWindowsEx(User32.EWX_LOGOFF, Reason),
                    PowerAction.Restart => EnableShutdownPrivilege() && User32.ExitWindowsEx(User32.EWX_REBOOT, Reason),
                    PowerAction.Shutdown => EnableShutdownPrivilege()
                                            && User32.ExitWindowsEx(User32.EWX_SHUTDOWN | User32.EWX_POWEROFF | User32.EWX_HYBRID_SHUTDOWN, Reason),
                    _ => false,
                };
            }
            catch (Exception ex)
            {
                Log.Error($"Power: {action} failed", ex);
                return false;
            }
        });
    }

    private static uint Reason => User32.SHTDN_REASON_MAJOR_OTHER | User32.SHTDN_REASON_MINOR_OTHER | User32.SHTDN_REASON_FLAG_PLANNED;

    private bool Sleep()
    {
        if (_caps.Value.Sleep) return PowrProf.SetSuspendState(0, 0, 0) != 0;
        // Modern Standby (S0ix) machines enter standby when the display turns off.
        return User32.PostMessage(User32.HWND_BROADCAST, User32.WM_SYSCOMMAND, User32.SC_MONITORPOWER, 2);
    }

    private static (bool, bool, bool) QueryCapabilities()
    {
        var buffer = stackalloc byte[PowrProf.PowerCapabilitiesBufferSize];
        new Span<byte>(buffer, PowrProf.PowerCapabilitiesBufferSize).Clear();
        try
        {
            if (PowrProf.GetPwrCapabilities(buffer) == 0) return (true, false, false);
            var s3 = buffer[PowrProf.OffsetSystemS3] != 0 || buffer[PowrProf.OffsetSystemS1] != 0 || buffer[PowrProf.OffsetSystemS2] != 0;
            var s4 = buffer[PowrProf.OffsetSystemS4] != 0 && buffer[PowrProf.OffsetHiberFilePresent] != 0;
            var aoac = buffer[PowrProf.OffsetAoAc] != 0;
            return (s3, s4, aoac);
        }
        catch (Exception ex)
        {
            Log.Warn("Power: capabilities unavailable", ex);
            return (true, false, false);
        }
    }

    private static bool EnableShutdownPrivilege()
    {
        if (!AdvApi32.OpenProcessToken(Kernel32.GetCurrentProcess(), AdvApi32.TOKEN_ADJUST_PRIVILEGES | AdvApi32.TOKEN_QUERY, out var token)) return false;
        try
        {
            if (!AdvApi32.LookupPrivilegeValue(null, AdvApi32.SE_SHUTDOWN_NAME, out var luid)) return false;
            var privileges = new AdvApi32.TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = AdvApi32.SE_PRIVILEGE_ENABLED };
            return AdvApi32.AdjustTokenPrivileges(token, false, &privileges, 0, 0, 0)
                   && System.Runtime.InteropServices.Marshal.GetLastPInvokeError() != AdvApi32.ERROR_NOT_ALL_ASSIGNED;
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }
}
