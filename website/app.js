// NotchHub site. Plain JS, no build step.
// ── Config ───────────────────────────────────────────────────────────────
const REPO = "oliwiercold/NotchHub";           // GitHub "owner/repo" - change in one place
const BMC_URL = "https://www.buymeacoffee.com/gimpostin";
// ─────────────────────────────────────────────────────────────────────────

const RELEASES_URL = `https://github.com/${REPO}/releases/latest`;
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
const store = {
  get(k) { try { return localStorage.getItem(k); } catch { return null; } },
  set(k, v) { try { localStorage.setItem(k, v); } catch { } }
};
document.documentElement.classList.add("js");

// ── i18n (Polish lives in the HTML; English here) ───────────────────────
const EN = {
  skip: "Skip to content",
  "nav.features": "Features", "nav.privacy": "Privacy", "nav.download": "Download",
  "cta.download": "Download", "cta.sub": "for Windows 11", "cta.latest": "latest release", "cta.tour": "See what it does ↓",
  "hero.eyebrow": "For Windows 11 · free · open source",
  "hero.title": "A warm little <em>notch</em> that keeps everything within reach.",
  "hero.lead": "NotchHub curls up at the top of your screen like a cat on a radiator. Hover - it peeks. Click - it unfolds into controls, calendar, clipboard and your phone. Material 3 Expressive style.",
  "mock.t1": "Controls", "mock.t2": "Today", "mock.t3": "Stats", "mock.t4": "Clipboard", "mock.t5": "Phone",
  "mock.night": "Night", "mock.off": "Off", "mock.s1": "Collapsed", "mock.s2": "Peek", "mock.s3": "Expanded",
  "f.eyebrow": "What's inside", "f.title": "Everything you usually dig for in five different places.",
  "f1.h": "Controls", "f1.p": "Wi-Fi, Bluetooth, volume, brightness, night light and media - big, soft tiles instead of digging through Settings. Shortcut: <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd>.", "f1.c": "Quick tiles and sliders",
  "f2.h": "Today + meeting bar", "f2.p": "Weather, tasks and upcoming events on one screen. Before a meeting the notch shows a slim bar with a countdown and a “Join” button.", "f2.c": "Today and the meeting bar",
  "f3.h": "Stats", "f3.p": "CPU, GPU, RAM, disk and network in calm charts - plus a one-click internet speed test.", "f3.c": "Charts and speed test",
  "f4.h": "Clipboard", "f4.p": "Copy history with search and pinning. Text, links, images. <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>V</kbd> and you're there.", "f4.c": "Clipboard history",
  "f5.h": "Phone via KDE Connect", "f5.p": "Android notifications, battery, file sending and a shared clipboard. All on your local network, no cloud.", "f5.c": "Phone notifications",
  "f6.h": "Google Calendar", "f6.p": "Connect with an iCal address (read-only) or your own OAuth client (read & write). Tokens stay with you, encrypted with DPAPI.", "f6.c": "Calendar in the notch",
  "f7.h": "Mods", "f7.p": "Add your own widgets: from no-code ones (JSON + a script) to full C# modules. Examples included: Pomodoro and NBP exchange rates.", "f7.c": "Example mods",
  "f8.h": "Animations you can feel", "f8.p": "Springy transitions, a wavy progress bar, smooth shape morphing. Material 3 Expressive - just warmer.", "f8.c": "Collapsed → peek → expanded",
  "p.h": "Your data stays home", "p.p": "Everything lives locally in <code>%APPDATA%\\NotchHub</code>. No accounts, no telemetry, no ads.",
  "p.l1": "Weather: Open-Meteo; approximate location: get.geojs.io (when no city is set)", "p.l2": "Cloudflare - only when you run a speed test",
  "p.l3": "Google - only if you connect a calendar", "p.l4": "Phone - over your local network only",
  "r.h": "Requirements", "r.l1": "Windows 11 (64-bit)",
  "r.l2": "<a href=\"https://dotnet.microsoft.com/download/dotnet/10.0\" target=\"_blank\" rel=\"noopener\">.NET 10 Desktop Runtime</a> (x64)",
  "r.l3": "Optional: an Android phone with the KDE Connect app", "r.l4": "Optional: a Google account for the calendar",
  "d.h": "Make some tea and install NotchHub", "d.p": "One file, no installer. Run it and the notch appears at the top of your screen.", "d.all": "All releases on GitHub",
  "q.h": "FAQ",
  "q1.q": "Windows says “Windows protected your PC”. Now what?",
  "q1.a": "That's SmartScreen - the file isn't code-signed (certificates cost money), so Windows doesn't recognise it. Click <b>More info → Run anyway</b>. The code is open on GitHub and you can build it yourself.",
  "q2.q": "A Windows Firewall prompt appeared. Allow it?",
  "q2.a": "Yes, for <b>private networks</b>. It's KDE Connect - your phone needs to find the PC on the local network. Without it everything else works, just without the phone.",
  "q3.q": "Why do I need my own Google or Spotify client ID?",
  "q3.a": "NotchHub is open source, so it can't ship its own keys. You can connect a calendar with nothing at all - via its secret iCal address (read-only). For full sync, create a free OAuth client (type “Desktop app”) in Google Cloud Console and paste the client ID and secret into <b>Settings → Calendar</b>. Spotify works the same way: an app in the Spotify Developer Dashboard and its client ID in Settings. About 5 minutes, once.",
  "q4.q": "How do I enable autostart or uninstall?",
  "q4.a": "Turn autostart on in <b>Settings</b>. To uninstall: turn autostart off, quit NotchHub from the tray, delete <code>NotchHub.exe</code> and - to remove everything - the <code>%APPDATA%\\NotchHub</code> folder.",
  "q5.q": "Where is my data?",
  "q5.a": "In <code>%APPDATA%\\NotchHub</code>: settings, tasks, calendar, clipboard history, paired phones, mods. Paste that path into File Explorer's address bar.",
  "q6.q": "How do I add a mod?",
  "q6.a": "Drop the mod's folder into <code>%APPDATA%\\NotchHub\\mods</code> and enable it in <b>Settings → Mods</b> (there's a guide there too). No-code mods can run a local script only if you allow it.",
  "q7.q": "The app doesn't start.",
  "q7.a": "Most often the <a href=\"https://dotnet.microsoft.com/download/dotnet/10.0\" target=\"_blank\" rel=\"noopener\">.NET 10 Desktop Runtime</a> (x64) is missing. Install it and try again. Still nothing? Open an issue on GitHub.",
  "q8.q": "I just want to look around without touching my data.",
  "q8.a": "Run demo mode: <code>NotchHub.exe --demo --data %TEMP%\\nh-demo</code>. Sample data, nothing changes on your system.",
  "ft.p": "Made with coffee and patience. An independent implementation of the KDE Connect protocol.", "ft.privacy": "Privacy", "ft.terms": "Terms",
  "m.h": "Before you download…", "m.p": "NotchHub is free and will stay free. If you find it useful, you can buy me a coffee - it genuinely helps. And if not now, that's fine too.",
  "m.yes": "☕ Buy a coffee & download", "m.no": "Download without coffee", "m.remember": "Don't ask again",
  "t.start": "Download starting… thank you!", "t.page": "Opening the releases page…", "vid.soon": "Video coming soon"
};
const PL_EXTRA = { "t.start": "Pobieranie rusza… dziękuję!", "t.page": "Otwieram stronę wydań…", "vid.soon": "Wideo wkrótce" };
const PL = {};
$$("[data-i18n]").forEach(el => { PL[el.dataset.i18n] = el.innerHTML; });
Object.assign(PL, PL_EXTRA);
let lang = store.get("nh-lang") || ((navigator.language || "pl").toLowerCase().startsWith("pl") ? "pl" : "en");
const t = k => (lang === "en" ? EN : PL)[k] ?? PL[k] ?? k;

