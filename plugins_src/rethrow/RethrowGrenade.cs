using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;

namespace RethrowGrenade;

public class RethrowGrenade : BasePlugin
{
    public override string ModuleName => "Rethrow Grenade";
    public override string ModuleVersion => "1.0.2";

    public override void Load(bool hotReload)
    {
        AddCommand("css_rethrow", "Повторить последний бросок гранаты", Command_Rethrow);
    }

    private void Command_Rethrow(CCSPlayerController? client, CommandInfo info)
    {
        if (client == null || !client.IsValid)
            return;

        // Выполняем серверную команду (без привязки к клиенту)
        Server.ExecuteCommand("sv_rethrow_last_grenade");

        // Опционально: сообщение игроку
        client.PrintToChat("Граната повторена!");
    }
}