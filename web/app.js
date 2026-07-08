// DoubleDashScore Web — läs-bar mobil-webb som speglar C#-appens data.
// Läser en manuellt uppladdad SQLite-fil (data/db.sqlite) i browsern via
// sqlite-wasm och renderar Kvällar-sektionen. Ingen redigering, inget skriv-API.
//
// VIKTIGT: logiken här speglar appens C#-kod och MÅSTE hållas i synk med den.
//   - Poäng/vinnare: Services/StatsCalculator.PointsFor + GameNightRepository.GetSummariesAsync
//   - Spelarfärger:  Services/PlayerColors.cs
//   - Komplett omgång: Data/RoundCompletionRule.cs (TrackCount == 16 && Results.Count == 4)
//   - Statistik/grafer: Services/StatsCalculator.CalculateHistory + HistoryStatsViewModel
// Appen är sanning, webben mirrorar. Ändras något där, ändra här också.

import sqlite3InitModule
    from "https://cdn.jsdelivr.net/npm/@sqlite.org/sqlite-wasm@3.50.4-build1/sqlite-wasm/jswasm/sqlite3.mjs";
// Chart.js pinnad på 4.5.1 (senaste stabila 4.x). "auto"-ingången
// auto-registrerar alla controllers/scales så vi slipper manuell
// Chart.register(...). Ren canvas/main-thread — inget worker-krav, funkar på
// iOS Safari + Android Chrome. Se web/CLAUDE.md.
import Chart from "https://cdn.jsdelivr.net/npm/chart.js@4.5.1/auto/+esm";

// Spelarfärger — hårdkodade speglingar av Services/PlayerColors.cs (HexByName).
// MÅSTE hållas i synk med appen. Se web/CLAUDE.md.
const PLAYER_COLORS = {
    claes:  "#E55A1F",
    robin:  "#1F77B4",
    aleksi: "#2CA02C",
    jonas:  "#B8860B",
};
const WINNER_FALLBACK_COLOR = "#ACACAC";

// Poängsystem (per bana): 1:a = 4, 2:a = 3, 3:e = 2, 4:e = 1. Speglar
// StatsCalculator.PointsFor.
function pointsFor(r) {
    return 4 * r.FirstPlaces + 3 * r.SecondPlaces + 2 * r.ThirdPlaces + 1 * r.FourthPlaces;
}

// En omgång är komplett om den har 16 banor OCH exakt 4 resultatrader.
// Speglar Data/RoundCompletionRule.cs.
function isComplete(trackCount, resultsCount) {
    return trackCount === 16 && resultsCount === 4;
}

const svSe = new Intl.DateTimeFormat("sv-SE", { day: "numeric", month: "long", year: "numeric" });

// PlayedOn lagras av sqlite-net som .NET-ticks (100 ns sedan 0001-01-01, UTC),
// ett tal ~6,4·10¹⁷ som överstiger Number.MAX_SAFE_INTEGER. Att läsa det råa
// heltalet till JS spränger 53-bitars-precisionen (sqlite-motorer kastar eller
// trunkerar). Därför konverteras ticks → epoch-millisekunder **i SQL** (64-bit
// heltalsaritmetik), och JS får ett tal i säker range. Se PLAYED_ON_MS_SQL.
const TICKS_EPOCH_OFFSET_MS = 62135596800000; // ms mellan 0001-01-01 och 1970-01-01
const PLAYED_ON_MS_SQL = `(PlayedOn / 10000 - ${TICKS_EPOCH_OFFSET_MS})`;

function formatDate(playedOnMs) {
    const d = new Date(playedOnMs);
    if (Number.isNaN(d.getTime())) return "Okänt datum";
    return svSe.format(d);
}

// Speglar NightsListViewModel.FormatRoundCount.
function formatRoundCount(total, complete) {
    if (total === 0) return "Inga omgångar";
    const roundWord = total === 1 ? "omgång" : "omgångar";
    const completeWord = complete === 1 ? "komplett" : "kompletta";
    return `${total} ${roundWord} (${complete} ${completeWord})`;
}

function colorForName(name) {
    return PLAYER_COLORS[String(name).toLowerCase()] ?? WINNER_FALLBACK_COLOR;
}

// --- databas ---------------------------------------------------------------

function queryAll(db, sql) {
    return db.exec({ sql, rowMode: "object", returnValue: "resultRows" });
}

