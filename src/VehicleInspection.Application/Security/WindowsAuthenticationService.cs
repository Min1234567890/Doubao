using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using VehicleInspection.Application.Models;

namespace VehicleInspection.Application.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsAuthenticationService
{
    public WindowsAuthenticationResult AuthenticateCurrentUser(WindowsAuthenticationOptions options)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var groupNames = GetGroupNames(identity).ToArray();
        var userName = identity.Name;

        if (options.HasActiveDirectoryConfiguration && TryResolveDomain(options, out var domainName))
        {
            var activeDirectoryResult = TryCreateResult(userName, groupNames, WindowsAuthenticationMode.ActiveDirectory, domainName, options, "ActiveDirectoryIntegratedAuthentication");
            if (activeDirectoryResult != null)
            {
                return activeDirectoryResult;
            }
        }

        var localResult = TryCreateResult(userName, groupNames, WindowsAuthenticationMode.LocalWindows, Environment.MachineName, options, options.HasActiveDirectoryConfiguration ? "ADUnavailableFallbackLocal" : "LocalWindowsAuthentication");
        return localResult ?? CreateDefaultViewerResult(userName, groupNames, options.HasActiveDirectoryConfiguration ? "ADUnavailableFallbackLocal;NoMappedWindowsGroupDefaultViewer" : "NoMappedWindowsGroupDefaultViewer");
    }

    private static WindowsAuthenticationResult? TryCreateResult(string userName, IReadOnlyList<string> groupNames, WindowsAuthenticationMode mode, string authority, WindowsAuthenticationOptions options, string detail)
    {
        var candidates = new[]
        {
            new GroupRoleCandidate(Role.Admin, authority, options.AdministratorsGroupName),
            new GroupRoleCandidate(Role.Operator, authority, options.OperatorsGroupName),
            new GroupRoleCandidate(Role.Viewer, authority, options.ViewersGroupName)
        };

        foreach (var candidate in candidates)
        {
            var matchedGroups = groupNames.Where(group => IsMatchingGroup(group, candidate.Authority, candidate.GroupName, mode)).ToArray();
            if (matchedGroups.Length > 0)
            {
                return CreateResult(userName, candidate.Role, mode, groupNames, matchedGroups, detail);
            }
        }

        return null;
    }

    private static WindowsAuthenticationResult CreateDefaultViewerResult(string userName, IReadOnlyList<string> groupNames, string detail)
    {
        return CreateResult(userName, Role.Viewer, WindowsAuthenticationMode.LocalWindows, groupNames, Array.Empty<string>(), detail);
    }

    private static WindowsAuthenticationResult CreateResult(string userName, Role role, WindowsAuthenticationMode mode, IReadOnlyList<string> groupNames, IReadOnlyList<string> matchedGroups, string detail)
    {
        var session = new UserSession
        {
            UserName = userName,
            Role = role,
            AuthenticationProvider = mode.ToString(),
            IdentityName = userName,
            SecurityGroups = groupNames,
            AuthenticationDetail = detail
        };

        return new WindowsAuthenticationResult
        {
            Session = session,
            Mode = mode,
            IdentityName = userName,
            MatchedGroups = matchedGroups,
            ResultDetail = detail
        };
    }

    private static bool IsMatchingGroup(string actualGroup, string authority, string expectedGroupName, WindowsAuthenticationMode mode)
    {
        if (string.Equals(actualGroup, $@"{authority}\{expectedGroupName}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return mode == WindowsAuthenticationMode.LocalWindows
            && string.Equals(expectedGroupName, "Administrators", StringComparison.OrdinalIgnoreCase)
            && string.Equals(actualGroup, @"BUILTIN\Administrators", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetGroupNames(WindowsIdentity identity)
    {
        if (identity.Groups == null)
        {
            yield break;
        }

        foreach (var group in identity.Groups)
        {
            string? translated = null;
            try
            {
                translated = group.Translate(typeof(NTAccount)).Value;
            }
            catch (IdentityNotMappedException)
            {
            }
            catch (SystemException)
            {
            }

            if (!string.IsNullOrWhiteSpace(translated))
            {
                yield return translated;
            }
        }
    }

    private static bool TryResolveDomain(WindowsAuthenticationOptions options, out string domainName)
    {
        domainName = !string.IsNullOrWhiteSpace(options.ActiveDirectoryDomain)
            ? options.ActiveDirectoryDomain!
            : options.ActiveDirectoryServer ?? string.Empty;

        var result = DsGetDcName(null, options.ActiveDirectoryDomain, IntPtr.Zero, null, 0, out var domainControllerInfo);
        if (result != 0 || domainControllerInfo == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var info = Marshal.PtrToStructure<DomainControllerInfo>(domainControllerInfo);
            if (!string.IsNullOrWhiteSpace(info.DomainName))
            {
                domainName = info.DomainName;
            }

            return true;
        }
        finally
        {
            NetApiBufferFree(domainControllerInfo);
        }
    }

    [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DsGetDcName(string? computerName, string? domainName, IntPtr domainGuid, string? siteName, int flags, out IntPtr domainControllerInfo);

    [DllImport("Netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DomainControllerInfo
    {
        public string DomainControllerName;
        public string DomainControllerAddress;
        public int DomainControllerAddressType;
        public Guid DomainGuid;
        public string DomainName;
        public string DnsForestName;
        public int Flags;
        public string DcSiteName;
        public string ClientSiteName;
    }

    private sealed record GroupRoleCandidate(Role Role, string Authority, string GroupName);
}
