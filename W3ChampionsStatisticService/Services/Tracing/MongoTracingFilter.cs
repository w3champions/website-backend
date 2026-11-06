using MongoDB.Bson;
using W3ChampionsStatisticService.PersonalSettings.Hotkeys;

namespace W3ChampionsStatisticService.Services.Tracing;

/// <summary>
/// Decides which Mongo commands get a trace span. Command text capture would export whole documents,
/// so collections that hold user content (hotkey setups) are never traced.
/// </summary>
public static class MongoTracingFilter
{
    private const string HotkeySetupCollection = nameof(HotkeySetup);

    public static bool ShouldTrace(BsonDocument command)
    {
        if (command == null || command.ElementCount == 0)
        {
            return true;
        }

        // The target collection is the value of the command's first element ({ insert: "<collection>", ... }).
        if (IsHotkeySetup(command.GetElement(0).Value))
        {
            return false;
        }

        // getMore carries its collection in a separate field: { getMore: <cursorId>, collection: "<collection>" }.
        return !(command.TryGetValue("collection", out var collection) && IsHotkeySetup(collection));
    }

    private static bool IsHotkeySetup(BsonValue value) =>
        value.IsString && value.AsString == HotkeySetupCollection;
}
