namespace DoubleDashScore.Data;

public sealed record RoundResultInput(
    int PlayerId,
    int FirstPlaces,
    int SecondPlaces,
    int ThirdPlaces,
    int FourthPlaces);