// Beräknar vinnare per komplett omgång per kväll — speglar
// GameNightRepository.GetSummariesAsync (samma loop räknar kompletta omgångar
// och plockar vinnare). Vinnare = spelare med flest banpoäng; lika poäng ⇒
// delad seger (alla oavgjorda tas med). Partiella omgångar utelämnas helt.
function buildNightSummaries(db) {
    const players = queryAll(db, "SELECT Id, Name FROM Players WHERE DeletedAt IS NULL");
    const nameById = new Map(players.map(p => [p.Id, p.Name]));

    const nights = queryAll(db,
        `SELECT Id, ${PLAYED_ON_MS_SQL} AS PlayedOnMs, Note ` +
        "FROM GameNights WHERE DeletedAt IS NULL ORDER BY PlayedOn DESC");

    const rounds = queryAll(db,
        "SELECT Id, GameNightId, RoundNumber, TrackCount FROM Rounds WHERE DeletedAt IS NULL");

    const results = queryAll(db,
        "SELECT RoundId, PlayerId, FirstPlaces, SecondPlaces, ThirdPlaces, FourthPlaces " +
        "FROM RoundResults WHERE DeletedAt IS NULL");

    const resultsByRound = new Map();
    for (const r of results) {
        if (!resultsByRound.has(r.RoundId)) resultsByRound.set(r.RoundId, []);
        resultsByRound.get(r.RoundId).push(r);
    }

    const roundsByNight = new Map();
    for (const round of rounds) {
        if (!roundsByNight.has(round.GameNightId)) roundsByNight.set(round.GameNightId, []);
        roundsByNight.get(round.GameNightId).push(round);
    }

    return nights.map(night => {
        const nightRounds = (roundsByNight.get(night.Id) ?? [])
            .slice()
            .sort((a, b) => a.RoundNumber - b.RoundNumber);

        let complete = 0;
        const winnersByRound = [];
        for (const round of nightRounds) {
            const roundResults = resultsByRound.get(round.Id) ?? [];
            if (!isComplete(round.TrackCount, roundResults.length)) continue;
            complete++;

            const maxPoints = Math.max(...roundResults.map(pointsFor));
            const winners = roundResults
                .filter(r => pointsFor(r) === maxPoints)
                .map(r => nameById.get(r.PlayerId) ?? `#${r.PlayerId}`);
            winnersByRound.push(winners);
        }

        return {
            date: formatDate(night.PlayedOnMs),
            note: night.Note,
            roundsSummary: formatRoundCount(nightRounds.length, complete),
            winnersByRound,
        };
    });
}

// Banor en spelare kört i en omgång/aggregat = summan av de fyra räknarna.
// Speglar StatsCalculator.TracksFor / HistoricalTracksFor (OBS: historiska
// aggregat använder summan, INTE TotalTracks-kolumnen).
function tracksFor(r) {
    return r.FirstPlaces + r.SecondPlaces + r.ThirdPlaces + r.FourthPlaces;
}

// Omgångsplacering via standard competition ranking (1-2-2-4) efter total
// banpoäng, fallande. Speglar StatsCalculator.CalculateRoundPositions.
function calculateRoundPositions(results) {
    const sorted = results
        .map(r => ({ id: r.PlayerId, points: pointsFor(r) }))
        .sort((a, b) => b.points - a.points);
    const positions = new Map();
    let rank = 0;
    let prev = null;
    for (let i = 0; i < sorted.length; i++) {
        if (i === 0 || sorted[i].points !== prev) rank = i + 1;
        positions.set(sorted[i].id, rank);
        prev = sorted[i].points;
    }
    return positions;
}

// Formaterar en spelares placeringar en kväll: ", "-separerat, tie märks med
// "*" (t.ex. "1*, 2"). Tom sträng om spelaren saknar placeringar den kvällen.
// Speglar HistoryStatsViewModel.FormatPlacements.
function formatPlacements(list) {
    if (!list || list.length === 0) return "";
    return list.map(p => (p.tied ? `${p.pos}*` : `${p.pos}`)).join(", ");
}

// sv-SE, 2 decimaler, komma som decimaltecken ("3,03"). Speglar appens
// career.ToString("0.00", sv-SE).
function formatAverage(value) {
    return value.toFixed(2).replace(".", ",");
}

