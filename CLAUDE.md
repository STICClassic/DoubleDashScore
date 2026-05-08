# Mario Kart Double Dash — Statistikapp

Det här dokumentet är produktspecifikation och teknisk guide för appen.
Läs det innan du börjar arbeta i projektet. Uppdatera det när vi fattar nya beslut.

## Översikt

En privat .NET MAUI-app (Android först, iOS senare) för att hantera poängstatistik
för Mario Kart Double Dash. Fyra spelare spelar tillsammans varje vecka. Appen
ersätter en manuell Excel-process där användaren idag fotar poängtavlan, skriver
in poäng i Excel, och uppdaterar grafer och listor för hand.

Användare: en person (utvecklaren själv) på en Android-telefon. Möjligen delas
med kompisar senare. Inga kommersiella användningsfall.

## Domän och begrepp

Använd dessa termer konsekvent i kod, kommentarer, tabellnamn och UI-text.
Svenska termer i UI, engelska i kod.

- **Spelare** (`Player`) — en av fyra fasta personer som spelar varje vecka.
- **Bana** (`Track`) — en enskild Mario Kart-bana. En omgång består av 16 banor.
- **Omgång** (`Round`) — 16 banor i följd. En omgång räknas som **komplett**
  (`IsComplete = true`) endast om den har alla 16 banor. Ofullständiga
  omgångar är **partiella** (`IsComplete = false`).
- **Kväll** (`GameNight`) — ett speltillfälle. Består av en eller flera omgångar.
  Kan innehålla både kompletta och partiella omgångar.
- **Position** (`Position`) — placering 1–4 på en enskild bana eller en hel omgång.
- **Banpoäng** (`TrackPoints`) — poäng för en bana baserat på position:
  1:a = 4p, 2:a = 3p, 3:e = 2p, 4:a = 1p.
- **Omgångsplacering** (`RoundPosition`) — spelarens slutposition (1–4) i en
  komplett omgång, baserat på total banpoäng den omgången.
- **Kvällssnitt** (`NightAverage`) — total banpoäng för kvällen delat med totalt
  antal banor den kvällen. Räknas på **alla** banor, inklusive partiella omgångar.
- **Kvällsplacering** (`NightPlacements`) — listan av omgångsplaceringar för
  kvällen, t.ex. `[1, 1]` om spelaren vann båda kompletta omgångar. Partiella
  omgångar räknas inte med.
- **Totalscore-lista** (`PositionTotals`) — ackumulerad räknare per spelare över
  hur många gånger hen hamnat på 1:a, 2:a, 3:e, 4:e plats. Uppdateras med +1
  per **komplett** omgång. Partiella omgångar räknas inte.

## Poängsystem och formler

Banpoäng per position:
- 1:a plats: 4 poäng
- 2:a plats: 3 poäng
- 3:e plats: 2 poäng
- 4:a plats: 1 poäng

**Kvällssnitt-formel:** summera alla banpoäng spelaren fått under kvällen
(oavsett om omgången är komplett eller partiell), dela med totalt antal banor
spelaren spelat den kvällen.

Exempel: spelare vann 7 banor, blev 2:a på 5, 3:a på 3, 4:a på 1 bana. Totalt 16 banor.
`(7×4) + (5×3) + (3×2) + (1×1) = 28 + 15 + 6 + 1 = 50 poäng.`
`50 / 16 = 3,125 ≈ 3,13 i kvällssnitt.`

**Omgångsplacering** beräknas inom en komplett omgång genom att rangordna
spelarna efter total banpoäng inom de 16 banorna.

**Tiebreaker-regel (standard tied ranking):** spelare med samma poäng får
samma placering. Nästa placering hoppar över motsvarande antal positioner.

Exempel:
- Två spelare delar 1:a plats → båda får placering 1. Nästa spelare får
  placering 3 (inte 2). Sista spelaren får placering 4.
- Tre spelare delar 1:a plats → alla tre får placering 1. Sista spelaren
  får placering 4.
- Två spelare delar 2:a plats → 1:a-placering oförändrad, båda får
  placering 2, sista spelaren får placering 4.

Detta gäller både omgångsplacering och hur platser räknas in i
totalscore-listan. Om två spelare delar 1:a plats i en komplett omgång
får båda +1 på sin 1:a-räknare. Ingen får +1 på 2:a-räknaren den omgången.

