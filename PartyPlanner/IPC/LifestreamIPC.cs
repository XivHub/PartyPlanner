using ECommons.EzIpcManager;
using ECommons.Reflection;
using System;

namespace PartyPlanner.IPC;

#nullable disable
/// <summary>
/// Optional integration with the Lifestream plugin (NightmareXIV) for world/DC travel.
/// Lifestream is NOT a hard dependency: when it isn't installed, <see cref="Installed"/>
/// is false and every gate is a safe no-op (SafeWrapper.AnyException), so the core
/// event-browsing features work unchanged.
/// </summary>
public class LifestreamIPC
{
    public const string Name = "Lifestream";

    public LifestreamIPC() => EzIPC.Init(this, Name, SafeWrapper.AnyException);

    public static bool Installed => DalamudReflector.TryGetDalamudPlugin(Name, out _, false, true);

    /// <summary>Travel to the given world by its World sheet RowId. Returns false if not possible.</summary>
    [EzIPC] public Func<uint, bool> ChangeWorldById;

    /// <summary>Travel to the given world by name. Returns false if not possible.</summary>
    [EzIPC] public Func<string, bool> ChangeWorld;

    /// <summary>Whether the world is reachable as a cross-data-center visit.</summary>
    [EzIPC] public Func<string, bool> CanVisitCrossDC;

    /// <summary>Whether the world is reachable as a same-data-center visit.</summary>
    [EzIPC] public Func<string, bool> CanVisitSameDC;

    /// <summary>Whether Lifestream is currently busy with a task.</summary>
    [EzIPC] public Func<bool> IsBusy;
}
