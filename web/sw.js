// DoubleDashScore Web — service worker (skiva 26).
// Offline-cache med stale-while-revalidate: sidan får den cachade versionen
// omedelbart och en färsk hämtas i bakgrunden för NÄSTA laddning. Det gör att
// webben fungerar utan internet efter första besöket, och att en ny db.sqlite
// (eller ny kod) slår igenom vid nästa page-load.
//
// CACHE-VERSIONERING: bumpa `CACHE` när kod, assets eller de pinnade CDN-
// versionerna nedan ändras — activate raderar alla caches med annat namn.
// De pinnade CDN-deparna (måste matcha app.js/index.html):
//   - @sqlite.org/sqlite-wasm@3.50.4-build1
//   - chart.js@4.5.1 (+ @kurkle/color@0.3.4 som chart.js drar in)
// Uppgraderas någon av dem: uppdatera URL:erna nedan OCH bumpa CACHE.
const CACHE = "dds-cache-v1";

// Same-origin-kärna. Måste lyckas — annars fungerar inte offline.
const CORE_ASSETS = [
    "./",
    "./index.html",
    "./style.css",
    "./app.js",
    "./appicon.png",
    "./manifest.webmanifest",
    "./data/db.sqlite",
];

// Cross-origin CDN-filer (jsDelivr tillåter caching: CORS `*`, Cache-Control
// immutable, CORP cross-origin — verifierat i skiva 26). Best-effort: en miss
// får inte fälla hela install:en.
const CDN_ASSETS = [
    "https://cdn.jsdelivr.net/npm/@sqlite.org/sqlite-wasm@3.50.4-build1/sqlite-wasm/jswasm/sqlite3.mjs",
    "https://cdn.jsdelivr.net/npm/@sqlite.org/sqlite-wasm@3.50.4-build1/sqlite-wasm/jswasm/sqlite3.wasm",
    "https://cdn.jsdelivr.net/npm/chart.js@4.5.1/auto/+esm",
    "https://cdn.jsdelivr.net/npm/@kurkle/color@0.3.4/+esm",
];

self.addEventListener("install", (event) => {
    event.waitUntil((async () => {
        const cache = await caches.open(CACHE);
        await cache.addAll(CORE_ASSETS);
        // CDN best-effort — svälj enskilda fel så install inte faller.
        await Promise.allSettled(CDN_ASSETS.map((url) => cache.add(url)));
        await self.skipWaiting();
    })());
});

self.addEventListener("activate", (event) => {
    event.waitUntil((async () => {
        const names = await caches.keys();
        await Promise.all(names.filter((n) => n !== CACHE).map((n) => caches.delete(n)));
        await self.clients.claim();
    })());
});

self.addEventListener("fetch", (event) => {
    const req = event.request;
    if (req.method !== "GET") return;
    const url = new URL(req.url);
    if (url.protocol !== "http:" && url.protocol !== "https:") return;
    event.respondWith(staleWhileRevalidate(event));
});

async function staleWhileRevalidate(event) {
    const req = event.request;
    const cache = await caches.open(CACHE);
    const cached = await cache.match(req);

    // Revalidera i bakgrunden. Cacha bara lyckade svar (200) eller opaque
    // (cross-origin utan CORS — går att lagra men inte inspektera).
    const network = fetch(req).then((res) => {
        if (res && (res.status === 200 || res.type === "opaque")) {
            cache.put(req, res.clone()).catch(() => {});
        }
        return res;
    }).catch(() => null);

    if (cached) {
        // Håll SW vid liv medan bakgrunds-hämtningen slutförs.
        event.waitUntil(network.catch(() => {}));
        return cached;
    }

    const res = await network;
    if (res) return res;

    // Offline utan cache: servera app-skalet för navigeringar så sidan i alla
    // fall renderar (app.js visar sen ett vettigt "ingen databas"-meddelande).
    if (req.mode === "navigate") {
        const shell = (await cache.match("./index.html")) || (await cache.match("./"));
        if (shell) return shell;
    }
    return new Response("Offline", { status: 503, statusText: "Offline" });
}