## Funktioner (skivor)

Bygg i den här ordningen. Färdigställ en skiva innan nästa påbörjas.
Commit:a efter varje skiva.

### Skiva 1 — Datamodell + manuell inmatning
- SQLite-databas via `sqlite-net-pcl`.
- Tabeller: `Players`, `GameNights`, `Rounds`, `TrackResults`. Alla rader
  har `CreatedAt` (UTC) och `DeletedAt` (nullable).
- UI: skapa kväll, lägg till omgång, mata in fyra spelares position per bana.
- Soft delete: ingen rad raderas hårt. Allt filtreras på `DeletedAt IS NULL`.

### Skiva 2 — Statistik och grafer
- Vy: kvällssnitt per spelare (för aktuell kväll och historiskt).
- Vy: kvällsplacering (lista av omgångsplaceringar).
- Vy: totalscore-lista (ackumulerad).
- Graf över kvällssnitt över tid per spelare. Använd
  **OxyPlot.Maui.Skia** eller **Microcharts**.

### Skiva 3 — Excel/CSV-export + mailbackup
- Generera CSV (enkelt) eller XLSX (`ClosedXML`) med kvällens data.
- Skicka via mail (`MailKit` med SMTP, eller plattformens dela-intent som fallback).
- Trigger: knapp "Avsluta kväll" eller automatiskt när kväll markeras som klar.

### Skiva 4 — Importera startvärden från Excel
- Engångsimport: aggregerade startvärden, **inte** rådata per bana.
- Tre saker importeras:
  1. Totalscore-lista per spelare (antal 1:or, 2:or, 3:or, 4:or vid startdatum).
  2. Historiska kvällssnitt som datapunkter (datum + snitt per spelare) för grafen.
  3. Historiska kvällsplaceringar (datum + lista per spelare).
- Lagras med en `IsHistoricalSeed`-flagga så de aldrig ändras av appen.
- Statistik och grafer kombinerar seed-data + appens egen data.

### Skiva 5 — Kamera + ML Kit OCR
- Ta foto i appen eller importera från galleriet.
- OCR via Google ML Kit Text Recognition (MAUI-binding, t.ex.
  `Plugin.MAUI.MLKitTextRecognition` eller egen Android-implementation via
  `Xamarin.Google.MLKit.Vision.Text`).
- Resultatet visas som **redigerbar förhandsgranskning** i samma UI som manuell
  inmatning — användaren bekräftar eller rättar innan det sparas.
  Bekräftelseskärmen är obligatorisk även om OCR är 100% säker.
- Stöd för flera bilder per kväll (t.ex. tre bilder = två kompletta omgångar
  + en partiell).

## Ångra och historik

- Alla skapade entiteter (kväll, omgång, bild-import) kan markeras
  `DeletedAt = now()`.
- "Senaste handling kan ångras" — visa en undo-knapp efter inmatning.
- Statistik räknar aldrig på rader med `DeletedAt != null`.
- Importerade seed-värden (Skiva 4) kan inte raderas via UI utan endast
  genom att redigera databasen direkt.

## Tech-stack

- **.NET MAUI** (senaste LTS), C#.
- **MVVM** med `CommunityToolkit.Mvvm` (`ObservableObject`, `RelayCommand`,
  source generators).
- **SQLite** via `sqlite-net-pcl`.
- **OxyPlot.Maui.Skia** (förstahandsval) eller **Microcharts** för grafer.
- **ClosedXML** för Excel-export, **MailKit** för mail.
- **Google ML Kit Text Recognition** för OCR (Skiva 5).
- **Dependency injection** via `MauiAppBuilder.Services`.

## Projektstruktur

/Models          — POCO + SQLite-attribut (Player, GameNight, Round, TrackResult)
/Data            — DatabaseService, repositories
/Services        — OcrService, ExportService, MailService, StatsCalculator
/ViewModels      — en per vy, ärver ObservableObject
/Views           — XAML-vyer (NewNightPage, RoundEntryPage, StatsPage, etc.)
/Resources       — bilder, stilar
MauiProgram.cs   — DI och appstart
CLAUDE.md        — det här dokumentet

## Konventioner

