# DoubleDashScore Web

Läs-bar mobil-webb för DoubleDashScore. Speglar C#-appens data via en
manuellt uppladdad `.db`-fil. Ingen redigering, ingen inloggning, inget
skriv-API — bara visa data.

Syfte: kompisarna (varav en har iPhone) ska kunna kolla resultatet utan att
utvecklaren behöver bli Apple Developer och distribuera en iOS-app. Webben
läggs som hemskärms-genväg på deras telefoner.

Servas på: **https://sticclassic.github.io/DoubleDashScore/** (default
GitHub Pages-URL, ingen custom domain).

## Appen är sanning — webben mirrorar

Det här projektet duplicerar medvetet en liten del av appens läslogik i
JavaScript. **C#-appen är den auktoritativa källan.** Ändras en formel, en
färg eller en regel i appen måste motsvarande spegling här uppdateras för
hand. Kända speglingar (sök på kommentaren "speglar" i `app.js`):

| Webb (`app.js`)              | App (sanning)                                            |
|------------------------------|----------------------------------------------------------|
| `pointsFor`                  | `Services/StatsCalculator.PointsFor`                     |
| `tracksFor`                  | `Services/StatsCalculator.TracksFor` / `HistoricalTracksFor` |
| `isComplete`                 | `Data/RoundCompletionRule.cs`                            |
| `calculateRoundPositions`    | `Services/StatsCalculator.CalculateRoundPositions` (competition ranking 1-2-2-4) |
| `buildNightSummaries`        | `Data/GameNightRepository.GetSummariesAsync`             |
| `buildHistory`               | `Services/StatsCalculator.CalculateHistory` (inkl. `ApplySeed`) |
| `formatPlacements`           | `ViewModels/HistoryStatsViewModel.FormatPlacements`      |
| `formatRoundCount`           | `ViewModels/NightsListViewModel.FormatRoundCount`        |
| `formatAverage`              | `career.ToString("0.00", sv-SE)` i Total­score-VM:erna    |
| `renderWinners`              | `ViewModels/NightsListViewModel.BuildWinnersText`        |
| `renderTotalscore`           | `Views/TotalscoreTable.xaml` + `HistoryStatsViewModel`   |
| `renderPlacementsTable`      | `Views/PlacementsTable.xaml` + `HistoryStatsViewModel`   |
| `buildGraphs` (night)        | `StatsCalculator.CalculateHistory` `AverageByPlayer` (kvällssnitt) |
| `buildGraphs` (career)       | `HistoryStatsViewModel.BuildCumulativeCareerAverages` (rullande snitt) |
| graf-etikett `"Kväll N"`     | `HistoryStatsViewModel.BuildNightLabel`                  |
| Y-lås 1–4, marker, scrub, spelartoggle | `HistoryStatsViewModel.BuildPlotModel` + `ChartTransferStore` |
| `PLAYER_COLORS`              | `Services/PlayerColors.cs` (`HexByName`)                 |

**`buildHistory` gör en enda pass** över seed + live och speglar
`CalculateHistory`:

- **Totalscore-counts** (1:or/2:or/3:or/4:or) = `HistoricalPositionTotalsSnapshot`
  (auktoritativ historisk bas) + live kompletta omgångars competition-ranking-
  placeringar. Historiska `HistoricalRoundPlacements` räknas **inte** in i
  counts (snapshot inkluderar dem redan — annars dubbelräkning).
- **Karriärsnitt** = `(Σ historiska banpoäng + Σ live banpoäng) / (Σ historiska
  banor + Σ live banor)`, viktat per bana. Banor = summan av de fyra
  positionsräknarna (även för historiska aggregat — **inte** `TotalTracks`-
  kolumnen). Detta är Totalscore-tabbens formel, **inte** karriärgrafens
  oviktade löpande medel (skiva 24).
