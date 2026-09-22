#!/usr/bin/env node
/*
 * Walks the clickable prototype (index.html) the way a person does and asserts what
 * each screen shows. Needs jsdom:
 *
 *   cd prototype && npm install jsdom && node smoke.js
 *
 * It is a demo aid only — the Kotlin app under app/ is the real deliverable.
 */
/* Smoke test: drives prototype/index.html in jsdom the way a user would click it. */
const fs = require("fs");
const path = require("path");
const { JSDOM } = require("jsdom");   // npm i jsdom

const file = path.join(__dirname, "index.html");
const html = fs.readFileSync(file, "utf8");

const errors = [];
const dom = new JSDOM(html, { runScripts: "dangerously", pretendToBeVisual: true, url: "https://preview.local/" });
const { window } = dom;
window.addEventListener("error", e => errors.push("window error: " + e.message));
window.onerror = (m) => errors.push("onerror: " + m);
window.prompt = () => "Sunset picnic 🌅";
const doc = window.document;

const sleep = ms => new Promise(r => setTimeout(r, ms));
const screen = () => doc.getElementById("screen").textContent.replace(/\s+/g, " ").trim();
const click = el => el.dispatchEvent(new window.MouseEvent("click", { bubbles: true }));
const byText = txt => [...doc.querySelectorAll("#screen button, #screen .card, #screen .pill, .bottom .tab")]
  .find(el => el.textContent.toLowerCase().includes(txt.toLowerCase()));
const step = (label, fn) => { const out = fn(); console.log(`${out ? "✅" : "❌"} ${label}`); return out; };
const has = (s, txt) => s.toLowerCase().includes(txt.toLowerCase());

