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
    } catch (err) {
        console.error(err);
        setStatus("Kunde inte läsa databasen.", true);
    } finally {
        db.close();
    }
}

main();
