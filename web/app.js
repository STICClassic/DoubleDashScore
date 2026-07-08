// DoubleDashScore Web — läs-bar mobil-webb som speglar C#-appens data.
// Läser en manuellt uppladdad SQLite-fil (data/db.sqlite) i browsern via
// sqlite-wasm och renderar Kvällar-sektionen. Ingen redigering, inget skriv-API.
//
// VIKTIGT: logiken här speglar appens C#-kod och MÅSTE hållas i synk med den.
//   - Poäng/vinnare: Services/StatsCalculator.PointsFor + GameNightRepository.GetSummariesAsync
//   - Spelarfärger:  Services/PlayerColors.cs
//   - Komplett omgång: Data/RoundCompletionRule.cs (TrackCount == 16 && Results.Count == 4)
// Appen är sanning, webben mirrorar. Ändras något där, ändra här också.

import sqlite3InitModule
    from "https://cdn.jsdelivr.net/npm/@sqlite.org/sqlite-wasm@3.50.4-build1/sqlite-wasm/jswasm/sqlite3.mjs";

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
        setSectionStatus("oversikt-status", "Ingen statistik än.", false);
        setSectionStatus("placeringar-status", "Inga kvällar än.", false);
        return;
    }
    setSectionStatus("oversikt-status", null, false);
    setSectionStatus("placeringar-status", null, false);

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
            setSectionStatus("oversikt-status", "Kunde inte beräkna statistik.", true);
            setSectionStatus("placeringar-status", "Kunde inte beräkna statistik.", true);
        }
    } catch (err) {
        console.error(err);
        setStatus("Kunde inte läsa databasen.", true);
    } finally {
        db.close();
    }
}

main();