// Bygger Totalscore-rader + kronologisk placeringsserie i EN pass över seed +
// live-data — speglar StatsCalculator.CalculateHistory (inkl. ApplySeed).
// Ingen aggregering sker sedan i renderings-looparna.
//
//   Totalscore-counts = PositionTotalsSnapshot (historisk bas) + live kompletta
//     omgångars competition-ranking-placeringar.
//   Karriärsnitt      = (Σ historiska banpoäng + Σ live banpoäng)
//                       / (Σ historiska banor + Σ live banor).
//   series            = seed-kvällar (asc NightNumber) följt av live-kvällar
//                       (asc PlayedOn); varje punkt bär redan färdig-
//                       formaterade cell-strängar per spelare (orderedIds-ordning).
function buildHistory(db) {
    // Spelare i DisplayOrder — samma "orderedIds" som appen använder.
    const players = queryAll(db,
        "SELECT Id, Name, DisplayOrder FROM Players WHERE DeletedAt IS NULL ORDER BY DisplayOrder");
    const orderedIds = players.map(p => p.Id);
    const nameById = new Map(players.map(p => [p.Id, p.Name]));

    const counts = new Map(orderedIds.map(id => [id, { f: 0, s: 0, t: 0, fo: 0 }]));
    const careerPoints = new Map(orderedIds.map(id => [id, 0]));
    const careerTracks = new Map(orderedIds.map(id => [id, 0]));
    const series = []; // { label, cells: [c0, c1, c2, c3] } i orderedIds-ordning

    // --- SEED (speglar ApplySeed) ---
    // 1. Snapshot är auktoritativ bas för historiska position-totals.
    for (const snap of queryAll(db,
        "SELECT PlayerId, Firsts, Seconds, Thirds, Fourths FROM HistoricalPositionTotalsSnapshot")) {
        if (!counts.has(snap.PlayerId)) continue;
        counts.set(snap.PlayerId, { f: snap.Firsts, s: snap.Seconds, t: snap.Thirds, fo: snap.Fourths });
    }
    // 2. Historiska aggregat bidrar med banpoäng + banor till karriärsnittet.
    const aggregates = queryAll(db,
        "SELECT NightNumber, PlayerId, FirstPlaces, SecondPlaces, ThirdPlaces, FourthPlaces " +
        "FROM HistoricalNightAggregates");
    for (const a of aggregates) {
        if (!counts.has(a.PlayerId)) continue;
        careerPoints.set(a.PlayerId, careerPoints.get(a.PlayerId) + pointsFor(a));
        careerTracks.set(a.PlayerId, careerTracks.get(a.PlayerId) + tracksFor(a));
    }
    // 3. Seed-serie: en punkt per historisk kväll (asc NightNumber) med
    //    placeringar per (kväll, omgång). Tie = placeringen delas i omgången.
    const placements = queryAll(db,
        "SELECT NightNumber, PlayerId, RoundIndex, Position FROM HistoricalRoundPlacements");
    const placementsByNight = new Map();
    for (const p of placements) {
        if (!placementsByNight.has(p.NightNumber)) placementsByNight.set(p.NightNumber, []);
        placementsByNight.get(p.NightNumber).push(p);
    }
    const seedNightNumbers = [...new Set(aggregates.map(a => a.NightNumber))].sort((a, b) => a - b);
    for (const nightNumber of seedNightNumbers) {
        const perPlayer = new Map(orderedIds.map(id => [id, []]));
        const nightPlacements = placementsByNight.get(nightNumber) ?? [];
        const roundIndices = [...new Set(nightPlacements.map(p => p.RoundIndex))].sort((a, b) => a - b);
        for (const roundIndex of roundIndices) {
            const rows = nightPlacements.filter(p => p.RoundIndex === roundIndex);
            const freq = new Map();
            for (const r of rows) freq.set(r.Position, (freq.get(r.Position) ?? 0) + 1);
            const byPlayer = new Map(rows.map(r => [r.PlayerId, r]));
            for (const id of orderedIds) {
                const r = byPlayer.get(id);
                if (!r) continue;
                perPlayer.get(id).push({ pos: r.Position, tied: freq.get(r.Position) > 1 });
            }
        }
        series.push({
            label: `Kväll ${nightNumber}`,
            cells: orderedIds.map(id => formatPlacements(perPlayer.get(id))),
        });
    }

    // --- LIVE (speglar huvudloopen i CalculateHistory) ---
    // Endast kvällar med minst en omgång (appen filtrerar withRounds > 0).
    const nights = queryAll(db,
        `SELECT Id, ${PLAYED_ON_MS_SQL} AS PlayedOnMs ` +
        "FROM GameNights WHERE DeletedAt IS NULL ORDER BY PlayedOn ASC");
    const rounds = queryAll(db,
        "SELECT Id, GameNightId, RoundNumber, TrackCount FROM Rounds WHERE DeletedAt IS NULL");
    const results = queryAll(db,
        "SELECT RoundId, PlayerId, FirstPlaces, SecondPlaces, ThirdPlaces, FourthPlaces " +
        "FROM RoundResults WHERE DeletedAt IS NULL");

    const resultsByRound = new Map();
    for (const r of results) {
        if (!resultsByRound.has(r.RoundId)) resultsByRound.set(r.RoundId, []);
        resultsByRound.get(r.RoundId).push(r);
    }
    const roundsByNight = new Map();
    for (const round of rounds) {
        if (!roundsByNight.has(round.GameNightId)) roundsByNight.set(round.GameNightId, []);
        roundsByNight.get(round.GameNightId).push(round);
    }

    for (const night of nights) {
        const nightRounds = (roundsByNight.get(night.Id) ?? [])
            .slice()
            .sort((a, b) => a.RoundNumber - b.RoundNumber);
        if (nightRounds.length === 0) continue;

        const perPlayer = new Map(orderedIds.map(id => [id, []]));
        for (const round of nightRounds) {
            const roundResults = resultsByRound.get(round.Id) ?? [];
            // Karriärsnittet räknar ALLA banor (även partiella omgångar).
            for (const r of roundResults) {
                careerPoints.set(r.PlayerId, careerPoints.get(r.PlayerId) + pointsFor(r));
                careerTracks.set(r.PlayerId, careerTracks.get(r.PlayerId) + tracksFor(r));
            }
            // Placeringar + totalscore-counts bara för kompletta omgångar.
            if (isComplete(round.TrackCount, roundResults.length)) {
                const positions = calculateRoundPositions(roundResults);
                const freq = new Map();
                for (const pos of positions.values()) freq.set(pos, (freq.get(pos) ?? 0) + 1);
                for (const id of orderedIds) {
                    const pos = positions.get(id);
                    const c = counts.get(id);
                    if (pos === 1) c.f++;
                    else if (pos === 2) c.s++;
                    else if (pos === 3) c.t++;
                    else c.fo++;
                    perPlayer.get(id).push({ pos, tied: freq.get(pos) > 1 });
                }
            }
        }
        series.push({
            label: formatDate(night.PlayedOnMs),
            cells: orderedIds.map(id => formatPlacements(perPlayer.get(id))),
        });
    }

    const totalsRows = orderedIds.map(id => {
        const c = counts.get(id);
        const tracks = careerTracks.get(id);
        const avg = tracks === 0 ? 0 : careerPoints.get(id) / tracks;
        return {
            id,
            name: nameById.get(id),
            firsts: c.f,
            seconds: c.s,
            thirds: c.t,
            fourths: c.fo,
            careerAverage: formatAverage(avg),
        };
    });

    return { players, totalsRows, series };
}

// --- rendering -------------------------------------------------------------

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
}