- **Placeringsserien** = seed-kvällar (asc `NightNumber`, etikett "Kväll N")
  följt av live-kvällar (asc `PlayedOn`, etikett sv-SE-datum). Endast live-
  kvällar med minst en omgång tas med (appen filtrerar `Rounds.Count > 0`).
  Placeringscell = `, `-separerade positioner, tie märks med `*` (t.ex.
  `1*, 2`); tom cell om spelaren saknar placeringar den kvällen.
- **Ordning i UI:** Placeringar och Översiktens senaste-4 visas **nyaste
  först** (serien reverseras vid render). Appen visar dem stigande med
  auto-scroll till botten — se avvikelse-noten i skiva 23:s PR.

**`buildGraphs` (skiva 24)** bygger dataserier för de två graferna i en pass:

- **Kvällsgraf** = kvällssnitt per kväll (`AverageByPlayer`): banpoäng/banor,
  1–4 där **högre är bättre** (4 = alla förstaplatser). Seed:
  `HistoricalPointsFor/HistoricalTracksFor`. Live: `nightPoints/nightTracks`
  över alla omgångar (även partiella). Detta är appens Kvällsgraf — **inte**
  "snittplacering" (1 bäst); mät på poäng, inte position.
- **Karriärgraf** = **oviktat löpande medel** av kvällssnitten t.o.m. kväll N.
  Detta är "karriärsnittformelns avvikelse": karriärgrafen använder oviktat
  medel (inte `points/tracks` som Totalscore-tabellen) eftersom historisk
  seed-data saknar banantal per kväll. Sluttalen skiljer sig därför något
  mellan Karriärgrafens sista punkt och Totalscore-tabellens Karriärsnitt —
  det är **avsiktligt**, inte en bugg.
- **Y-axeln är låst 1–4** på båda graferna (som `BuildPlotModel`), så att
  avväljning av en spelarlinje aldrig får skalan att hoppa.
- **Etikett** per kväll = `"Kväll N"` (`BuildNightLabel`): seed-kvällar
  använder sitt `NightNumber`, live-kvällar sitt kronologiska index.
- **Delat läge** (`graphState`): scrub-position och avvalda spelare gäller
  **båda** graferna samtidigt (speglar `ChartTransferStore` som delar
  `SelectedNightIndex` + `HiddenPlayerNames`). Default-vald kväll = senaste.

### Grafbibliotek: Chart.js 4.5.1 (pinnad)

- Importeras som ES-modul: `https://cdn.jsdelivr.net/npm/chart.js@4.5.1/auto/+esm`.
  **`auto`-ingången** auto-registrerar alla controllers/scales så vi slipper
  `Chart.register(...)`. Ren canvas/2D på **main-tråden** — inget worker-krav,
  fungerar på iOS Safari + Android Chrome. Bundlen (~200 KB min, brotli ~60 KB)
  drar med sig sin enda dep (`@kurkle/color`) från samma CDN.
- **Pinnad** (inte `@latest`) för reproducerbarhet — 4.x har inga brytande
  ändringar men vi vill undvika överraskningar vid framtida majors.
- Legenden är **egen HTML** (inte Chart.js inbyggda), eftersom den visar den
  valda kvällens värden per spelare (scrubber-stil) och togglar linjer.
  Tooltip + inbyggd legend är avstängda; scrub drivs av egna pointer-events
  (`touch-action: pan-y` så vertikal sid-scroll finns kvar).

### Helskärm för grafer (Alt A)

Trigger: **explicit ⛶-knapp** per graf (inte tap-på-graf) — tap/drag är redan
scrub-gesten, och appen använder också en dedikerad "Helskärm"-knapp. En delad
overlay (`#fs-overlay`) återanvänds för båda graferna.

Strategin är **en orientation-driven klass-toggle** som täcker båda plattformar:

- **Android Chrome:** `element.requestFullscreen()` + `screen.orientation.lock('landscape')`
  → enheten roterar fysiskt → vyn blir landskap → ingen CSS-rotation behövs.
