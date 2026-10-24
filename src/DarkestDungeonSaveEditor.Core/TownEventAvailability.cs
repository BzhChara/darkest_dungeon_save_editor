namespace DarkestDungeonSaveEditor.Core;

public static class TownEventAvailability
{
    public static string UnavailableReason(TownEventDefinition definition)
    {
        if (definition.HasProviderConflict) return EditorText.Get("TownEvent_Unresolved");
        foreach (var effect in definition.Effects)
        {
            try { _ = TownEventInitializer.EffectType(effect); }
            catch (InvalidOperationException error) { return error.Message; }
        }
        return string.Empty;
    }
}