function applyLang() {
  document.documentElement.lang = lang;
  $$("[data-i18n]").forEach(el => { const v = t(el.dataset.i18n); if (v != null) el.innerHTML = v; });
  $$(".vid .ph span").forEach(s => s.textContent = "▶ " + t("vid.soon"));
  const b = $("#langBtn");
  b.textContent = lang === "en" ? "PL" : "EN";
  b.setAttribute("aria-label", lang === "en" ? "Przełącz na polski" : "Switch language to English");
  if (release?.tag) $("#dlVersion").textContent = release.tag;
}
$("#langBtn").addEventListener("click", () => { lang = lang === "en" ? "pl" : "en"; store.set("nh-lang", lang); applyLang(); });

// ── Links ────────────────────────────────────────────────────────────────
$("#ghLink").href = `https://github.com/${REPO}`;
$("#releasesLink").href = `https://github.com/${REPO}/releases`;

// ── Latest release (resolved up-front so the click can download directly) ─
let release = null;
(async () => {
  try {
    const ctl = new AbortController(); setTimeout(() => ctl.abort(), 6000);
    const r = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, { signal: ctl.signal, headers: { Accept: "application/vnd.github+json" } });
    if (!r.ok) return;
    const j = await r.json();
    const assets = j.assets || [];
    const a = assets.find(x => /\.exe$/i.test(x.name)) || assets.find(x => /\.(zip|msix|msi)$/i.test(x.name)) || assets[0];
    release = { tag: j.tag_name, url: a ? a.browser_download_url : null };
    if (release.tag) $("#dlVersion").textContent = release.tag;
  } catch { /* offline / rate-limited → fall back to the releases page */ }
})();

