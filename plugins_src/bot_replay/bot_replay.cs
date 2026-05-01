using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace ReplayPlugin;

public class ReplayPlugin : BasePlugin
{
    public override string ModuleName => "Action Replay Bot (TickSync)";
    public override string ModuleVersion => "4.1.0";

    private class ReplayFrame
    {
        public string PositionString { get; set; } = "";
        public string RotationString { get; set; } = "";
        public string SpeedString { get; set; } = "";
        public PlayerButtons Buttons { get; set; }
        public PlayerFlags Flags { get; set; }
        public MoveType_t MoveType { get; set; }
        public string MovementServiceData { get; set; } = "";
    }

    private class PlayerReplayData
    {
        public List<ReplayFrame> Frames = new();
        public bool IsRecording = false;
        public CCSPlayerController? ReplayBot = null;
        public int StartTick = 0;
        public bool WaitForMove = false;
        public Vector LastRecordedPos = new(0, 0, 0);
        public string? PendingFileName = null;
    }

    // Новый класс для метаданных реплея
    private class ReplayMetadata
    {
        public string? Nickname { get; set; } = null;
        public string? AvatarPath { get; set; } = null;
    }

    private readonly Dictionary<int, PlayerReplayData> playerReplays = new();
    private readonly List<string> replayPlaylist = new();
    private readonly List<PlayerReplayData> activePlaybacks = new();

    private string replayDirectory => Path.Combine(ModuleDirectory, "ReplayData");

    public override void Load(bool hotReload)
    {
        AddCommand("css_record", "Начать запись действий [-file_name <name>] [-on_move]", OnRecord);
        AddCommand("css_stoprec", "Остановить запись", OnStopRecord);
        AddCommand("css_play", "Воспроизвести реплей [-file_name <name>] или плейлист", OnPlay);
        AddCommand("css_stopplay", "Остановить воспроизведение", OnStopPlay);
        AddCommand("css_replaylist_add", "Добавить реплей в плейлист", OnReplaylistAdd);
        AddCommand("css_replaylist_remove", "Удалить реплей из плейлиста", OnReplaylistRemove);
        AddCommand("css_replay_edit", "Изменить ник/аватар реплея [-file_name <name>] [-add_nickname <nick>] [-add_avatar <path>]", OnReplayEdit);

        Directory.CreateDirectory(replayDirectory);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
    }