- **iOS Safari:** saknar element-`requestFullscreen` och `screen.orientation.lock`
  → båda anropen no-op:ar (feature-detektion: `if (el.requestFullscreen)` /
  `if (screen.orientation?.lock)`, plus `try/catch` runt `lock()` eftersom
  vissa Android-browsers avvisar lock även när API:t finns). Vyn förblir
  porträtt → `updateRotation()` sätter klassen `.rotate` som CSS-roterar hela
  grafinnehållet 90° (wrappen får `100vh × 100vw` + `rotate(90deg)`) så det
  visas liggande.
- `updateRotation()` lyssnar på `orientationchange`/`resize` och togglar
  `.rotate` enbart utifrån `matchMedia('(orientation: portrait)')`. Vrider
  användaren fysiskt telefonen till landskap (iOS) tas rotationen bort och
  grafen fyller viewporten direkt. Stäng med ✕-knappen (exit fullscreen +
  `orientation.unlock()`).

### ⚠️ Spelarfärger

Färgerna i `PLAYER_COLORS` (`app.js`) är **hårdkodade** och MÅSTE hållas i
synk med `Services/PlayerColors.cs` i appen. Vid ändring: uppdatera **båda**
ställena. Nuvarande palett:

- Claes `#E55A1F` (röd-orange)
- Robin `#1F77B4` (blå)
- Aleksi `#2CA02C` (grön)
- Jonas `#B8860B` (mörk gul/guld)

## Mapp-struktur

```
web/
  index.html      Sidstruktur: header + Kvällar + Kvällsgraf + Översikt
                  + Placeringar + Karriärgraf (alla sektioner byggda)
  style.css       Mörkt tema, matchar appen
  app.js          Huvudlogik (ES-modul): laddar db, beräknar, renderar
  appicon.png     Kopia av appens Resources/AppIcon/appicon.png
  data/
    db.sqlite     Databasen (manuellt uppladdad — se "Uppdatera databasen")
  CLAUDE.md       Det här dokumentet
```

## Tekniska val

- **Vanilla HTML/JS/CSS.** Ingen framework, ingen bundler, inget build-steg.
  Browsern läser koden direkt. `app.js` är en ES-modul (`<script type="module">`).
- **sqlite-wasm** `@sqlite.org/sqlite-wasm@3.50.4-build1` från jsDelivr:
  `https://cdn.jsdelivr.net/npm/@sqlite.org/sqlite-wasm@3.50.4-build1/sqlite-wasm/jswasm/sqlite3.mjs`
  Versionen är **pinnad** (inte `@latest`) med flit — `3.53.0` och senare
  har byggts om till en worker-orienterad `dist/`-struktur utan den stabila
  main-thread-ESM-ingången (`jswasm/sqlite3.mjs`) vi använder här.
  - Vi kör i **main-tråden** och läser en **transient** databas: `.db`-filen
    hämtas med `fetch` → `sqlite3_deserialize` in i en in-memory-DB. Vi
    använder **inte** OPFS/persistens.
  - **Därför krävs inga COOP/COEP-headers** (`Cross-Origin-Opener-Policy` /
    `Cross-Origin-Embedder-Policy`). De behövs bara för OPFS/SharedArrayBuffer,
    som vi undviker. Det är viktigt eftersom **GitHub Pages inte kan sätta
    egna HTTP-headers** — vår upplägg fungerar ändå. Rör inte det här (t.ex.
    byt inte till worker/OPFS-läget) utan att lösa header-frågan.
  - **Bundle-storlek:** `sqlite3.mjs` ≈ 460 KB + `sqlite3.wasm` ≈ 840 KB
    okomprimerat; jsDelivr serverar brotli så det blir ~0,5 MB över tråden.
    Cachas av browsern efter första besöket. Väl under 3 s första render på
    mobil-4G.
- **Baloo 2** som font (Google Fonts) — matchar appen (`Baloo2`/`Baloo2Bold`).
- **Chart.js 4.5.1** driver Kvällsgraf + Karriärgraf (skiva 24). Detaljer +
  pinning-motivering under "Grafbibliotek: Chart.js" nedan. Hämta **inte** ett
  konkurrerande graf-lib.

## Design-konventioner

