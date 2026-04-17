using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace BotPlace;

public class BotPlace : BasePlugin
{
    public override string ModuleName => "Bot Place";
    public override string ModuleVersion => "1.2.2";

    private const int SAFE_MIMIC = 10000;

    public override void Load(bool hotReload)
    {
        AddCommand("css_copybot_nearp", "Copy player position & angle to nearest bot", Command_CopyNearestBotp);        
        AddCommand("css_copybot_nearm", "Copy player position & angle to nearest bot", Command_CopyNearestBotm);
        AddCommand("css_copybot_all", "Copy player position & angle to all bots", Command_CopyAllBots);
    }

    private void Command_CopyNearestBotp(CCSPlayerController? client, CommandInfo info)
    {


        if (client == null || !client.IsValid || client.PlayerPawn?.IsValid != true)
        {
            client?.PrintToChat("You must be alive to use this command.");
            return;
        }

       
        int mimicValue = (client.UserId.HasValue && client.UserId > 0) ? client.UserId.Value + 1 : client.Slot + 1;
        Server.ExecuteCommand($"bot_mimic {mimicValue}");

    }
private void Command_CopyNearestBotm(CCSPlayerController? client, CommandInfo info)
    {
        if (client == null || !client.IsValid || client.PlayerPawn?.IsValid != true)
        {
            client?.PrintToChat("You must be alive to use this command.");
            return;
        }

        var pawn = client.PlayerPawn.Value;
        var pos = pawn.AbsOrigin!;
        var ang = pawn.AbsRotation!;

        var bots = Utilities.GetPlayers()
            .Where(p => p.IsValid && p.IsBot && p.PlayerPawn?.IsValid == true)
            .ToList();

        if (bots.Count == 0)
        {
            client.PrintToChat("No bots on the server.");
            return;
        }

        CCSPlayerController? nearest = null;
        var minDist = float.MaxValue;
        foreach (var bot in bots)
        {
            var botPos = bot.PlayerPawn!.Value!.AbsOrigin!;
            var dist = (botPos - pos).Length();
            if (dist < minDist)
            {
                minDist = dist;
                nearest = bot;
            }
        }

        if (nearest == null) return;
        int mimicValue = (client.UserId.HasValue && client.UserId > 0) ? client.UserId.Value + 1 : client.Slot + 1;
  
        Server.ExecuteCommand($"bot_mimic {SAFE_MIMIC}");
        nearest.PlayerPawn!.Value!.Teleport(pos, ang, new Vector(0, 0, 0));




        client.PrintToChat($"Bot {nearest.PlayerName} copied your position & angle.");
    }


    private void Command_CopyAllBots(CCSPlayerController? client, CommandInfo info)
    {
        if (client == null || !client.IsValid || client.PlayerPawn?.IsValid != true)
        {
            client?.PrintToChat("You must be alive to use this command.");
            return;
        }

        var pawn = client.PlayerPawn.Value;
        var pos = pawn.AbsOrigin!;
        var ang = pawn.AbsRotation!;

        var bots = Utilities.GetPlayers()
            .Where(p => p.IsValid && p.IsBot && p.PlayerPawn?.IsValid == true)
            .ToList();

        if (bots.Count == 0)
        {
            client.PrintToChat("No bots on the server.");
            return;
        }

        int oldMimic = GetBotMimic();
        int mimicValue = (client.UserId.HasValue && client.UserId > 0) ? client.UserId.Value : client.Slot + 1;
        Server.ExecuteCommand($"bot_mimic {mimicValue}");

        foreach (var bot in bots)
        {
            bot.PlayerPawn!.Value!.Teleport(pos, ang, new Vector(0, 0, 0));
        }

        if (oldMimic == 0)
            Server.ExecuteCommand($"bot_mimic {SAFE_MIMIC}");
        else
            Server.ExecuteCommand($"bot_mimic {oldMimic}");

        client.PrintToChat($"Copied position & angle to {bots.Count} bot(s).");
    }

    private int GetBotMimic()
    {
        var cvar = ConVar.Find("bot_mimic");
        return cvar?.GetPrimitiveValue<int>() ?? 0;
    }
}