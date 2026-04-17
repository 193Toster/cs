using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace GodMode;

public class GodMode : BasePlugin
{
    public override string ModuleName => "God Mode";
    public override string ModuleVersion => "1.0.0";

    private Dictionary<ulong, bool> playerGod = new Dictionary<ulong, bool>();
    private bool botsGod = false;

    public override void Load(bool hotReload)
    {
        AddCommand("css_god", "Toggle god mode for yourself", Command_God);
        AddCommand("css_bot_gods", "Toggle god mode for all bots", Command_BotGods);

        RegisterEventHandler<EventPlayerHurt>((@event, info) =>
        {
            var victim = @event.Userid;
            var attacker = @event.Attacker;
            int damageHealth = @event.DmgHealth;
            int damageArmor = @event.DmgArmor;

            if (victim?.IsValid != true) return HookResult.Continue;

            // Боты
            if (victim.IsBot && botsGod)
            {
                var victimPawn = victim.PlayerPawn?.Value;
                if (victimPawn?.IsValid == true)
                {
                    victimPawn.Health = 100;
                }
                if (attacker?.IsValid == true && !attacker.IsBot)
                {
                    attacker.PrintToChat($"Бот {victim.PlayerName} получил урон: {damageHealth}");
                }
                return HookResult.Continue;
            }

            // Игроки
            if (!victim.IsBot && playerGod.ContainsKey(victim.SteamID) && playerGod[victim.SteamID])
            {
                var victimPawn = victim.PlayerPawn?.Value;
                if (victimPawn?.IsValid == true)
                {
                    victimPawn.Health = 100;
                }
                victimPawn.ArmorValue = 100;

                if (attacker?.IsValid == true && !attacker.IsBot)
                {
                    attacker.PrintToChat($"Игрок {victim.PlayerName} получил урон: {damageHealth}");
                }
		if (victim?.IsValid == true && attacker != victim) {
                    victim.PrintToChat($"Получен урон: {damageHealth} от {attacker.PlayerName}");
                }
                return HookResult.Continue;
            }

            // Обычный урон – показываем атакующему
            if (attacker?.IsValid == true && !attacker.IsBot)
            {
                attacker.PrintToChat($"Игрок {victim.PlayerName} получил урон: {damageHealth}");
            }

	    if (victim?.IsValid == true && !victim.IsBot && attacker != victim) {
                victim.PrintToChat($"Получен урон: {damageHealth} от {attacker.PlayerName}");
            }
            return HookResult.Continue;
        });
    }

    private void Command_God(CCSPlayerController? client, CommandInfo info)
    {
        if (client == null || !client.IsValid)
        {
            info.ReplyToCommand("Эта команда доступна только игрокам.");
            return;
        }

        ulong steamId = client.SteamID;
        bool current = playerGod.GetValueOrDefault(steamId);
        playerGod[steamId] = !current;
        client.PrintToChat(playerGod[steamId] ? "Режим бога ВКЛЮЧЁН" : "Режим бога ВЫКЛЮЧЁН");
    }

    private void Command_BotGods(CCSPlayerController? client, CommandInfo info)
    {
        botsGod = !botsGod;
        string msg = botsGod ? "Бессмертие для ботов ВКЛЮЧЕНО" : "Бессмертие для ботов ВЫКЛЮЧЕНО";
        if (client != null && client.IsValid)
            client.PrintToChat(msg);
        else
            Console.WriteLine(msg);
    }
}