    // ====== ГЛАВНЫЙ ТИК (запись + воспроизведение) ======
    private void OnTick()
    {
        int currentTick = Server.TickCount;

        // --- Запись ---
        foreach (var (slot, data) in playerReplays)
        {
            if (!data.IsRecording) continue;

            var player = Utilities.GetPlayerFromSlot(slot);
            if (player == null || !player.IsValid) continue;

            var pawn = player.PlayerPawn?.Value;
            if (pawn == null) continue;

            if (data.WaitForMove)
            {
                Vector currentPos = pawn.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(0, 0, 0);
                float dx = currentPos.X - data.LastRecordedPos.X;
                float dy = currentPos.Y - data.LastRecordedPos.Y;
                float dz = currentPos.Z - data.LastRecordedPos.Z;
                if (dx * dx + dy * dy + dz * dz == 0)
                    continue;
                data.WaitForMove = false;
            }

            Vector pos = pawn.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(0, 0, 0);
            Vector speed = pawn.AbsVelocity ?? new Vector(0, 0, 0);
            QAngle angles = pawn.EyeAngles ?? new QAngle(0, 0, 0);

            string movementData = "";
            if (pawn.MovementServices != null)
            {
                var handle = pawn.MovementServices.Handle;
                try
                {
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bDucked").ToString() + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flDuckAmount").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flDuckSpeed").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bDuckOverride").ToString() + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bDesiresDuck").ToString() + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bDucking").ToString() + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flDuckRootOffset").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flDuckViewOffset").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flLastDuckTime").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flBombPlantViewOffset").ToString("F4") + ";";

                    var crouch = Schema.GetSchemaValue<Vector2D>(handle, "CCSPlayer_MovementServices", "m_vecLastPositionAtFullCrouchSpeed");
                    movementData += $"{crouch.X:F4};{crouch.Y:F4};";

                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_duckUntilOnGround").ToString() + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bHasWalkMovedSinceLastJump").ToString() + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bInStuckTest").ToString() + ";";
                    movementData += Schema.GetSchemaValue<int>(handle, "CCSPlayer_MovementServices", "m_nTraceCount").ToString() + ";";
                    movementData += Schema.GetSchemaValue<int>(handle, "CCSPlayer_MovementServices", "m_StuckLast").ToString() + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bSpeedCropped").ToString() + ";";
                    movementData += Schema.GetSchemaValue<int>(handle, "CCSPlayer_MovementServices", "m_nOldWaterLevel").ToString() + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flWaterEntryTime").ToString("F4") + ";";

                    var fwd = Schema.GetSchemaValue<Vector>(handle, "CCSPlayer_MovementServices", "m_vecForward");
                    movementData += $"{fwd.X:F4};{fwd.Y:F4};{fwd.Z:F4};";
                    var left = Schema.GetSchemaValue<Vector>(handle, "CCSPlayer_MovementServices", "m_vecLeft");
                    movementData += $"{left.X:F4};{left.Y:F4};{left.Z:F4};";
                    var up = Schema.GetSchemaValue<Vector>(handle, "CCSPlayer_MovementServices", "m_vecUp");
                    movementData += $"{up.X:F4};{up.Y:F4};{up.Z:F4};";

                    movementData += Schema.GetSchemaValue<int>(handle, "CCSPlayer_MovementServices", "m_nGameCodeHasMovedPlayerAfterCommand").ToString() + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_fStashGrenadeParameterWhen").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<ulong>(handle, "CCSPlayer_MovementServices", "m_nButtonDownMaskPrev").ToString() + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bUseFrictionStashedSpeed").ToString() + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flUseFrictionStashedSpeedUntilFrac").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flFrictionStashedSpeed").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flStamina").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flHeightAtJumpStart").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flMaxJumpHeightThisJump").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flMaxJumpHeightLastJump").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flStaminaAtJumpStart").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flVelMulAtJumpStart").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flAccumulatedJumpError").ToString("F4") + ";";

                    movementData += Schema.GetSchemaValue<int>(handle, "CCSPlayer_MovementServices", "m_nLastJumpTick").ToString() + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flLastJumpFrac").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flLastJumpVelocityZ").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bJumpApexPending").ToString() + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_flTicksSinceLastSurfingDetected").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bWasSurfing").ToString() + ";";

                    var walk = Schema.GetSchemaValue<Vector2D>(handle, "CCSPlayer_MovementServices", "m_vecWalkWishVel");
                    movementData += $"{walk.X:F4};{walk.Y:F4};";

                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_gtLastTimeOnStaticWorldGround").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<float>(handle, "CCSPlayer_MovementServices", "m_gtLastTimeInAir").ToString("F4") + ";";
                    movementData += Schema.GetSchemaValue<bool>(handle, "CCSPlayer_MovementServices", "m_bHasEverProcessedCommand").ToString();

                    nint legacyJump = Schema.GetSchemaValue<nint>(handle, "CCSPlayer_MovementServices", "m_LegacyJump");
                    movementData += ";" + Schema.GetSchemaValue<bool>(legacyJump, "CCSPlayerLegacyJump", "m_bOldJumpPressed").ToString();
                    movementData += ";" + Schema.GetSchemaValue<float>(legacyJump, "CCSPlayerLegacyJump", "m_flJumpPressedTime").ToString("F4");

                    nint modernJump = Schema.GetSchemaValue<nint>(handle, "CCSPlayer_MovementServices", "m_ModernJump");
                    movementData += ";" + Schema.GetSchemaValue<int>(modernJump, "CCSPlayerModernJump", "m_nLastActualJumpPressTick.m_Value").ToString();
                    movementData += ";" + Schema.GetSchemaValue<float>(modernJump, "CCSPlayerModernJump", "m_flLastActualJumpPressFrac").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<int>(modernJump, "CCSPlayerModernJump", "m_nLastUsableJumpPressTick.m_Value").ToString();
                    movementData += ";" + Schema.GetSchemaValue<float>(modernJump, "CCSPlayerModernJump", "m_flLastUsableJumpPressFrac").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<int>(modernJump, "CCSPlayerModernJump", "m_nLastLandedTick.m_Value").ToString();
                    movementData += ";" + Schema.GetSchemaValue<float>(modernJump, "CCSPlayerModernJump", "m_flLastLandedFrac").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<float>(modernJump, "CCSPlayerModernJump", "m_flLastLandedVelocityX").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<float>(modernJump, "CCSPlayerModernJump", "m_flLastLandedVelocityY").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<float>(modernJump, "CCSPlayerModernJump", "m_flLastLandedVelocityZ").ToString("F4");

                    nint animState = Schema.GetSchemaValue<nint>(handle, "CCSPlayer_MovementServices", "m_AnimationState");
                    movementData += ";" + Schema.GetSchemaValue<int>(animState, "CCSPlayerAnimationState", "m_currentMoveType").ToString();
                    movementData += ";" + Schema.GetSchemaValue<int>(animState, "CCSPlayerAnimationState", "m_groundMoveState").ToString();
                    movementData += ";" + Schema.GetSchemaValue<int>(animState, "CCSPlayerAnimationState", "m_groundActionDirection").ToString();
                    movementData += ";" + Schema.GetSchemaValue<int>(animState, "CCSPlayerAnimationState", "m_airAction").ToString();
                    movementData += ";" + Schema.GetSchemaValue<bool>(animState, "CCSPlayerAnimationState", "m_bWasOnGroundLastUpdate").ToString();
                    movementData += ";" + Schema.GetSchemaValue<bool>(animState, "CCSPlayerAnimationState", "m_bWasStationaryLastUpdate").ToString();
                    movementData += ";" + Schema.GetSchemaValue<int>(animState, "CCSPlayerAnimationState", "m_actionStartTick").ToString();
                    movementData += ";" + Schema.GetSchemaValue<int>(animState, "CCSPlayerAnimationState", "m_staticAimTimerStartTick").ToString();
                    movementData += ";" + Schema.GetSchemaValue<int>(animState, "CCSPlayerAnimationState", "m_stutterStepStartTick").ToString();
                    movementData += ";" + Schema.GetSchemaValue<int>(animState, "CCSPlayerAnimationState", "m_plantAndTurnStartTick").ToString();
                    movementData += ";" + Schema.GetSchemaValue<bool>(animState, "CCSPlayerAnimationState", "m_bIsStutterStep").ToString();
                    movementData += ";" + Schema.GetSchemaValue<float>(animState, "CCSPlayerAnimationState", "m_flTurnOnSpotAngle").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<float>(animState, "CCSPlayerAnimationState", "m_flPreviousAimYaw").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<float>(animState, "CCSPlayerAnimationState", "m_flPreviousHorizontalSpeed").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<float>(animState, "CCSPlayerAnimationState", "m_flFootIKOffsetLeft").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<float>(animState, "CCSPlayerAnimationState", "m_flFootIKOffsetRight").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<float>(animState, "CCSPlayerAnimationState", "m_flWeaponDropPercentageDueToMovement").ToString("F4");
                    movementData += ";" + Schema.GetSchemaValue<float>(animState, "CCSPlayerAnimationState", "m_flWeaponDropSmoothDampVelocity").ToString("F4");
                }
                catch { }
            }

            data.Frames.Add(new ReplayFrame
            {
                PositionString = $"{pos.X:F3} {pos.Y:F3} {pos.Z:F3}",
                RotationString = $"{angles.X:F3} {angles.Y:F3} {angles.Z:F3}",
                SpeedString = $"{speed.X:F3} {speed.Y:F3} {speed.Z:F3}",
                Buttons = player.Buttons,
                Flags = (PlayerFlags)pawn.Flags,
                MoveType = pawn.MoveType,
                MovementServiceData = movementData
            });
        }

        // --- Воспроизведение ---
        for (int i = activePlaybacks.Count - 1; i >= 0; i--)
        {
            var data = activePlaybacks[i];
            var bot = data.ReplayBot;
            if (bot == null || !bot.IsValid)
            {
                activePlaybacks.RemoveAt(i);
                continue;
            }

            int frameIndex = currentTick - data.StartTick;
            if (frameIndex >= data.Frames.Count)
            {
                if (bot.IsValid)
                    Server.ExecuteCommand($"kickid {bot.UserId}");
                activePlaybacks.RemoveAt(i);
                continue;
            }

            if (frameIndex < 0) continue;

            var frame = data.Frames[frameIndex];
            var pawn = bot.PlayerPawn.Value;
            if (pawn == null) continue;

            bot.Pawn.Value!.MoveType = frame.MoveType;
            bot.Pawn.Value!.ActualMoveType = frame.MoveType;

            // Восстановление MovementService
            if (!string.IsNullOrEmpty(frame.MovementServiceData) && pawn.MovementServices != null)
            {
                var handle = pawn.MovementServices.Handle;
                var parts = frame.MovementServiceData.Split(';');
                if (parts.Length >= 64)
                {
                    try
                    {
                        int idx = 0;
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bDucked", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flDuckAmount", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flDuckSpeed", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bDuckOverride", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bDesiresDuck", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bDucking", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flDuckRootOffset", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flDuckViewOffset", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flLastDuckTime", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flBombPlantViewOffset", float.Parse(parts[idx++]));

                        float crX = float.Parse(parts[idx++]), crY = float.Parse(parts[idx++]);
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_vecLastPositionAtFullCrouchSpeed", new Vector2D(crX, crY));

                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_duckUntilOnGround", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bHasWalkMovedSinceLastJump", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bInStuckTest", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_nTraceCount", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_StuckLast", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bSpeedCropped", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_nOldWaterLevel", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flWaterEntryTime", float.Parse(parts[idx++]));

                        Vector fwd = new(float.Parse(parts[idx++]), float.Parse(parts[idx++]), float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_vecForward", fwd);
                        Vector left = new(float.Parse(parts[idx++]), float.Parse(parts[idx++]), float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_vecLeft", left);
                        Vector up = new(float.Parse(parts[idx++]), float.Parse(parts[idx++]), float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_vecUp", up);

                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_nGameCodeHasMovedPlayerAfterCommand", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_fStashGrenadeParameterWhen", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_nButtonDownMaskPrev", ulong.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bUseFrictionStashedSpeed", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flUseFrictionStashedSpeedUntilFrac", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flFrictionStashedSpeed", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flStamina", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flHeightAtJumpStart", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flMaxJumpHeightThisJump", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flMaxJumpHeightLastJump", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flStaminaAtJumpStart", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flVelMulAtJumpStart", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flAccumulatedJumpError", float.Parse(parts[idx++]));

                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_nLastJumpTick", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flLastJumpFrac", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flLastJumpVelocityZ", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bJumpApexPending", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_flTicksSinceLastSurfingDetected", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bWasSurfing", bool.Parse(parts[idx++]));

                        float wx = float.Parse(parts[idx++]), wy = float.Parse(parts[idx++]);
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_vecWalkWishVel", new Vector2D(wx, wy));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_gtLastTimeOnStaticWorldGround", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_gtLastTimeInAir", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(handle, "CCSPlayer_MovementServices", "m_bHasEverProcessedCommand", bool.Parse(parts[idx++]));

                        nint legacyHandle = Schema.GetSchemaValue<nint>(handle, "CCSPlayer_MovementServices", "m_LegacyJump");
                        Schema.SetSchemaValue(legacyHandle, "CCSPlayerLegacyJump", "m_bOldJumpPressed", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(legacyHandle, "CCSPlayerLegacyJump", "m_flJumpPressedTime", float.Parse(parts[idx++]));

                        nint modernHandle = Schema.GetSchemaValue<nint>(handle, "CCSPlayer_MovementServices", "m_ModernJump");
                        Schema.SetSchemaValue(modernHandle, "CCSPlayerModernJump", "m_nLastActualJumpPressTick.m_Value", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(modernHandle, "CCSPlayerModernJump", "m_flLastActualJumpPressFrac", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(modernHandle, "CCSPlayerModernJump", "m_nLastUsableJumpPressTick.m_Value", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(modernHandle, "CCSPlayerModernJump", "m_flLastUsableJumpPressFrac", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(modernHandle, "CCSPlayerModernJump", "m_nLastLandedTick.m_Value", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(modernHandle, "CCSPlayerModernJump", "m_flLastLandedFrac", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(modernHandle, "CCSPlayerModernJump", "m_flLastLandedVelocityX", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(modernHandle, "CCSPlayerModernJump", "m_flLastLandedVelocityY", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(modernHandle, "CCSPlayerModernJump", "m_flLastLandedVelocityZ", float.Parse(parts[idx++]));

                        nint animHandle = Schema.GetSchemaValue<nint>(handle, "CCSPlayer_MovementServices", "m_AnimationState");
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_currentMoveType", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_groundMoveState", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_groundActionDirection", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_airAction", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_bWasOnGroundLastUpdate", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_bWasStationaryLastUpdate", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_actionStartTick", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_staticAimTimerStartTick", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_stutterStepStartTick", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_plantAndTurnStartTick", int.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_bIsStutterStep", bool.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_flTurnOnSpotAngle", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_flPreviousAimYaw", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_flPreviousHorizontalSpeed", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_flFootIKOffsetLeft", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_flFootIKOffsetRight", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_flWeaponDropPercentageDueToMovement", float.Parse(parts[idx++]));
                        Schema.SetSchemaValue(animHandle, "CCSPlayerAnimationState", "m_flWeaponDropSmoothDampVelocity", float.Parse(parts[idx++]));
                    }
                    catch { }
                }
            }

            Vector pos = ParseVector(frame.PositionString);
            QAngle ang = ParseQAngle(frame.RotationString);
            Vector vel = ParseVector(frame.SpeedString);
            pawn.Teleport(pos, new QAngle(0, ang.Y, 0), vel);
            SnapViewAngles(pawn, ang);
        }
    }

    // ====== КОМАНДЫ ======
    private void OnRecord(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        int slot = player.Slot;

        var args = info.ArgString?.Split(' ') ?? Array.Empty<string>();
        string? fileName = null;
        bool waitForMove = false;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-file_name" && i + 1 < args.Length)
                fileName = args[++i];
            else if (args[i] == "-on_move")
                waitForMove = true;
        }

        if (!playerReplays.ContainsKey(slot))
            playerReplays[slot] = new PlayerReplayData();
        else
            playerReplays[slot].Frames.Clear();

        var player2 = Utilities.GetPlayerFromSlot(slot);
        Vector currentPos = player2.PlayerPawn?.Value.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(0, 0, 0);
        playerReplays[slot].LastRecordedPos = new Vector(currentPos.X, currentPos.Y, currentPos.Z);

        playerReplays[slot].IsRecording = true;
        playerReplays[slot].WaitForMove = waitForMove;
        playerReplays[slot].PendingFileName = fileName;

        if (waitForMove)
            player.PrintToChat("[Replay] Запись начнётся, как только вы начнёте движение.");
        else
            player.PrintToChat("[Replay] Запись начата.");
    }

    private void OnStopRecord(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        int slot = player.Slot;

        if (!playerReplays.TryGetValue(slot, out var data) || !data.IsRecording)
        {
            player.PrintToChat("[Replay] Вы не записываете.");
            return;
        }

        data.IsRecording = false;

        if (!string.IsNullOrEmpty(data.PendingFileName))
        {
            try
            {
                string path = Path.Combine(replayDirectory, $"{data.PendingFileName}.json");
                var framesToSave = data.Frames.Select(f => new ReplayFrame
                {
                    PositionString = f.PositionString,
                    RotationString = f.RotationString,
                    SpeedString = f.SpeedString,
                    Buttons = f.Buttons,
                    Flags = f.Flags,
                    MoveType = f.MoveType,
                    MovementServiceData = f.MovementServiceData
                }).ToList();
                string json = JsonSerializer.Serialize(framesToSave);
                File.WriteAllText(path, json);
                player.PrintToChat($"[Replay] Запись сохранена в {data.PendingFileName}.json");
            }
            catch (Exception ex)
            {
                player.PrintToChat($"[Replay] Ошибка сохранения: {ex.Message}");
            }
        }
        else
        {
            player.PrintToChat($"[Replay] Запись остановлена. Кадров: {data.Frames.Count}");
        }
    }

    private void OnReplaylistAdd(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || string.IsNullOrEmpty(info.ArgString)) return;
        string name = info.ArgString.Trim();
        if (!replayPlaylist.Contains(name))
        {
            replayPlaylist.Add(name);
            player.PrintToChat($"[Replay] '{name}' добавлен в плейлист.");
        }
    }

    private void OnReplaylistRemove(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || string.IsNullOrEmpty(info.ArgString)) return;
        string name = info.ArgString.Trim();
        if (replayPlaylist.Remove(name))
            player.PrintToChat($"[Replay] '{name}' удалён из плейлиста.");
    }

    private void OnPlay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        var args = info.ArgString?.Split(' ') ?? Array.Empty<string>();
        string? fileToPlay = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-file_name" && i + 1 < args.Length)
                fileToPlay = args[++i];
        }

        if (replayPlaylist.Count > 0)
        {
            var playlistCopy = new List<string>(replayPlaylist);
            replayPlaylist.Clear();
            PlayPlaylist(playlistCopy, player);
            return;
        }

        if (!string.IsNullOrEmpty(fileToPlay))
        {
            string path = Path.Combine(replayDirectory, $"{fileToPlay}.json");
            if (!File.Exists(path))
            {
                player.PrintToChat($"[Replay] Файл {fileToPlay}.json не найден.");
                return;
            }
            try
            {
                string json = File.ReadAllText(path);
                var frames = JsonSerializer.Deserialize<List<ReplayFrame>>(json);
                if (frames != null && frames.Count > 0)
                {
                    // Загружаем метаданные, если есть
                    var meta = LoadReplayMetadata(fileToPlay);
                    StartBotPlayback(frames, player, meta);
                }
            }
            catch (Exception ex)
            {
                player.PrintToChat($"[Replay] Ошибка загрузки: {ex.Message}");
            }
            return;
        }

        int slot = player.Slot;
        if (playerReplays.TryGetValue(slot, out var data) && data.Frames.Count > 0)
        {
            if (activePlaybacks.Any(p => p.ReplayBot != null && p.ReplayBot.IsValid))
            {
                player.PrintToChat("[Replay] Воспроизведение уже идёт.");
                return;
            }
            player.PrintToChat("[Replay] Запуск воспроизведения...");
            data.StartTick = Server.TickCount;
            activePlaybacks.Add(data);
        }
        else
        {
            player.PrintToChat("[Replay] Нет записанных кадров. Сначала !record и !stoprec.");
        }
    }

    private void PlayPlaylist(List<string> playlist, CCSPlayerController owner)
    {
        var loadedFrames = new List<List<ReplayFrame>>();
        var loadedMetas = new List<ReplayMetadata?>();  // метаданные для каждого реплея
        foreach (var name in playlist)
        {
            string path = Path.Combine(replayDirectory, $"{name}.json");
            if (!File.Exists(path))
            {
                owner.PrintToChat($"[Replay] Файл {name}.json не найден.");
                continue;
            }
            try
            {
                string json = File.ReadAllText(path);
                var frames = JsonSerializer.Deserialize<List<ReplayFrame>>(json);
                if (frames != null && frames.Count > 0)
                {
                    loadedFrames.Add(frames);
                    loadedMetas.Add(LoadReplayMetadata(name));
                }
            }
            catch (Exception ex)
            {
                owner.PrintToChat($"[Replay] Ошибка загрузки {name}: {ex.Message}");
            }
        }

        if (loadedFrames.Count == 0)
        {
            owner.PrintToChat("[Replay] Нет валидных записей в плейлисте.");
            return;
        }

        Server.ExecuteCommand("sv_cheats 1");
        Server.ExecuteCommand("bot_quota 0");
        Server.ExecuteCommand("bot_quota_mode fill");
        Server.ExecuteCommand("bot_stop 1");
        Server.ExecuteCommand("bot_freeze 1");
        Server.ExecuteCommand("bot_zombie 1");

        for (int i = 0; i < loadedFrames.Count; i++)
            Server.ExecuteCommand("bot_add_ct");

        AddTimer(0.5f, () =>
        {
            var allBots = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller")
                .Where(b => b != null && b.IsValid && b.IsBot && !b.IsHLTV)
                .ToList();

            if (allBots.Count < loadedFrames.Count)
            {
                owner.PrintToChat($"[Replay] Не удалось создать достаточно ботов ({allBots.Count}/{loadedFrames.Count}).");
                return;
            }

            int startTick = Server.TickCount;
            for (int i = 0; i < loadedFrames.Count; i++)
            {
                var bot = allBots[i];
                SetupReplayBot(bot, loadedMetas[i]); // передаём метаданные
                var pData = new PlayerReplayData
                {
                    Frames = loadedFrames[i],
                    ReplayBot = bot,
                    StartTick = startTick,
                };
                activePlaybacks.Add(pData);
            }
            owner.PrintToChat($"[Replay] Плейлист запущен ({loadedFrames.Count} ботов).");
        });
    }

    private void StartBotPlayback(List<ReplayFrame> frames, CCSPlayerController owner, ReplayMetadata? meta = null)
    {
        Server.ExecuteCommand("sv_cheats 1");
        Server.ExecuteCommand("bot_add_ct");
        Server.ExecuteCommand("bot_quota 0");
        Server.ExecuteCommand("bot_quota_mode fill");
        Server.ExecuteCommand("bot_stop 1");
        Server.ExecuteCommand("bot_freeze 1");
        Server.ExecuteCommand("bot_zombie 1");

        AddTimer(0.1f, () =>
        {
            var bots = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
            foreach (var bot in bots)
            {
                if (bot == null || !bot.IsValid || !bot.IsBot || bot.IsHLTV)
                    continue;

                SetupReplayBot(bot, meta);
                var pData = new PlayerReplayData
                {
                    Frames = frames,
                    ReplayBot = bot,
                    StartTick = Server.TickCount,
                };
                activePlaybacks.Add(pData);
                owner.PrintToChat($"[Replay] Бот запущен.");
                return;
            }
        });
    }

    private void OnStopPlay(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;

        foreach (var data in activePlaybacks)
        {
            if (data.ReplayBot != null && data.ReplayBot.IsValid)
                Server.ExecuteCommand($"kickid {data.ReplayBot.UserId}");
        }
        activePlaybacks.Clear();
        player.PrintToChat("[Replay] Воспроизведение остановлено.");
    }

    // ====== РЕДАКТИРОВАНИЕ МЕТАДАННЫХ ======
    private void OnReplayEdit(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null) return;
        var args = info.ArgString?.Split(' ') ?? Array.Empty<string>();
        if (args.Length < 2)
        {
            player.PrintToChat("[Replay] Использование: css_replay_edit -file_name <name> [-add_nickname <nick>] [-add_avatar <path>]");
            return;
        }

        string? fileName = null;
        string? nickname = null;
        string? avatarPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-file_name" && i + 1 < args.Length)
                fileName = args[++i];
            else if (args[i] == "-add_nickname" && i + 1 < args.Length)
                nickname = args[++i];
            else if (args[i] == "-add_avatar" && i + 1 < args.Length)
                avatarPath = args[++i];
        }

        if (string.IsNullOrEmpty(fileName))
        {
            player.PrintToChat("[Replay] Не указано имя файла (-file_name).");
            return;
        }

        // Проверяем, существует ли файл реплея
        string replayPath = Path.Combine(replayDirectory, $"{fileName}.json");
        if (!File.Exists(replayPath))
        {
            player.PrintToChat($"[Replay] Файл {fileName}.json не найден.");
            return;
        }

        // Загружаем текущие метаданные (если есть)
        string metaPath = Path.Combine(replayDirectory, $"{fileName}_meta.json");
        ReplayMetadata meta = new ReplayMetadata();
        if (File.Exists(metaPath))
        {
            try
            {
                string existingJson = File.ReadAllText(metaPath);
                meta = JsonSerializer.Deserialize<ReplayMetadata>(existingJson) ?? new ReplayMetadata();
            }
            catch { }
        }

        // Обновляем поля, если заданы
        if (nickname != null)
            meta.Nickname = nickname == "null" ? null : nickname;  // специальное значение для сброса
        if (avatarPath != null)
            meta.AvatarPath = avatarPath == "null" ? null : avatarPath;

        // Сохраняем
        try
        {
            string json = JsonSerializer.Serialize(meta);
            File.WriteAllText(metaPath, json);
            player.PrintToChat($"[Replay] Метаданные для {fileName}.json обновлены.");
        }
        catch (Exception ex)
        {
            player.PrintToChat($"[Replay] Ошибка сохранения метаданных: {ex.Message}");
        }
    }

    // ====== ОБЩИЕ МЕТОДЫ ======
    private ReplayMetadata? LoadReplayMetadata(string fileName)
    {
        string metaPath = Path.Combine(replayDirectory, $"{fileName}_meta.json");
        if (!File.Exists(metaPath))
            return null;

        try
        {
            string json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<ReplayMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    private void SetupReplayBot(CCSPlayerController bot, ReplayMetadata? meta = null)
    {
        bot.PlayerPawn.Value!.Bot!.IsSleeping = true;
        bot.PlayerPawn.Value!.Bot!.AllowActive = true;
        bot.RemoveWeapons();

        // Установка ника, если есть в метаданных
        if (meta != null && !string.IsNullOrEmpty(meta.Nickname))
        {
            //bot.PlayerName = meta.Nickname;
            // или вызов ChangePlayerName, если нужно отправить обновление
            // ChangePlayerName(bot, meta.Nickname);
        }

        // Аватар пока не применяем (в CS2 сервер не управляет аватарами напрямую),
        // но можем сохранить в метаданных для информации.

        bot.Pawn.Value!.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NONE;
        bot.Pawn.Value!.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_NONE;
        Utilities.SetStateChanged(bot, "CCollisionProperty", "m_CollisionGroup", 0);
        Utilities.SetStateChanged(bot, "CCollisionProperty", "m_collisionAttribute", 0);
    }

    private void SnapViewAngles(CCSPlayerPawn pawn, QAngle angles)
    {
        var sig = GameData.GetSignature("SnapViewAngles");
        if (sig == null)
        {
            Console.WriteLine("[ReplayPlugin] SnapViewAngles signature not found in gamedata.json.");
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ReplayPlugin] SnapViewAngles failed: {ex.Message}");
        }
    }

    private static Vector ParseVector(string str)
    {
        var parts = str.Split(' ');
        if (parts.Length < 3) return new Vector(0, 0, 0);
        float.TryParse(parts[0], out float x);
        float.TryParse(parts[1], out float y);
        float.TryParse(parts[2], out float z);
        return new Vector(x, y, z);
    }

    private static QAngle ParseQAngle(string str)
    {
        var parts = str.Split(' ');
        if (parts.Length < 3) return new QAngle(0, 0, 0);
        float.TryParse(parts[0], out float x);
        float.TryParse(parts[1], out float y);
        float.TryParse(parts[2], out float z);
        return new QAngle(x, y, z);
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null) return HookResult.Continue;
        int slot = player.Slot;

        if (playerReplays.TryGetValue(slot, out var data))
        {
            if (data.ReplayBot != null && data.ReplayBot.IsValid)
                Server.ExecuteCommand($"kickid {data.ReplayBot.UserId}");
            activePlaybacks.Remove(data);
            playerReplays.Remove(slot);
        }
        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        foreach (var data in activePlaybacks)
        {
            if (data.ReplayBot != null && data.ReplayBot.IsValid)
                Server.ExecuteCommand($"kickid {data.ReplayBot.UserId}");
        }
        activePlaybacks.Clear();
        base.Unload(hotReload);
    }
}