function toast(msg) {
  const el = $("#toast"); el.textContent = msg; el.classList.add("show");
  clearTimeout(toast.t); toast.t = setTimeout(() => el.classList.remove("show"), 3200);
}
function startDownload() {
  if (release?.url) {
    const a = document.createElement("a"); a.href = release.url; a.rel = "noopener";
    document.body.appendChild(a); a.click(); a.remove();
    toast(t("t.start"));
  } else {
    toast(t("t.page"));
    location.href = RELEASES_URL;
  }
}

// ── Download + optional coffee modal (never blocks) ──────────────────────
const modal = $("#coffeeModal");
function closeModal() {
  if ($("#mRemember").checked) store.set("nh-skip-coffee", "1");
  if (modal.open) modal.close();
}
$$(".js-download").forEach(b => b.addEventListener("click", () => {
  if (store.get("nh-skip-coffee") === "1" || typeof modal.showModal !== "function") return startDownload();
  modal.showModal();
}));
$("#mCoffee").addEventListener("click", () => {
  window.open(BMC_URL, "_blank", "noopener");   // same user gesture → not popup-blocked
  closeModal();
  setTimeout(startDownload, 150);
});
$("#mSkip").addEventListener("click", () => { closeModal(); startDownload(); });
modal.addEventListener("click", e => { if (e.target === modal) modal.close(); }); // click on backdrop

// ── Nav border on scroll ─────────────────────────────────────────────────
const nav = $(".nav");
addEventListener("scroll", () => nav.classList.toggle("scrolled", scrollY > 8), { passive: true });

// ── Scroll reveal ────────────────────────────────────────────────────────
if ("IntersectionObserver" in window && !reduced) {
  const io = new IntersectionObserver(es => es.forEach(e => {
    if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); }
  }), { rootMargin: "0px 0px -8% 0px", threshold: .08 });
  $$(".reveal").forEach((el, i) => { el.style.transitionDelay = `${(i % 3) * 60}ms`; io.observe(el); });
} else $$(".reveal").forEach(el => el.classList.add("in"));

// ── FAQ: smooth open animation, one open at a time ───────────────────────
$$(".faq details").forEach(d => d.addEventListener("toggle", () => {
  if (!d.open) return;
  d.classList.add("anim");
  $$(".faq details[open]").forEach(o => { if (o !== d) o.open = false; });
}));

// ── Video slots: drop media/<name>.mp4 (+ <name>.jpg poster) and it just works
$$(".vid").forEach(fig => {
  const src = fig.dataset.video;
  const ph = document.createElement("div"); ph.className = "ph"; ph.innerHTML = "<span></span>";
  fig.prepend(ph);
  const v = document.createElement("video");
  Object.assign(v, { muted: true, loop: true, playsInline: true, preload: "none" });
  v.setAttribute("muted", ""); v.setAttribute("playsinline", ""); v.setAttribute("aria-hidden", "true");
  v.poster = src.replace(/\.mp4$/i, ".jpg");
  v.src = src;
  v.addEventListener("loadeddata", () => fig.classList.add("ready"));
  v.addEventListener("error", () => v.remove());
  fig.prepend(v);
  const vo = new IntersectionObserver(([e]) => {
    if (!v.isConnected) return vo.disconnect();
    if (e.isIntersecting) { if (v.preload === "none") { v.preload = "auto"; v.load(); } if (!reduced) v.play().catch(() => { }); }
    else v.pause();
  }, { threshold: .25 });
  vo.observe(fig);
});

// ── Hero notch: collapsed → peek → expanded loop ─────────────────────────
const notch = $("#notch");
const states = ["collapsed", "peek", "expanded"];
const hold = { collapsed: 1800, peek: 2200, expanded: 3600 };
let idx = 0, timer = null, userPicked = false;
function setState(s) {
  notch.dataset.state = s; idx = states.indexOf(s);
  $$(".stage-chips button").forEach(b => b.setAttribute("aria-pressed", String(b.dataset.goto === s)));
}
function loop() { timer = setTimeout(() => { setState(states[(idx + 1) % 3]); loop(); }, hold[states[idx]]); }
$$(".stage-chips button").forEach(b => b.addEventListener("click", () => {
  userPicked = true; clearTimeout(timer); setState(b.dataset.goto);
  clearTimeout(b.t); b.t = setTimeout(() => { userPicked = false; if (!reduced) loop(); }, 8000);
}));
if (reduced) setState("expanded");
else {
  setState("collapsed");
  // run only while visible
  new IntersectionObserver(([e]) => {
    clearTimeout(timer);
    if (e.isIntersecting && !userPicked) loop();
  }).observe(notch);
}

applyLang();
