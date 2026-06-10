using ECommons.Reflection;

namespace PartyPlanner.IPC;

#nullable disable
/// <summary>vnavmesh (awgil/ffxiv_navmesh) presence check. PartyPlanner does not drive navmesh directly;
/// Lifestream uses it internally for the final walk to a plot. We only need to know it's installed.</summary>
public class NavmeshIPC
{
    public const string Name = "vnavmesh";
    public static bool Installed => DalamudReflector.TryGetDalamudPlugin(Name, out _, false, true);
}
