namespace DoubleDashScore.Services;

/// <summary>
/// <see cref="IPlayerPositionMappingStore"/> mot MAUI:s <see cref="Preferences"/>. Enda
/// stället som känner till nyckeln. Nyckelnamnet är kvar från Skiva 29 (OCR)
/// för att inte tappa mappningen som redan ligger sparad på användarens telefon.
/// </summary>
public sealed class PreferencesPlayerPositionMappingStore : IPlayerPositionMappingStore
{
    internal const string PreferenceKey = "ocr_player_mapping";

    public IReadOnlyList<int>? Get() =>
        PlayerPositionMappingCodec.TryDeserialize(Preferences.Default.Get<string?>(PreferenceKey, null));

    public void Set(IReadOnlyList<int> playerIds) =>
        Preferences.Default.Set(PreferenceKey, PlayerPositionMappingCodec.Serialize(playerIds));
}
