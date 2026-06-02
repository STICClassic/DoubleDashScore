namespace DoubleDashScore.Services;

// Broadcastas av EditNoteViewModel efter att en kvälls anteckning sparats.
// NightDetailViewModel (uppdaterar sin Note direkt) och NightsListViewModel
// (uppdaterar subtiteln i listan) prenumererar via WeakReferenceMessenger.
// Note är det sparade värdet (null = anteckning borttagen). Samma in-place-
// reload-mönster som DatabaseImportedMessage, men riktat mot en enskild kväll.
public sealed record GameNightNoteUpdatedMessage(int NightId, string? Note);
