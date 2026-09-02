namespace DoubleDashScore.Services;

/// <summary>
/// <see cref="IOcrMappingStore"/> mot MAUI:s <see cref="Preferences"/>. Enda
/// stället som känner till nyckeln.
/// </summary>
public sealed class PreferencesOcrMappingStore : IOcrMappingStore
{
    internal const string PreferenceKey = "ocr_player_mapping";

    public IReadOnlyList<int>? Get() =>
        OcrMappingCodec.TryDeserialize(Preferences.Default.Get<string?>(PreferenceKey, null));

    public void Set(IReadOnlyList<int> playerIds) =>
        Preferences.Default.Set(PreferenceKey, OcrMappingCodec.Serialize(playerIds));
}
