using System;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Commands;

namespace BotMimicAdv;

public class RecordedCommand
{
    public string Command { get; set; } = "";
    public float TimeOffset { get; set; }
}

public class BotMimicAdv : BasePlugin
{
    public override string ModuleName => "Bot Mimic Advanced";
    public override string ModuleVersion => "7.3.0";

    private List<RecordedCommand> _recordedCommands = new();
    private CCSPlayerController? _recordingPlayer;
    private bool _isRecording;
    private float _recordStartTime;

    private CCSPlayerController? _botPlayer;
    private bool _isPlaying;
    private int _playbackIndex;
    private float _playbackStartTime;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _playbackTimer; // явное указание

    public override void Load(bool hotReload)
    {
        AddCommand("css_record", "Start recording player actions", OnRecordCommand);
        AddCommand("css_stoprecord", "Stop recording", OnStopRecordCommand);
        AddCommand("css_playback", "Playback recorded actions on a bot", OnPlaybackCommand);
        AddCommand("css_stopplayback", "Stop playback", OnStopPlaybackCommand);

        // Перехватываем все команды
        AddCommandListener(OnPlayerCommand, "", HookMode.Pre);
    }

    private HookResult OnPlayerCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || player != _recordingPlayer || !_isRecording)
            return HookResult.Continue;

        // Получаем строку команды (совместимость с разными версиями API)
        string cmd = command.CommandString;
        if (string.IsNullOrEmpty(cmd)) cmd = command.GetCommandString();

        if (cmd.StartsWith('+') || cmd.StartsWith('-'))
        {
            float now = (float)(Server.CurrentTime - _recordStartTime);
            _recordedCommands.Add(new RecordedCommand { Command = cmd, TimeOffset = now });
        }
        return HookResult.Continue;
    }

    [CommandHelper(minArgs: 1, usage: "<player name>")]
    private void OnRecordCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller == null || !caller.IsValid)
        {
            command.ReplyToCommand("Только игроки могут использовать эту команду.");
            return;
        }

        if (_isRecording)
        {
            command.ReplyToCommand("Запись уже идёт. Используйте !stoprecord.");
            return;
        }

        if (_isPlaying)
        {
            command.ReplyToCommand("Сейчас идёт воспроизведение. Остановите !stopplayback.");
            return;
        }

        string targetName = command.GetArg(1);
        var target = Utilities.GetPlayers()
            .FirstOrDefault(p => p.PlayerName.Equals(targetName, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            command.ReplyToCommand($"Игрок '{targetName}' не найден.");
            return;
        }

        _recordingPlayer = target;
        _recordedCommands.Clear();
        _isRecording = true;
        _recordStartTime = (float)Server.CurrentTime;

        command.ReplyToCommand($"Запись действий {_recordingPlayer.PlayerName} начата.");
    }

    private void OnStopRecordCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!_isRecording)
        {
            command.ReplyToCommand("Нет активной записи.");
            return;
        }

        _isRecording = false;
        command.ReplyToCommand($"Запись остановлена. Команд записано: {_recordedCommands.Count}");
    }

    private void OnPlaybackCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (_isPlaying)
        {
            command.ReplyToCommand("Воспроизведение уже идёт.");
            return;
        }

        if (_recordedCommands.Count == 0)
        {
            command.ReplyToCommand("Нет записанных данных. Сначала !record.");
            return;
        }

        if (_isRecording)
        {
            command.ReplyToCommand("Остановите запись (!stoprecord) перед воспроизведением.");
            return;
        }

        _botPlayer = FindOrCreateBot();
        if (_botPlayer == null || !_botPlayer.IsValid)
        {
            command.ReplyToCommand("Не удалось создать бота.");
            return;
        }

        // Сбрасываем все кнопки
        ResetBotKeys();

        _playbackIndex = 0;
        _isPlaying = true;
        _playbackStartTime = (float)Server.CurrentTime;
        _playbackTimer = AddTimer(0.016f, PlaybackTick, TimerFlags.REPEAT);

        command.ReplyToCommand($"Воспроизведение {_recordedCommands.Count} команд на боте {_botPlayer.PlayerName}...");
    }

    private void PlaybackTick()
    {
        if (!_isPlaying || _botPlayer == null || !_botPlayer.IsValid)
        {
            StopPlayback();
            return;
        }

        float now = (float)(Server.CurrentTime - _playbackStartTime);
        while (_playbackIndex < _recordedCommands.Count && _recordedCommands[_playbackIndex].TimeOffset <= now)
        {
            var cmd = _recordedCommands[_playbackIndex];
            _botPlayer.ExecuteClientCommand(cmd.Command);
            _playbackIndex++;
        }

        if (_playbackIndex >= _recordedCommands.Count)
            StopPlayback();
    }

    private void OnStopPlaybackCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!_isPlaying)
        {
            command.ReplyToCommand("Нет активного воспроизведения.");
            return;
        }

        StopPlayback();
        command.ReplyToCommand("Воспроизведение остановлено.");
    }

    private void StopPlayback()
    {
        _playbackTimer?.Kill();
        _playbackTimer = null;
        _isPlaying = false;
        if (_botPlayer != null && _botPlayer.IsValid)
            ResetBotKeys();
    }

    private void ResetBotKeys()
    {
        if (_botPlayer == null) return;
        foreach (var key in new[] { "forward", "back", "moveleft", "moveright", "jump", "duck", "attack", "attack2", "reload", "use", "speed" })
            _botPlayer.ExecuteClientCommand($"-{key}");
    }

    private CCSPlayerController? FindOrCreateBot()
    {
        var existing = Utilities.GetPlayers().FirstOrDefault(p => p.IsBot && p.IsValid);
        if (existing != null) return existing;
        Server.ExecuteCommand("bot_add");
        Server.NextFrame(() => { });
        return Utilities.GetPlayers().FirstOrDefault(p => p.IsBot && p.IsValid);
    }
}