// Vinnare per omgång: ", " mellan omgångar, "/" mellan oavgjorda spelare i
// samma omgång, varje namn i sin spelarfärg. Speglar
// NightsListViewModel.BuildWinnersText.
function renderWinners(winnersByRound) {
    const wrap = el("div", "night-card__winners");
    if (winnersByRound.length === 0) {
        wrap.appendChild(el("span", "winners-empty", "—"));
        return wrap;
    }
    winnersByRound.forEach((group, g) => {
        if (g > 0) wrap.appendChild(el("span", "winner-sep", ", "));
        group.forEach((name, w) => {
            if (w > 0) wrap.appendChild(el("span", "winner-sep", "/"));
            const span = el("span", "winner", name);
            span.style.color = colorForName(name);
            wrap.appendChild(span);
        });
    });
    return wrap;
}

function renderNightCard(summary) {
    const card = el("div", "night-card");

    const top = el("div", "night-card__top");
    top.appendChild(el("span", "night-card__date", summary.date));
    top.appendChild(el("span", "night-card__rounds", summary.roundsSummary));
    card.appendChild(top);

    if (summary.note && summary.note.trim().length > 0) {
        card.appendChild(el("div", "night-card__note", summary.note));
    }

    card.appendChild(renderWinners(summary.winnersByRound));
    return card;
}

function setStatus(message, isError) {
    const status = document.getElementById("nights-status");
    status.textContent = message ?? "";
    status.classList.toggle("status--error", Boolean(isError));
    status.style.display = message ? "" : "none";
}

function renderNights(summaries) {
    const list = document.getElementById("nights-list");
    list.replaceChildren();
    if (summaries.length === 0) {
        setStatus("Inga kvällar än.", false);
        return;
    }
    setStatus(null, false);
    for (const summary of summaries) {
        list.appendChild(renderNightCard(summary));
    }
}

function setSectionStatus(id, message, isError) {
    const status = document.getElementById(id);
    status.textContent = message ?? "";
    status.classList.toggle("status--error", Boolean(isError));
    status.style.display = message ? "" : "none";
}

// Totalscore-tabell (speglar Views/TotalscoreTable.xaml + HistoryStatsViewModel).
// Spelarnamn i första kolumnen får sin färg. Karriärsnitt döljs bakom en
// toggle (visar "•••" tills användaren klickar "Visa karriärsnitt"), precis
// som appen.
function renderTotalscore(container, totalsRows) {
    container.replaceChildren();
    const wrap = el("div", "totalscore");
    wrap.dataset.career = "hidden";

    const toggle = el("button", "career-toggle", "Visa karriärsnitt");
    toggle.type = "button";
    toggle.addEventListener("click", () => {
        const shown = wrap.dataset.career === "shown";
        wrap.dataset.career = shown ? "hidden" : "shown";
        toggle.textContent = shown ? "Visa karriärsnitt" : "Dölj karriärsnitt";
    });
    wrap.appendChild(toggle);

    const table = el("table", "stat-table");
    const thead = el("thead");
    const htr = el("tr");
    htr.appendChild(el("th", "col-name", "Spelare"));
    for (const h of ["1:or", "2:or", "3:or", "4:or"]) htr.appendChild(el("th", "col-num", h));
    htr.appendChild(el("th", "col-avg", "Karriärsnitt"));
    thead.appendChild(htr);
    table.appendChild(thead);

    const tbody = el("tbody");
    for (const row of totalsRows) {
        const tr = el("tr");
        const nameTd = el("td", "col-name");
        const nameSpan = el("span", "player-name", row.name);
        nameSpan.style.color = colorForName(row.name);
        nameTd.appendChild(nameSpan);
        tr.appendChild(nameTd);
        tr.appendChild(el("td", "col-num", String(row.firsts)));
        tr.appendChild(el("td", "col-num", String(row.seconds)));
        tr.appendChild(el("td", "col-num", String(row.thirds)));
        tr.appendChild(el("td", "col-num", String(row.fourths)));

        const avgTd = el("td", "col-avg");
        avgTd.appendChild(el("span", "career-value", row.careerAverage));
        avgTd.appendChild(el("span", "career-hidden", "•••"));
        tr.appendChild(avgTd);
        tbody.appendChild(tr);
    }
    table.appendChild(tbody);
    wrap.appendChild(table);
    container.appendChild(wrap);
}

// Placeringstabell (speglar Views/PlacementsTable.xaml). Kolumner: Kväll + 4
// spelare (namn i sina färger i header-raden). Cellvärden är redan färdig-
// formaterade i buildHistory; tom cell renderas som dämpat "—".
function renderPlacementsTable(container, rows, players) {
    container.replaceChildren();
    const table = el("table", "stat-table");

    const thead = el("thead");
    const htr = el("tr");
    htr.appendChild(el("th", "col-name", "Kväll"));
    for (const p of players) {
        const th = el("th", "col-num");
        const span = el("span", "player-name", p.Name);
        span.style.color = colorForName(p.Name);
        th.appendChild(span);
        htr.appendChild(th);
    }
    thead.appendChild(htr);
    table.appendChild(thead);

    const tbody = el("tbody");
    for (const row of rows) {
        const tr = el("tr");
        tr.appendChild(el("td", "col-name", row.label));
        for (const cell of row.cells) {
            tr.appendChild(cell === ""
                ? el("td", "col-num cell-empty", "—")
                : el("td", "col-num", cell));
        }
        tbody.appendChild(tr);
    }
    table.appendChild(tbody);
    container.appendChild(table);
}

