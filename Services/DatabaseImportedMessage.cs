namespace DoubleDashScore.Services;

// Signaleras via WeakReferenceMessenger när DatabaseService.ReplaceDatabaseAsync
// har bytt aktiv .db-fil. Datadrivna ViewModels prenumererar och kör om sin
// LoadAsync så att vyer uppdateras utan att användaren måste navigera bort
// och tillbaka.
public sealed record DatabaseImportedMessage;
