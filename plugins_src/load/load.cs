using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace KeepCheatsOn;

public class KeepCheatsOn : BasePlugin
{
    public override string ModuleName => "Keep Cheats On";
    public override string ModuleVersion => "1.0.0";
    public override void Load(bool hotReload)
    {

        RegisterEventHandler<EventPlayerConnectFull>((@event, info) =>
        {
            Server.ExecuteCommand("exec train");
            Console.WriteLine("[KeepCheatsOn] sv_cheats restored to 1 after player join.");

            return HookResult.Continue;
        });
    }
}