using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace WallHack;

public class WallHack : BasePlugin
{
    public override string ModuleName => "Training Mode (X-Ray)";
    public override string ModuleVersion => "1.0.0";

    private const string WALLHACK_COMMAND = "css_wh";
    private const string UNWALLHACK_COMMAND = "css_uwh";

    // Набор команд, которые дают "рентгеновское зрение"
    private const string ENABLE_WALLHACK_CMD = "cl_ent_bbox 1; cl_ent_bbox_mode 3; r_drawclipbrushes 2; ent_bbox 1; ent_bbox_radius 200; cs2_show_ragdoll_bones 1";
    private const string DISABLE_WALLHACK_CMD = "cl_ent_bbox 0; cl_ent_bbox_mode 0; r_drawclipbrushes 0; ent_bbox 0; cs2_show_ragdoll_bones 0";

    // Хранилище активных режимов
    private Dictionary<ulong, bool> _activeWallhackTargets = new();

    public override void Load(bool hotReload)
    {
        AddCommand(WALLHACK_COMMAND, "Включает режим тренировки (X-Ray) для указанного игрока.", Command_WallHack);
        AddCommand(UNWALLHACK_COMMAND, "Выключает режим тренировки (X-Ray) для указанного игрока.", Command_UnWallHack);
        Logger.LogInformation("[WallHack] Плагин успешно загружен.");
    }

    private void Command_WallHack(CCSPlayerController? admin, CommandInfo info)
    {
        ProcessWallHackCommand(admin, info, enable: true);
    }

    private void Command_UnWallHack(CCSPlayerController? admin, CommandInfo info)
    {
        ProcessWallHackCommand(admin, info, enable: false);
    }

    private void ProcessWallHackCommand(CCSPlayerController? admin, CommandInfo info, bool enable)
    {
        string commandName = enable ? WALLHACK_COMMAND : UNWALLHACK_COMMAND;
        string actionText = enable ? "включён" : "выключен";

        if (admin == null || info.ArgCount < 2)
        {
            info.ReplyToCommand($"Использование: {commandName} <ник или SteamID64>");
            return;
        }

        string targetNameOrId = info.GetArg(1);
        CCSPlayerController? targetPlayer = FindPlayer(targetNameOrId);

        if (targetPlayer == null || !targetPlayer.IsValid)
        {
            info.ReplyToCommand($"Игрок '{targetNameOrId}' не найден.");
            return;
        }

        ulong steamId = targetPlayer.SteamID;

        if (enable)
        {
            _activeWallhackTargets[steamId] = true;
            targetPlayer.PrintToChat($"Для вас {actionText} режим тренировки (X-Ray).");
        }
        else
        {
            if (_activeWallhackTargets.ContainsKey(steamId))
            {
                _activeWallhackTargets.Remove(steamId);
                targetPlayer.PrintToChat($"Для вас {actionText} режим тренировки (X-Ray).");
            }
            else
            {
                info.ReplyToCommand($"У игрока '{targetPlayer.PlayerName}' режим X-Ray и так не активен.");
                return;
            }
        }

        ApplyEffectToPlayer(targetPlayer, enable);
        info.ReplyToCommand($"Режим X-Ray {actionText} для '{targetPlayer.PlayerName}'.");
        Logger.LogInformation($"[WallHack] {admin?.PlayerName} {(enable ? "включил" : "выключил")} X-Ray для '{targetPlayer.PlayerName}'.");
    }

    private void ApplyEffectToPlayer(CCSPlayerController player, bool enable)
    {
        if (!player.IsValid) return;

        // Отправляем команды на клиент
        player.ExecuteClientCommand(enable ? ENABLE_WALLHACK_CMD : DISABLE_WALLHACK_CMD);
    }

    private CCSPlayerController? FindPlayer(string nameOrId)
    {
        if (ulong.TryParse(nameOrId, out ulong steamId))
        {
            var playerBySteamId = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);
            if (playerBySteamId != null) return playerBySteamId;
        }

        var players = Utilities.GetPlayers();
        var foundPlayers = players.Where(p => p.IsValid && p.PlayerName.Contains(nameOrId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (foundPlayers.Count == 1)
            return foundPlayers.First();

        if (foundPlayers.Count > 1)
            Console.WriteLine($"[WallHack] Найдено несколько игроков по запросу '{nameOrId}': {string.Join(", ", foundPlayers.Select(p => p.PlayerName))}");

        return null;
    }
}