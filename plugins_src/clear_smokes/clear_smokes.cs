using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace ClearSmoke;

public class ClearSmoke : BasePlugin
{
    public override string ModuleName => "Clear Smoke";
    public override string ModuleVersion => "1.0.0";

    // Название класса сущности для активной дымовой гранаты
    private const string SMOKE_ENTITY_NAME = "smokegrenade_projectile";

    public override void Load(bool hotReload)
    {
        // Регистрируем команду "css_clearsmoke"
        AddCommand("css_clearsmoke", "Убирает все дымовые гранаты с карты", Command_ClearSmoke);
    }

    private void Command_ClearSmoke(CCSPlayerController? client, CommandInfo info)
    {
        // Счётчик удалённых дымов
        int removedCount = 0;

        // Получаем список всех существующих на сервере сущностей
        var allEntities = Utilities.GetAllEntities();

        foreach (var entity in allEntities)
        {
            // Проверяем, является ли сущность снарядом дымовой гранаты
            if (entity == null || !entity.IsValid) continue;
            if (entity.DesignerName == SMOKE_ENTITY_NAME)
            {
                // Удаляем сущность дыма
                entity.Remove();
                removedCount++;
            }
        }

        // Отправляем сообщение игроку, который ввёл команду
        string playerName = client?.PlayerName ?? "Console";
        info.ReplyToCommand($"[ClearSmoke] Удалено дымов: {removedCount}.");

        // Также выводим сообщение в консоль сервера для наглядности
        Console.WriteLine($"[ClearSmoke] Игрок {playerName} очистил {removedCount} дым(ов).");
    }
}