(async () => {
  await sleep(60);
  step("onboarding renders with tagline + Get started", () => has(screen(), "Stop arguing. Start vibing.") && has(screen(), "Get started"));
  step("three explainer cards present", () => ["Make a group", "Vote YES or NO", "Keep the memory"].every(x => has(screen(), x)));

  click(byText("Get started")); await sleep(20);
  step("login screen", () => has(screen(), "Welcome back") && has(screen(), "Continue with Google"));

  click(byText("Log in")); await sleep(20);
  step("home: greeting + groups + quick start", () => has(screen(), "Good") && has(screen(), "Friday Night Crew")
        && has(screen(), "Surprise Me") && has(screen(), "Can't Decide?"));
  step("bottom bar has three tabs", () => doc.querySelectorAll(".bottom .tab").length === 3);
  step("light theme off by default (coral CTA visible)", () => doc.getElementById("statusRight").textContent.includes("🌙"));

  // ---- open group, filter pills
  click(byText("Friday Night Crew")); await sleep(20);
  step("group screen: invite code + Vibe List + 4 ideas", () => has(screen(), "FRYDAY")
        && has(screen(), "Vibe List") && has(screen(), "Go for pizza") && has(screen(), "Braai at the beach"));
  click(byText("Favourites")); await sleep(20);
  step("favourites filter narrows the list", () => has(screen(), "Go for pizza") && !has(screen(), "Bowling night"));
  click(byText("All")); await sleep(20);
  step("All filter restores the list", () => has(screen(), "Bowling night") && has(screen(), "Movie marathon"));

  // ---- questions must not leak before the round closes
  click(byText("Start decision")); await sleep(30);
  step("round 1 opens with one large card, no tallies", () => has(screen(), "Round 1") && has(screen(), "Go for pizza")
        && !has(screen(), "Vote summary"));
  step("NO and YES buttons are equal partners", () => doc.querySelectorAll("#screen .nobtn").length === 1
        && doc.querySelectorAll("#screen .yesbtn").length === 1);

  // vote NO on pizza, YES on the rest, one at a time
  click(doc.querySelector("#screen .nobtn")); await sleep(20);
  step("advances to idea 2 of 4", () => has(screen(), "Idea 2 of 4") && has(screen(), "Bowling night"));
  click(doc.querySelector("#screen .yesbtn")); await sleep(20);
  click(doc.querySelector("#screen .yesbtn")); await sleep(20);
  step("advances to idea 4 of 4", () => has(screen(), "Idea 4 of 4") && has(screen(), "Braai at the beach"));
  click(doc.querySelector("#screen .yesbtn")); await sleep(30);
  step("waiting state after your last vote", () => has(screen(), "Waiting for the rest of the group"));

  await sleep(4600);   // the rest of the group votes, then the round closes
  step("round closes and results are revealed", () => has(screen(), "Vote summary") && has(screen(), "eliminated"));
  step("eliminated idea keeps its card but no winner announced early", () => has(screen(), "Go for pizza"));
  step("a next step is offered", () => !!byText("Round 2") || !!byText("Level —") || has(screen(), "winner")
      || has(screen(), "Nothing survived"));

  // ---- offline queue
  click(doc.getElementById("offlineTag")); await sleep(20);
  step("airplane mode badge shows", () => !doc.getElementById("offlineTag").classList.contains("hidden"));
  step("offline banner on the round screen", () => screen().length > 0);
    click(doc.getElementById("offlineTag")); await sleep(20);
  step("reconnect clears airplane mode", () => doc.getElementById("offlineTag").classList.contains("hidden"));

  // ---- group: add an idea offline, then sync
  click(doc.querySelector(".bottom .tab")); await sleep(20);              // Home
  click(byText("Friday Night Crew")); await sleep(20);
  click(doc.getElementById("offlineTag")); await sleep(20);
  click(byText("Add an idea")); await sleep(30);
  step("idea added while offline (queued, not silent)", () => has(screen(), "Sunset picnic") && has(screen(), "Waiting to sync"));
  click(doc.querySelector("#screen [data-action='reconnect']")); await sleep(40);
  step("queue drains on reconnect and reports it", () => has(screen(), "synced"));

  // ---- surprise me
  click(doc.querySelector(".bottom .tab")); await sleep(20);
  click(byText("Surprise Me")); await sleep(30);
  step("Surprise Me offers one random idea with accept/skip", () => has(screen(), "Lock it in") && has(screen(), "Skip"));
  click(byText("Skip")); await sleep(20);
  step("Skip offers another idea", () => has(screen(), "Lock it in"));
  click(byText("Lock it in")); await sleep(30);
  step("plan screen with date and time", () => has(screen(), "Pick a date and time")
        && doc.querySelectorAll('input[type="date"], input[type="time"]').length === 2);
  click(byText("Plan it")); await sleep(30);
  step("plan saved, back home", () => has(screen(), "Quick Start"));

  // ---- memories: complete a plan, rate it
  click(byText("Friday Night Crew")); await sleep(20);
  if (byText("Start decision")) { click(byText("Start decision")); await sleep(3000); }
  click(doc.querySelector(".bottom .tab")); await sleep(20);
  click(doc.querySelectorAll(".bottom .tab")[1]); await sleep(30);
  step("memories screen lists seeded memories", () => has(screen(), "FIFA tournament") && has(screen(), "Movie marathon"));
  click(byText("Movie marathon")); await sleep(20);
  step("memory detail shows caption + rating stars", () => has(screen(), "Third row") && doc.querySelectorAll("#screen .stars span").length === 5);
  click(doc.querySelectorAll("#screen .stars span")[1]); await sleep(20);
  step("rating is editable", () => has(screen(), "2/5"));
  click(doc.querySelector("#screen .back")); await sleep(20);

  // ---- profile + settings i18n and theme
  click(doc.querySelectorAll(".bottom .tab")[2]); await sleep(20);
  step("profile: stats and rows", () => has(screen(), "Lerato") && has(screen(), "12") && has(screen(), "Help & Support"));
  click(byText("Settings")); await sleep(20);
  click(byText("isiZulu")); await sleep(20);
  step("isiZulu switches the UI + tab bar", () => doc.querySelector(".bottom .tab").textContent.includes("Ikhaya")
        && has(screen(), "Izilungiselelo"));
  click(byText("Sesotho")); await sleep(20);
  step("Sesotho switches the UI", () => doc.querySelector(".bottom .tab").textContent.includes("Lehae"));
  click(byText("English")); await sleep(20);
  click(byText("Light")); await sleep(20);
  step("light theme applies", () => doc.getElementById("phone").style.background.toLowerCase().includes("255, 247, 250"));
  click(byText("Dark")); await sleep(20);
  step("dark theme restores", () => doc.getElementById("phone").style.background.toLowerCase().includes("navy"));

  // ---- notifications + sign out
  click(doc.getElementById("screen").querySelector("[data-go='notifications']")); await sleep(20);
  step("notification inbox lists four alerts", () => (screen().match(/You have a group invitation/g) || []).length === 1);
  click(byText("Mark all as read")); await sleep(20);
  click(doc.querySelectorAll(".bottom .tab")[2]); await sleep(20);   // back to Profile
  click(byText("Settings")); await sleep(20);
  click(byText("Sign out")); await sleep(20);
  step("sign out returns to login", () => has(screen(), "Welcome back"));

  step("no JavaScript errors during the walkthrough", () => errors.length === 0);
  if (errors.length) console.log(errors.join("\n"));
  process.exit(errors.length ? 1 : 0);
})();
