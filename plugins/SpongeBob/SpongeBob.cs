using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace SpongeBobWar;

public class PluginConfig : BasePluginConfig
{
    [JsonPropertyName("T_model")] public string TModel { get; set; } = "characters/models/nozb1/patrik_player_model/patrik_player_model.vmdl";
    [JsonPropertyName("CT_model")] public string CTModel { get; set; } = "characters/models/nozb1/spongebob_player_model/spongebob_player_model.vmdl";
    [JsonPropertyName("ConfigVersion")] public override int Version { get; set; } = 1;
}

public class SpongeBobWar : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "SpongeBob War";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "YourName";

    public PluginConfig Config { get; set; } = new PluginConfig();

    public void OnConfigParsed(PluginConfig config)
    {
        if (config.Version < 1)
        {
            Logger.LogWarning("Конфиг устарел, обновляем...");
            config.TModel = Config.TModel;
            config.CTModel = Config.CTModel;
            config.Version = 1;
        }
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventPlayerSpawn>((@event, info) =>
        {
            CCSPlayerController? player = @event.Userid;
            if (player != null && player.IsValid && !player.IsBot)
            {
                AddTimer(1.0f, () => SetPlayerModelByTeam(player));
            }
            return HookResult.Continue;
        });
    }

    private void SetPlayerModelByTeam(CCSPlayerController? player)
    {
        if (player?.PlayerPawn?.Value == null) return;

        string modelPath = "";
        if (player.Team == CsTeam.Terrorist)
            modelPath = Config.TModel;
        else if (player.Team == CsTeam.CounterTerrorist)
            modelPath = Config.CTModel;
        else
            return;

        if (string.IsNullOrEmpty(modelPath) || modelPath == "none")
            return;

        CCSPlayerPawn pawn = player.PlayerPawn.Value;
        if (pawn != null && pawn.IsValid)
        {
            pawn.SetModel(modelPath);
            player.PrintToChat($"Модель для игрока {player.PlayerName} установлена на {modelPath}");
            Console.WriteLine($"Модель для игрока {player.PlayerName} установлена на {modelPath}");
        }
    }
}