// Renderar Översikt (Totalscore + senaste 4 kvällarna) och Placeringar (alla
// kvällar, nyaste först) från en enda buildHistory-pass.
function renderStatistics(history) {
    if (history.series.length === 0 && history.totalsRows.every(r => r.firsts === 0
        && r.seconds === 0 && r.thirds === 0 && r.fourths === 0)) {
        setSectionStatus("totalscore-status", "Ingen statistik än.", false);
        setSectionStatus("oversikt-status", "Ingen statistik än.", false);
        setSectionStatus("placeringar-status", "Inga kvällar än.", false);
        return;
    }
    setSectionStatus("totalscore-status", null, false);
    setSectionStatus("oversikt-status", null, false);
    setSectionStatus("placeringar-status", null, false);

    // Totalscore-tabellen visas i två tabbar: fristående "Totalscore" och
    // överst i "Översikt". Två oberoende instanser (var sin karriärsnitt-toggle).
    renderTotalscore(document.getElementById("totalscore-table"), history.totalsRows);
    renderTotalscore(document.getElementById("oversikt-totalscore"), history.totalsRows);
    // Senaste 4 kvällarna = sista 4 i den kronologiska serien, nyaste först.
    renderPlacementsTable(
        document.getElementById("oversikt-placements"),
        history.series.slice(-4).reverse(),
        history.players);
    // Placeringar: alla kvällar, nyaste först.
    renderPlacementsTable(
        document.getElementById("placeringar-placements"),
        [...history.series].reverse(),
        history.players);
}

// --- grafer ----------------------------------------------------------------

const CHART_MUTED = "#ACACAC";
const CHART_GRID = "rgba(255,255,255,0.08)";

// Bygger dataserier för Kvällsgraf + Karriärgraf i en pass över seed + live.
// Speglar StatsCalculator.CalculateHistory (AverageByPlayer per kväll) och
// HistoryStatsViewModel.BuildCumulativeCareerAverages (rullande snitt).
//
//   night  = kvällssnitt = banpoäng / banor den kvällen (1–4, högre bättre).
//            Seed: HistoricalPointsFor/HistoricalTracksFor. Live: nightPoints/
//            nightTracks (alla omgångar, även partiella).
//   career = OVIKTAT löpande medel av kvällssnitten t.o.m. kväll N. Detta är
//            karriärgrafens avvikande formel (inte points/tracks som
//            Totalscore-tabellen) — seed saknar banantal per kväll så viktat
//            snitt går inte att räkna. Se web/CLAUDE.md.
//
// Etikett = "Kväll N": historiska kvällar använder sitt NightNumber, live-
// kvällar sitt kronologiska index (seed-antal + löpnummer) — matchar appens
// BuildNightLabel för graf-legenden.
function buildGraphs(db) {
    const players = queryAll(db,
        "SELECT Id, Name, DisplayOrder FROM Players WHERE DeletedAt IS NULL ORDER BY DisplayOrder");
    const orderedIds = players.map(p => p.Id);

    // Varje punkt: { label, avg: Map<id, number|null> } (kvällssnitt).
    const points = [];

    // --- SEED ---
    const aggregates = queryAll(db,
        "SELECT NightNumber, PlayerId, FirstPlaces, SecondPlaces, ThirdPlaces, FourthPlaces " +
        "FROM HistoricalNightAggregates");
    const aggByNight = new Map();
    for (const a of aggregates) {
        if (!aggByNight.has(a.NightNumber)) aggByNight.set(a.NightNumber, new Map());
        aggByNight.get(a.NightNumber).set(a.PlayerId, a);
    }
    const seedNightNumbers = [...aggByNight.keys()].sort((x, y) => x - y);
    for (const nightNumber of seedNightNumbers) {
        const byPlayer = aggByNight.get(nightNumber);
        const avg = new Map();
        for (const id of orderedIds) {
            const a = byPlayer.get(id);
            avg.set(id, a ? pointsFor(a) / tracksFor(a) : null);
        }
        points.push({ label: `Kväll ${nightNumber}`, avg });
    }

    // --- LIVE (asc PlayedOn, rounds > 0) ---
    const nights = queryAll(db,
        "SELECT Id FROM GameNights WHERE DeletedAt IS NULL ORDER BY PlayedOn ASC");
    const rounds = queryAll(db,
        "SELECT Id, GameNightId, RoundNumber, TrackCount FROM Rounds WHERE DeletedAt IS NULL");
    const results = queryAll(db,
        "SELECT RoundId, PlayerId, FirstPlaces, SecondPlaces, ThirdPlaces, FourthPlaces " +
        "FROM RoundResults WHERE DeletedAt IS NULL");
    const resultsByRound = new Map();
    for (const r of results) {
        if (!resultsByRound.has(r.RoundId)) resultsByRound.set(r.RoundId, []);
        resultsByRound.get(r.RoundId).push(r);
    }
    const roundsByNight = new Map();
    for (const round of rounds) {
        if (!roundsByNight.has(round.GameNightId)) roundsByNight.set(round.GameNightId, []);
        roundsByNight.get(round.GameNightId).push(round);
    }
    let chronoIndex = seedNightNumbers.length;
    for (const night of nights) {
        const nightRounds = (roundsByNight.get(night.Id) ?? [])
            .slice()
            .sort((a, b) => a.RoundNumber - b.RoundNumber);
        if (nightRounds.length === 0) continue;

        const nightPoints = new Map(orderedIds.map(id => [id, 0]));
        const nightTracks = new Map(orderedIds.map(id => [id, 0]));
        for (const round of nightRounds) {
            for (const r of (resultsByRound.get(round.Id) ?? [])) {
                nightPoints.set(r.PlayerId, nightPoints.get(r.PlayerId) + pointsFor(r));
                nightTracks.set(r.PlayerId, nightTracks.get(r.PlayerId) + tracksFor(r));
            }
        }
        chronoIndex++;
        const avg = new Map();
        for (const id of orderedIds) {
            const tracks = nightTracks.get(id);
            avg.set(id, tracks ? nightPoints.get(id) / tracks : null);
        }
        points.push({ label: `Kväll ${chronoIndex}`, avg });
    }

    // Rullande karriärsnitt = oviktat löpande medel av kvällssnitten.
    const sumByPlayer = new Map(orderedIds.map(id => [id, 0]));
    const countByPlayer = new Map(orderedIds.map(id => [id, 0]));
    const cumulative = points.map(point => {
        const cum = new Map();
        for (const id of orderedIds) {
            const a = point.avg.get(id);
            if (a == null) { cum.set(id, null); continue; }
            sumByPlayer.set(id, sumByPlayer.get(id) + a);
            countByPlayer.set(id, countByPlayer.get(id) + 1);
            cum.set(id, sumByPlayer.get(id) / countByPlayer.get(id));
        }
        return cum;
    });

    const labels = points.map(p => p.label);
    const datasets = players.map(p => ({
        id: p.Id,
        name: p.Name,
        color: colorForName(p.Name),
        night: points.map(pt => pt.avg.get(p.Id)),
        career: cumulative.map(c => c.get(p.Id)),
    }));

    return { players, labels, datasets };
}

