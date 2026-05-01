using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;

using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace BotPlacerSnapView;

[MinimumApiVersion(367)]
public class BotPlacerPlugin : BasePlugin
{
    public override string ModuleName => "Bot Placer (SnapViewAngles)";
    public override string ModuleVersion => "11.1.0";

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[BotPlacer] Loaded with SnapViewAngles and slot argument.");
        AddCommand("css_bot_place", "Place a bot at your location. Usage: css_bot_place [slot]", OnBotPlaceCommand);
    }

    private void OnBotPlaceCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot || player.TeamNum < (int)CsTeam.Terrorist)
        {
            info.ReplyToCommand("This command can only be used by a human player on a team.");
            return;
        }

        var playerPawn = player.PlayerPawn.Value;
        if (playerPawn == null || !playerPawn.IsValid)
        {
            info.ReplyToCommand("Could not get your player pawn.");
            return;
        }

        // Crouch state
        bool isCrouching = false;
        if (playerPawn.MovementServices != null)
        {
            var movementService = new CCSPlayer_MovementServices(playerPawn.MovementServices.Handle);
            isCrouching = (int)movementService.DuckAmount == 1;
        }

        var position = playerPawn.AbsOrigin!;
        QAngle bodyAngle = new QAngle(0, playerPawn.EyeAngles.Y, 0);
        QAngle viewAngle = playerPawn.EyeAngles;

        // Try to parse a slot argument
        string arg = info.GetArg(1);
        if (!string.IsNullOrEmpty(arg) && int.TryParse(arg, out int slot))
        {
            // Find the player (bot or human) with that slot
            var target = Utilities.GetPlayers().FirstOrDefault(p => p.Slot == slot);
            if (target == null)
            {
                info.ReplyToCommand($"No player found in slot {slot}.");
                return;
            }
            if (!target.IsBot)
            {
                info.ReplyToCommand("The specified player is not a bot. Only bots can be placed.");
                return;
            }
            // Use the existing bot
            if (target.PlayerPawn.Value != null && target.PlayerPawn.Value.IsValid)
            {
                SetupBot(target, position, bodyAngle, viewAngle, isCrouching);
                info.ReplyToCommand($"Bot {target.PlayerName} placed at your location.");
            }
            else
            {
                info.ReplyToCommand("The bot's pawn is invalid.");
            }
        }
        else
        {
            // No valid slot argument – create a new bot
            CreateBot(player.Team, bot =>
            {
                if (bot == null)
                {
                    info.ReplyToCommand("Failed to create bot.");
                    return;
                }
                AddTimer(0.15f, () =>
                {
                    if (bot.PlayerPawn.Value != null && bot.PlayerPawn.Value.IsValid)
                        SetupBot(bot, position, bodyAngle, viewAngle, isCrouching);
                    else
                        Console.WriteLine("[BotPlacer] Bot pawn not ready.");
                });
            });
        }
    }

    private void CreateBot(CsTeam team, Action<CCSPlayerController?> callback)
    {
        var botsBefore = Utilities.GetPlayers().Where(p => p.IsBot).ToList();
        Server.ExecuteCommand(team == CsTeam.CounterTerrorist ? "bot_add_t" : "bot_add_ct");
        AddTimer(0.1f, () =>
        {
            var botsAfter = Utilities.GetPlayers().Where(p => p.IsBot).ToList();
            callback(botsAfter.Except(botsBefore).FirstOrDefault());
        });
    }

    private void SetupBot(CCSPlayerController bot, Vector position, QAngle bodyAngle, QAngle viewAngle, bool crouch)
    {
        var botPawn = bot.PlayerPawn.Value;
        if (botPawn == null || !botPawn.IsValid) return;

        // 1. Teleport body upright (yaw only)
        botPawn.Teleport(position, bodyAngle, new Vector(0, 0, 0));
        Console.WriteLine("[BotPlacer] SnapViewAngles about to be calles");

        // 2. Snap view angles on next frame
        //Server.NextFrame(() =>
        //{
            //if (!botPawn.IsValid) return;
            SnapViewAngles(botPawn, viewAngle);
        //});

        // 3. Crouch
        if (crouch)
        {
            AddTimer(0.2f, () =>
            {
                if (!botPawn.IsValid) return;
                var movementService = new CCSPlayer_MovementServices(botPawn.MovementServices!.Handle);
                movementService.DuckAmount = 1;
                if (botPawn.Bot != null) botPawn.Bot.IsCrouching = true;
                Console.WriteLine("[BotPlacer] Crouch applied.");
            });
        }
    }

    private void SnapViewAngles(CCSPlayerPawn pawn, QAngle angles)
    {
        var sig = GameData.GetSignature("SnapViewAngles");
        Console.WriteLine("[BotPlacer] SnapViewAngles called");

        if (string.IsNullOrEmpty(sig))
        {
            Console.WriteLine("[BotPlacer] SnapViewAngles signature not found in gamedata.json.");
            return;
        }

        try
        {
            var snap = VirtualFunction.CreateVoid<nint, nint>(sig);
            IntPtr ptr = Marshal.AllocHGlobal(12);
            Marshal.WriteInt32(ptr + 0, BitConverter.SingleToInt32Bits(angles.X));
            Marshal.WriteInt32(ptr + 4, BitConverter.SingleToInt32Bits(angles.Y));
            Marshal.WriteInt32(ptr + 8, BitConverter.SingleToInt32Bits(angles.Z));
            snap(pawn.Handle, ptr);
            Marshal.FreeHGlobal(ptr);
            Console.WriteLine($"[BotPlacer] SnapViewAngles applied: {angles}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BotPlacer] SnapViewAngles failed: {ex.Message}");
        }
    }
}