Matcha appen så nära som möjligt (referens: den redesignade Kvällar-vyn från
skiva 20). Värdena speglar `Resources/Styles/Colors.xaml`.

- **Bakgrund:** svart `#000000`.
- **Accent/Primary:** knallorange `#FB923C`.
- **Text:** vit `#FFFFFF`; **dämpad** `#ACACAC` (Gray300); **separatorer**
  `rgba(255,255,255,.5)`.
- **Kort:** `rgba(255,255,255,.05)`, radie 12 px.
- **Font:** Baloo 2 (400/500/600/700).
- Layouten är **en enda scrollsida** (ingen navigation): header →
  Kvällar → Kvällsgraf → Översikt → Placeringar → Karriärgraf.
- **Read-only-skillnader mot appen:** ingen "Lägg till ny kväll"-knapp,
  ingen papperskorgs-ikon, ingen hamburgermeny, ingen tap/edit/delete.

## Poängsystem (spegling)

Per bana: **1:a = 4 p, 2:a = 3 p, 3:e = 2 p, 4:e = 1 p.** En omgång är 16
banor. En omgång är **komplett** om `TrackCount == 16 && Results.Count == 4`.
**Vinnare** av en omgång = spelaren med högst total banpoäng. **Oavgjort:**
alla oavgjorda tas med (delad seger, visas `Namn1/Namn2`). Partiella
omgångar räknas inte och visas inte i vinnar-raden.

## Datum

`GameNight.PlayedOn` lagras av `sqlite-net` som **.NET-ticks** (heltal,
100 ns sedan 0001-01-01, UTC). `app.js` konverterar ticks → JS-`Date` och
formaterar i webbläsarens lokala tidszon med `sv-SE` (`26 maj 2026`) — samma
som appens `ToLocalTime()` + `d MMMM yyyy`. (Ticks är ~6,4·10¹⁷ vilket
överstiger `Number.MAX_SAFE_INTEGER`; precisionsförlusten ligger på
sub-mikrosekundnivå och påverkar aldrig vilket *datum* som visas.)

## Deployment

- **Workflow:** `.github/workflows/pages-deploy.yml`. Deployar **enbart**
  `web/`-mappen (via `actions/upload-pages-artifact` med `path: web` +
  `actions/deploy-pages`). Triggar på push till `main` när `web/**` eller
  workflowen ändras, samt manuellt via `workflow_dispatch`.
- **Engångs-setup (första gången):** efter att workflowen körts måste
  Pages-källan bytas manuellt i repo-inställningarna:
  **Settings → Pages → Build and deployment → Source: "GitHub Actions".**
  Detta görs bara en gång; därefter deployar varje push till `main` som rör
  `web/` automatiskt.

## Uppdatera databasen

1. Exportera en `.db` från appen (Inställningar → dela/exportera).
2. Gå till repot i GitHubs webbgränssnitt → `web/data/` → ladda upp filen och
   döp den till **`db.sqlite`** (skriv över den befintliga). Enklast via
   "Add file → Upload files" och dra-och-släpp, eller öppna `db.sqlite` och
   välj att ersätta den.
3. Commit till `main` → workflowen kör → sidan uppdateras automatiskt.

`app.js` visar "Ingen databas uppladdad än" så länge `db.sqlite` är den
tomma platshållaren (0 byte).

## Definition of done

- Fungerar i **Safari (iOS)** och **Chrome (Android)** i mobil-vy.
- Kvällar-sektionen renderar alla kvällar (nyaste först) med datum,
  omgångsantal, ev. anteckning och vinnare per komplett omgång i rätt
  spelarfärger.
- Alla sektioner byggda: Kvällar, Kvällsgraf, Översikt, Placeringar,
  Karriärgraf — inga placeholder-headings kvar.
- Graferna: rätt spelarfärger, Y-axel låst 1–4, scrub uppdaterar legenden,
  spelartoggle döljer linje utan att skalan hoppar, ⛶ → helskärm (landscape
  på Android, CSS-rotation på iOS).