// Delat läge för båda graferna (+ ev. helskärm): scrub-position och avvalda
// spelare gäller alla samtidigt — medvetet, precis som appens ChartTransferStore.
const graphState = {
    selectedIndex: null,
    hidden: new Set(),
    controllers: [],
    labels: [],
    inline: null, // { night, career } — inline-controllers, för resize vid tabb-byte
};

// Vertikal markörlinje vid vald kväll — Chart.js-plugin per graf.
const markerPlugin = {
    id: "nightMarker",
    afterDatasetsDraw(chart) {
        const idx = graphState.selectedIndex;
        if (idx == null) return;
        const x = chart.scales.x.getPixelForValue(idx);
        if (Number.isNaN(x)) return;
        const { top, bottom } = chart.chartArea;
        const ctx = chart.ctx;
        ctx.save();
        ctx.strokeStyle = "rgba(255,255,255,0.45)";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(x, top);
        ctx.lineTo(x, bottom);
        ctx.stroke();
        ctx.restore();
    },
};

function makeChart(canvas, kind, graphs) {
    return new Chart(canvas, {
        type: "line",
        data: {
            labels: graphs.labels,
            datasets: graphs.datasets.map(d => ({
                label: d.name,
                data: d[kind],
                borderColor: d.color,
                backgroundColor: d.color,
                borderWidth: 2,
                pointRadius: 0,
                pointHitRadius: 0,
                tension: 0,
                spanGaps: true,
                hidden: graphState.hidden.has(d.name),
            })),
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            // Y-skalan är låst 1–4 (som appens BuildPlotModel) → att toggla bort
            // en spelare hoppar aldrig skalan.
            scales: {
                y: {
                    min: 1,
                    max: 4,
                    ticks: { stepSize: 1, color: CHART_MUTED },
                    grid: { color: CHART_GRID },
                },
                x: {
                    grid: { display: false },
                    ticks: {
                        color: CHART_MUTED,
                        autoSkip: true,
                        maxTicksLimit: 8,
                        maxRotation: 0,
                        callback: (_v, i) => (graphs.labels[i] || "").replace("Kväll ", ""),
                    },
                },
            },
            plugins: {
                legend: { display: false },
                tooltip: { enabled: false },
            },
        },
        plugins: [markerPlugin],
    });
}

// Legend under grafen: en chip per spelare med värdet för vald kväll i sin
// färg. Klick togglar spelarens linje (delat state → alla grafer följer med).
function renderChartLegend(legendEl, kind, graphs) {
    legendEl.replaceChildren();
    const idx = graphState.selectedIndex;
    for (const d of graphs.datasets) {
        const item = el("button", "legend-item");
        item.type = "button";
        if (graphState.hidden.has(d.name)) item.classList.add("hidden");

        const swatch = el("span", "legend-swatch");
        swatch.style.background = d.color;
        const name = el("span", "legend-name", d.name);
        name.style.color = d.color;
        const value = idx != null ? d[kind][idx] : null;
        const valueEl = el("span", "legend-value", value == null ? "—" : formatAverage(value));

        item.append(swatch, name, valueEl);
        item.addEventListener("click", () => togglePlayer(d.name));
        legendEl.appendChild(item);
    }
}

function createController(canvas, legendEl, labelEl, kind, graphs) {
    const chart = makeChart(canvas, kind, graphs);
    const controller = {
        kind,
        chart,
        refreshLegend() { renderChartLegend(legendEl, kind, graphs); },
        refreshLabel() {
            if (labelEl) {
                labelEl.textContent = graphState.selectedIndex != null
                    ? graphs.labels[graphState.selectedIndex]
                    : "";
            }
        },
        applyHidden() {
            chart.data.datasets.forEach(ds => { ds.hidden = graphState.hidden.has(ds.label); });
            chart.update("none");
        },
        redraw() { chart.update("none"); },
    };
    attachScrub(canvas, chart);
    graphState.controllers.push(controller);
    return controller;
}

