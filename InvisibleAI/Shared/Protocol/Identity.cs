using System.Diagnostics;
using System.Security.Principal;

namespace InvisibleAI.Shared.Protocol;

public static class Identity
{
    public const string HostName = "com.invisibleai.assistant";
    public static string InstanceName => "InvisibleAI-" + WindowsIdentity.GetCurrent().User!.Value + "-" + Process.GetCurrentProcess().SessionId;
    public static string PipeName => InstanceName + "-bridge-v1";
}