- Namnge på engelska i kod, svenska i UI-text.
- En vy = en ViewModel = en fil per. Inga god classes.
- Statistikberäkningar i `StatsCalculator`, **inte** i ViewModels eller Views.
- All databasaccess går via repositories i `/Data`. ViewModels rör aldrig
  SQLite direkt.
- UTC i databasen, lokal tid i UI.
- Använd `CancellationToken` för alla async-operationer som kan ta tid
  (OCR, mail, export).

## Definition of done (per skiva)

- Bygger utan varningar för Android-target.
- Inga `TODO`-kommentarer kvar i kod som rör skivans omfång.
- Manuell test på Android-emulator eller fysisk enhet: huvudflödet i skivan
  fungerar.
- Commit med beskrivande meddelande.

## Inte med (anti-scope)

För att undvika överbyggnad: följande är **inte** med i nuvarande plan.
Bygg det inte utan att fråga.

- Molnsynk eller fleranvändarstöd (Supabase, Firebase, etc.).
- Inloggning eller autentisering.
- iOS-build (kommer senare, men koden ska inte vara Android-specifik utan
  god anledning).
- Push-notifieringar.
- Avancerad statistik utöver det som är specat (huvud-till-huvud, vinstserier,
  etc.) — kan läggas till senare.
- Realtidssynk mellan flera telefoner.

## Öppna frågor

Lös innan kod skrivs som beror på dem:

1. Exakt format på poängtavla-fotot — väntar på exempelbilder för att designa
   OCR-parser.
2. Mailbackup: SMTP via Gmail (kräver app-lösenord) eller plattformens
   dela-intent? Bestäm i Skiva 3.
   
## Git-arbetsflöde

- Allt arbete som Claude Code gör ska ske på en branch som börjar med `claude/`.
- Branchnamn: `claude/skiva-N-kort-beskrivning`, t.ex. `claude/skiva-1-datamodell`
  eller `claude/skiva-2-statistik-grafer`. För mindre delsteg inom en skiva:
  `claude/skiva-1-data-models`, `claude/skiva-1-manual-entry-ui`.
- Skapa branchen *innan* några filer ändras. Använd `git checkout -b claude/...`
  från `main`.
- Commit:a på branchen löpande med beskrivande commit-meddelanden.
- När arbetet är klart, branchen bygger felfritt och uppfyller "definition of
  done" för skivan: pusha branchen och **öppna en Pull Request på GitHub**.
- **Merga aldrig till `main` själv.** Användaren granskar och mergar PR:en
  manuellt via GitHubs UI.

### PR-krav

Varje PR ska innehålla:

- **Titel:** kort och beskrivande, t.ex. `Skiva 1: Datamodell och SQLite-setup`.
- **Beskrivning** (PR-body) med dessa avsnitt:
  - **Vad:** punktlista över vad som ändrats.
  - **Varför:** vilken del av PRD:n / vilken skiva detta uppfyller.
  - **Hur testat:** vilka manuella tester som körts (t.ex. "byggt utan
    varningar för Android, startar appen, skapar en kväll, lägger till en
    omgång, statistik filtrerar på DeletedAt").
  - **Avvikelser från PRD:** om något medvetet gjorts annorlunda än PRD:n
    säger — förklara varför. Om inga avvikelser, skriv "Inga".
  - **Öppna frågor / följdarbete:** saker som upptäcktes men inte ingår i
    denna PR.
- **Liten diff:** håll PR:en fokuserad på en skiva eller ett tydligt delsteg.
  Om PR:en växer till över ~500 rader ändrad kod, överväg att dela upp den.
- **Ren historik:** commit-meddelanden i imperativ form ("Add Player model",
  inte "added player model" eller "stuff").

### Verktyg för PR

- Använd antingen `gh` (GitHub CLI) om det är installerat, eller skriv ut
  PR-URL:en till terminalen så användaren kan klicka.
- Om `gh` saknas: pusha branchen och skriv en färdig PR-titel + beskrivning
  som användaren kan kopiera in på GitHub.

### Om en PR underkänns

- Användaren kan begära ändringar i PR:en. Gör då commits på samma branch
  och pusha igen — PR:en uppdateras automatiskt.
- Skapa inte en ny PR om den ursprungliga bara behöver justeras.