// Scrub längs grafen: pointer-drag väljer närmaste kväll. touch-action pan-y
// (CSS) låter vertikal sid-scroll vara kvar; horisontellt drag scrubbar.
function attachScrub(canvas, chart) {
    let active = false;
    const pick = (offsetX) => {
        const raw = chart.scales.x.getValueForPixel(offsetX);
        if (Number.isNaN(raw)) return;
        const idx = Math.min(Math.max(Math.round(raw), 0), graphState.labels.length - 1);
        setSelectedIndex(idx);
    };
    canvas.addEventListener("pointerdown", (e) => {
        active = true;
        canvas.setPointerCapture?.(e.pointerId);
        pick(e.offsetX);
    });
    canvas.addEventListener("pointermove", (e) => { if (active) pick(e.offsetX); });
    const stop = () => { active = false; };
    canvas.addEventListener("pointerup", stop);
    canvas.addEventListener("pointercancel", stop);
}

function setSelectedIndex(idx) {
    graphState.selectedIndex = idx;
    for (const c of graphState.controllers) {
        c.redraw();
        c.refreshLegend();
        c.refreshLabel();
    }
}

function togglePlayer(name) {
    if (graphState.hidden.has(name)) graphState.hidden.delete(name);
    else graphState.hidden.add(name);
    for (const c of graphState.controllers) {
        c.applyHidden();
        c.refreshLegend();
    }
}

// --- helskärm (Alt A) ---
// Android Chrome: native requestFullscreen() + screen.orientation.lock('landscape')
// roterar enheten. iOS Safari saknar orientation.lock → vi faller tillbaka på
// CSS-rotation (klass .rotate) som visar grafen liggande i porträtt. En enda
// orientation-driven klass-toggle täcker båda: är vyn porträtt roterar vi,
// annars fyller grafen viewporten direkt.
let fsController = null;

function openFullscreen(kind, graphs) {
    const overlay = document.getElementById("fs-overlay");
    const canvas = document.getElementById("fs-canvas");
    const legendEl = document.getElementById("fs-legend");
    destroyFsController();
    overlay.hidden = false;
    fsController = createController(canvas, legendEl, null, kind, graphs);
    fsController.refreshLegend();
    enterNativeFullscreen(overlay);
    updateRotation();
    requestAnimationFrame(() => { if (fsController) fsController.chart.resize(); });
}

function destroyFsController() {
    if (!fsController) return;
    const i = graphState.controllers.indexOf(fsController);
    if (i >= 0) graphState.controllers.splice(i, 1);
    fsController.chart.destroy();
    fsController = null;
}

async function enterNativeFullscreen(overlay) {
    try {
        if (overlay.requestFullscreen) await overlay.requestFullscreen();
    } catch { /* iOS Safari saknar element-fullscreen — CSS-rotation tar över */ }
    try {
        if (screen.orientation && screen.orientation.lock) {
            await screen.orientation.lock("landscape");
        }
    } catch { /* vissa Android-browsers avvisar lock — CSS-rotation tar över */ }
    updateRotation();
}

async function closeFullscreen() {
    const overlay = document.getElementById("fs-overlay");
    destroyFsController();
    overlay.hidden = true;
    overlay.classList.remove("rotate");
    try {
        if (document.fullscreenElement) await document.exitFullscreen();
    } catch { /* ignore */ }
    try {
        if (screen.orientation && screen.orientation.unlock) screen.orientation.unlock();
    } catch { /* ignore */ }
}

// Rotera bara när overlayn är öppen OCH vyn är porträtt (dvs orientation-lock
// inte tog — typiskt iOS). Landskap (Android efter lock, eller fysiskt vridet)
// fyller viewporten utan rotation.
function updateRotation() {
    const overlay = document.getElementById("fs-overlay");
    if (overlay.hidden) return;
    const portrait = window.matchMedia("(orientation: portrait)").matches;
    overlay.classList.toggle("rotate", portrait);
    if (fsController) requestAnimationFrame(() => fsController.chart.resize());
}

function renderGraphs(graphs) {
    if (graphs.labels.length === 0) {
        setSectionStatus("kvallsgraf-status", "Ingen graf-data än.", false);
        setSectionStatus("karriargraf-status", "Ingen graf-data än.", false);
        return;
    }
    setSectionStatus("kvallsgraf-status", null, false);
    setSectionStatus("karriargraf-status", null, false);

    graphState.labels = graphs.labels;
    graphState.selectedIndex = graphs.labels.length - 1; // default: senaste kvällen

    const nightBlock = document.querySelector('.chart-block[data-graph="night"]');
    const careerBlock = document.querySelector('.chart-block[data-graph="career"]');

    const nightController = createController(
        nightBlock.querySelector("canvas"),
        nightBlock.querySelector(".chart-legend"),
        nightBlock.querySelector(".chart-selected-label"),
        "night", graphs);
    const careerController = createController(
        careerBlock.querySelector("canvas"),
        careerBlock.querySelector(".chart-legend"),
        careerBlock.querySelector(".chart-selected-label"),
        "career", graphs);
    graphState.inline = { night: nightController, career: careerController };

    nightBlock.querySelector(".chart-fullscreen")
        .addEventListener("click", () => openFullscreen("night", graphs));
    careerBlock.querySelector(".chart-fullscreen")
        .addEventListener("click", () => openFullscreen("career", graphs));
    document.getElementById("fs-close").addEventListener("click", closeFullscreen);

    window.addEventListener("orientationchange", updateRotation);
    window.addEventListener("resize", updateRotation);

    // Initiera legend/label/markör på default-kvällen.
    setSelectedIndex(graphState.selectedIndex);

    // Charts skapades i (troligen) dolda paneler → 0 storlek. Om en graf-tabb
    // redan är aktiv när datan landar, mät om den nu.
    if (navState.main === "statistik") resizeGraphPanel(navState.inner);
}

// --- tabb-navigation -------------------------------------------------------

// Två-nivå-navigation som speglar appen: huvudtabbar (Kvällar | Statistik) +
// inre Statistik-tabbar (Totalscore | Placeringar | Kvällsgraf | Karriärgraf
// | Översikt). Panelbyte sker via display:none (attributet `hidden`) — DOM och
// Chart.js-instanser lever kvar och varje panels scroll-läge bevaras av
// browsern. En graf som skapats i en dold panel har 0 storlek tills den visas,
// därför resize() vid tabb-byte.
const navState = { main: "kvallar", inner: "oversikt" };

function showPanel(name) {
    for (const panel of document.querySelectorAll(".panel")) {
        panel.hidden = panel.dataset.panel !== name;
    }
}

// Mät om rätt inline-graf när dess tabb visas (0-storlek-fix + dvh-ändringar).
function resizeGraphPanel(name) {
    const kind = name === "kvallsgraf" ? "night" : name === "karriargraf" ? "career" : null;
    if (!kind || !graphState.inline) return;
    const controller = graphState.inline[kind];
    if (controller) requestAnimationFrame(() => controller.chart.resize());
}

function selectMain(main) {
    navState.main = main;
    for (const btn of document.querySelectorAll(".main-tab")) {
        btn.classList.toggle("is-active", btn.dataset.main === main);
    }
    const innerTabs = document.getElementById("inner-tabs");
    if (main === "kvallar") {
        innerTabs.hidden = true;
        showPanel("kvallar");
    } else {
        innerTabs.hidden = false;
        showPanel(navState.inner);
        resizeGraphPanel(navState.inner);
    }
}

function selectInner(inner) {
    navState.inner = inner;
    for (const btn of document.querySelectorAll(".inner-tab")) {
        btn.classList.toggle("is-active", btn.dataset.inner === inner);
    }
    showPanel(inner);
    resizeGraphPanel(inner);
}

function initTabs() {
    for (const btn of document.querySelectorAll(".main-tab")) {
        btn.addEventListener("click", () => selectMain(btn.dataset.main));
    }
    for (const btn of document.querySelectorAll(".inner-tab")) {
        btn.addEventListener("click", () => selectInner(btn.dataset.inner));
    }
    // Startläge: Kvällar aktiv; inre default Totalscore (dold tills Statistik).
    selectMain("kvallar");
}

// --- start -----------------------------------------------------------------

async function main() {
    let sqlite3;
    try {
        sqlite3 = await sqlite3InitModule();
    } catch (err) {
        console.error(err);
        setStatus("Kunde inte ladda databas-motorn.", true);
        return;
    }

    let bytes;
    try {
        const resp = await fetch("data/db.sqlite", { cache: "no-store" });
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
        bytes = new Uint8Array(await resp.arrayBuffer());
        if (bytes.byteLength === 0) throw new Error("empty");
    } catch (err) {
        console.error(err);
        setStatus("Ingen databas uppladdad än.", true);
        return;
    }

    const db = new sqlite3.oo1.DB();
    try {
        // Ladda de hämtade byten in i en in-memory-DB via sqlite3_deserialize.
        // SQLITE_DESERIALIZE_*-flaggorna exponeras inte på capi i det här
        // bygget, så vi använder de stabila C-ABI-värdena (sqlite3.h:
        // FREEONCLOSE=1, RESIZEABLE=2) med capi-fallback ifall en framtida
        // version börjar exponera dem. FREEONCLOSE gör att SQLite frigör
        // bufferten när DB:n stängs (allokerad med sqlite3-allokatorn nedan).
        const FREEONCLOSE = sqlite3.capi.SQLITE_DESERIALIZE_FREEONCLOSE ?? 1;
        const RESIZEABLE = sqlite3.capi.SQLITE_DESERIALIZE_RESIZEABLE ?? 2;
        const p = sqlite3.wasm.allocFromTypedArray(bytes);
        const rc = sqlite3.capi.sqlite3_deserialize(
            db.pointer, "main", p, bytes.byteLength, bytes.byteLength,
            FREEONCLOSE | RESIZEABLE);
        db.checkRc(rc);

        renderNights(buildNightSummaries(db));

        // Statistik-sektionerna får ett eget try — om aggregeringen skulle
        // kasta (t.ex. korrupt data) blankas bara de, inte hela sidan.
        try {
            renderStatistics(buildHistory(db));
        } catch (statErr) {
            console.error(statErr);
            setSectionStatus("totalscore-status", "Kunde inte beräkna statistik.", true);
            setSectionStatus("oversikt-status", "Kunde inte beräkna statistik.", true);
            setSectionStatus("placeringar-status", "Kunde inte beräkna statistik.", true);
        }

        // Graf-sektionerna får också ett eget try av samma skäl.
        try {
            renderGraphs(buildGraphs(db));
        } catch (graphErr) {
            console.error(graphErr);
            setSectionStatus("kvallsgraf-status", "Kunde inte rita grafen.", true);
            setSectionStatus("karriargraf-status", "Kunde inte rita grafen.", true);
        }
    } catch (err) {
        console.error(err);
        setStatus("Kunde inte läsa databasen.", true);
    } finally {
        db.close();
    }
}

// Tabb-navigationen är ren DOM och ska funka oavsett om datan laddar — init
// den direkt, kör sen dataladdningen.
initTabs();
main();
