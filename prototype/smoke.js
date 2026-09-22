#!/usr/bin/env node
/*
 * Walks the clickable prototype (index.html) the way a person does and asserts what each
 * screen shows - including the two things that must not regress: the app starts empty,
 * and the bottom bar behaves like tabs. Needs jsdom:
 *
 *   cd prototype && npm install jsdom && node smoke.js
 *
 * It is a demo aid only - the Kotlin app under app/ is the real deliverable.
 */
const fs = require("fs");
const path = require("path");
const { JSDOM } = require("jsdom");

const file = path.join(__dirname, "index.html");
const html = fs.readFileSync(file, "utf8");

const errors = [];
const dom = new JSDOM(html, { runScripts: "dangerously", pretendToBeVisual: true, url: "https://preview.local/" });
const { window } = dom;
window.addEventListener("error", e => errors.push("window error: " + e.message));
window.prompt = () => "Sunset picnic 🌅";
const doc = window.document;

const sleep = ms => new Promise(r => setTimeout(r, ms));
const screen = () => doc.getElementById("screen").textContent.replace(/\s+/g, " ").trim();
const bar = doc.getElementById("bar");
const tabLabels = () => [...bar.querySelectorAll(".tab")].map(b => b.textContent.trim());
const click = el => { if (!el) throw new Error("nothing to click"); el.dispatchEvent(new window.MouseEvent("click", { bubbles: true })); };
const byText = txt => [...doc.querySelectorAll("#screen button, #screen .card, #screen .pill")]
  .find(el => el.textContent.toLowerCase().includes(txt.toLowerCase()));
const setInput = (selector, value) => {
  const el = doc.querySelector(selector);
  el.value = value;
  el.dispatchEvent(new window.Event("input", { bubbles: true }));
};
let passed = 0;
const step = (label, fn) => { const ok = fn(); passed += ok ? 1 : 0; console.log(`${ok ? "✅" : "❌"} ${label}`); return ok; };
const has = (s, txt) => s.toLowerCase().includes(txt.toLowerCase());
const check = (label, fn) => { try { return step(label, fn); } catch (e) { return step(`${label} — threw ${e.message}`, () => false); } };

(async () => {
  await sleep(60);

  /* ---------------------------------------------------------------- cold start */
  check("starts on onboarding with the tagline", () => has(screen(), "Stop arguing. Start vibing."));
  check("nothing is pre-loaded (no groups, no seeded ideas)", () =>
    !has(screen(), "Friday Night Crew") && !has(screen(), "Girls Trip") && !has(screen(), "Game Squad")
    && !has(screen(), "Go for pizza") && !has(screen(), "Movie marathon"));
  check("the navigation bar is hidden before sign-in", () => bar.hidden === true);
  check("the coral quick-add button is hidden before sign-in", () => doc.getElementById("fab").hidden === true);

  click(byText("Get started")); await sleep(20);
  check("login screen", () => has(screen(), "Welcome back") && has(screen(), "Continue with Google"));

  click(byText("Log in")); await sleep(20);
  check("home is empty for a new account", () => has(screen(), "No groups yet") && has(screen(), "Create group"));
  check("navigation bar appears after sign-in", () => bar.hidden === false);
  check("bar holds exactly the three tabs from the design", () =>
    tabLabels().length === 3 && tabLabels()[0].includes("Home") && tabLabels()[1].includes("Memories")
    && tabLabels()[2].includes("Profile"));
  check("Home is the selected tab", () =>
    bar.querySelector('[data-tab="home"]').getAttribute("aria-selected") === "true");
  check("quick actions do not dead-end before a group exists", () =>
    [...doc.querySelectorAll("#screen .card")].some(c => has(c.textContent, "Can't Decide?")));

  /* ------------------------------------------------------------- create a group */
  click(byText("Create group")); await sleep(20);
  check("create-group screen asks for a name and an icon", () =>
    has(screen(), "Group name") && doc.querySelectorAll("#screen [data-icon]").length >= 6);
  check("create is disabled while the name is empty", () =>
    doc.querySelector('[data-action="create"]').disabled === true);

  setInput('[data-field="groupname"]', "Friday Night Crew");
  click(doc.querySelectorAll("#screen [data-icon]")[0]);
  click(doc.querySelector('[data-action="create"]')); await sleep(30);
  check("creating the group opens it, owned by me", () =>
    has(screen(), "Friday Night Crew") && has(screen(), "Invite code") && has(screen(), "Vibe List"));
  check("the bar is still there on a group screen (Home area selected)", () =>
    bar.hidden === false && bar.querySelector('[data-tab="home"]').getAttribute("aria-selected") === "true");
  check("the new group has an invite code to share", () => /\b[A-Z2-9]{6}\b/.test(screen()));
  check("the Vibe List starts empty", () => has(screen(), "Nothing here yet"));
  check("Start decision is disabled with fewer than two ideas", () =>
    doc.querySelector('[data-action="start"]').disabled === true);
  check("the quick-add button is available inside a group", () => doc.getElementById("fab").hidden === false);

  /* ------------------------------------------------------------- add some ideas */
  for (const idea of ["Go for pizza", "Bowling night", "Movie marathon", "Braai at the beach"]) {
    click(byId("add", doc)); await sleep(20);
  }
  check("added ideas land on the Vibe List", () =>
    doc.querySelectorAll("#screen [data-idea]").length === 4 && has(screen(), "Sunset picnic"));
  check("Start decision is enabled once there are two ideas", () =>
    doc.querySelector('[data-action="start"]').disabled === false);

  click(byText("Favourites")); await sleep(20);
  check("favourites filter is empty until something is starred", () => has(screen(), "Nothing here yet"));
  click(byText("All")); await sleep(20);
  check("All filter restores the list", () => has(screen(), "Sunset picnic"));

  /* ----------------------------------------------------------- the vote round */
  click(doc.querySelector('[data-action="start"]')); await sleep(30);
  check("round 1 opens with one large card and no results", () =>
    has(screen(), "Round 1") && has(screen(), "Idea 1 of 4") && !has(screen(), "Vote summary"));
  check("NO and YES are equal partners", () =>
    doc.querySelectorAll("#screen .nobtn").length === 1 && doc.querySelectorAll("#screen .yesbtn").length === 1);

  click(doc.querySelector("#screen .nobtn")); await sleep(20);
  check("voting advances to the next idea", () => has(screen(), "Idea 2 of 4"));
  click(doc.querySelector("#screen .yesbtn")); await sleep(20);
  click(doc.querySelector("#screen .yesbtn")); await sleep(20);
  check("advances to the last idea", () => has(screen(), "Idea 4 of 4"));
  click(doc.querySelector("#screen .yesbtn")); await sleep(30);
  check("a member who has voted sees the waiting state, not the tallies", () =>
    has(screen(), "Waiting for the rest of the group") && !has(screen(), "Vote summary"));

  await sleep(1200);      // this group has one member, so the round closes on its own
  check("round closes and only then are the results shown", () =>
    has(screen(), "Vote summary") && has(screen(), "survives"));
  check("a next step is offered after the round", () =>
    !!byText("winner") || !!byText("Round 2") || has(screen(), "Nothing survived"));

  /* ------------------------------------------------------------- the nav bar */
  check("the round is a focused flow, so the bar stayed out of the way", () => bar.hidden === true);
  click(doc.querySelector('#screen [data-go="group"]')); await sleep(20);   // back to the Vibe List
  check("leaving the round brings the bar back", () => bar.hidden === false);
  click(bar.querySelector('[data-tab="memories"]')); await sleep(20);
  check("Memories tab switches screen and marks itself selected", () =>
    has(screen(), "No memories yet")
    && bar.querySelector('[data-tab="memories"]').getAttribute("aria-selected") === "true"
    && bar.querySelector('[data-tab="home"]').getAttribute("aria-selected") === "false");
  click(bar.querySelector('[data-tab="profile"]')); await sleep(20);
  click(bar.querySelector('[data-tab="memories"]')); await sleep(20);
  click(bar.querySelector('[data-tab="profile"]')); await sleep(20);
  check("repeated tab switches leave the bar consistent (no duplicates, always one selected)",
    () => bar.querySelectorAll(".tab").length === 3
      && bar.querySelectorAll('[aria-selected="true"]').length === 1);
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  check("Home tab returns to the dashboard", () => has(screen(), "Quick Start"));

  click(byText("Friday Night Crew")); await sleep(20);
  check("a group is part of the Home area, so Home stays selected", () =>
    bar.hidden === false && bar.querySelectorAll('[aria-selected="true"]').length === 1
    && bar.querySelector('[data-tab="home"]').getAttribute("aria-selected") === "true");
  click(bar.querySelector('[data-tab="memories"]')); await sleep(20);
  check("the bar leaves Home for Memories from inside a group", () =>
    has(screen(), "No memories yet")
    && bar.querySelector('[data-tab="memories"]').getAttribute("aria-selected") === "true");

  /* --------------------------------------------------- focused flows, no bar */
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  click(byText("Friday Night Crew")); await sleep(20);
  click(doc.querySelector('[data-action="start"]')); await sleep(30);
  check("the vote round hides the bar and keeps a back arrow instead", () =>
    bar.hidden === true && !!doc.querySelector('#screen [data-go="group"]'));
  const roundScreen = screen();
  click(bar.querySelector('[data-tab="home"]')); await sleep(10);   // hidden, so inert
  check("the bar cannot be used to abandon a round", () => screen() === roundScreen);

  /* --------------------------------------------------------- language + theme */
  click(bar.querySelector('[data-tab="profile"]')); await sleep(10);  // hidden: must not navigate
  check("a hidden bar does not navigate", () => screen() === roundScreen);
  click(doc.querySelector('#screen [data-go="group"]')); await sleep(20);   // back to the group
  click(bar.querySelector('[data-tab="profile"]')); await sleep(20);
  check("with the round left behind, the bar works again", () => has(screen(), "Account"));
  click(byText("Settings")); await sleep(20);
  check("settings is a focused flow, so the bar steps aside", () => bar.hidden === true);
  click(byText("isiZulu")); await sleep(20);
  check("isiZulu switches the UI including the tab labels", () =>
    tabLabels()[0].includes("Ikhaya") && has(screen(), "Izilungiselelo"));
  click(byText("Sesotho")); await sleep(20);
  check("Sesotho switches the UI", () => tabLabels()[0].includes("Lehae"));
  click(byText("English")); await sleep(20);
  click(byText("Light")); await sleep(20);
  check("light theme applies to the whole phone", () =>
    doc.getElementById("phone").dataset.theme === "light");
  click(byText("Dark")); await sleep(20);
  check("dark theme restores", () => doc.getElementById("phone").dataset.theme === "dark");

  /* ------------------------------------------------------------ offline queue */
  click(doc.querySelector('#screen [data-go="profile"]')); await sleep(20);   // out of Settings
  check("the bar is back once the focused flow is left", () => bar.hidden === false);
  click(doc.getElementById("offlineTag")); await sleep(20);
  check("airplane mode shows with the pending count", () =>
    !doc.getElementById("offlineTag").classList.contains("hidden"));
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  click(byText("Friday Night Crew")); await sleep(20);
  click(byId("add", doc)); await sleep(30);
  check("an idea added offline is queued, not lost", () =>
    has(screen(), "waiting to sync") && has(screen(), "Sunset picnic"));
  click(doc.querySelector('#screen [data-action="reconnect"]')); await sleep(40);
  check("reconnecting drains the queue and says so", () =>
    has(screen(), "synced") && doc.getElementById("offlineTag").classList.contains("hidden"));

  /* ------------------------------------------------------------- join a group */
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  click(byText("Join with a code")); await sleep(20);
  check("join screen explains the six-character code", () => has(screen(), "six-character"));
  setInput('[data-field="joincode"]', "ZZZZZZ");
  click(doc.querySelector('[data-action="join"]')); await sleep(20);
  check("a wrong code gets a clear error and a next action", () =>
    has(screen(), "No group matches that code yet"));
  setInput('[data-field="joincode"]', "ABC");
  click(doc.querySelector('[data-action="join"]')); await sleep(20);
  check("a malformed code is rejected before any request", () => has(screen(), "six characters"));

  /* ------------------------------------------------- surprise, plan, memories */
  click(doc.querySelector('#screen [data-go="home"]')); await sleep(20);
  click(byText("Surprise Me")); await sleep(30);
  check("Surprise Me offers one idea with accept or skip", () =>
    has(screen(), "Lock it in") && has(screen(), "Skip"));
  click(byText("Skip")); await sleep(20);
  check("Skip offers another idea", () => has(screen(), "Lock it in"));
  click(byText("Lock it in")); await sleep(30);
  check("plan screen has a date and a time", () =>
    doc.querySelectorAll('#screen input[type="date"], #screen input[type="time"]').length === 2);
  click(byText("Plan it")); await sleep(30);
  check("saving a plan returns home", () => has(screen(), "Quick Start"));

  click(bar.querySelector('[data-tab="memories"]')); await sleep(20);
  check("Memories is still empty until something is completed", () => has(screen(), "No memories yet"));
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  click(byText("Friday Night Crew")); await sleep(20);
  click(doc.querySelector('[data-action="start"]')); await sleep(40);
  // YES on the first idea and NO on the rest, so exactly one idea survives and the
  // round can crown a winner instead of asking for another round.
  if (doc.querySelector("#screen .yesbtn")) { click(doc.querySelector("#screen .yesbtn")); await sleep(40); }
  for (let i = 0; i < 8 && doc.querySelector("#screen .nobtn"); i++) {
    click(doc.querySelector("#screen .nobtn")); await sleep(40);
  }
  await sleep(1200);
  check("a round with one survivor offers the winner", () => !!byText("winner"));
  click(byText("winner")); await sleep(30);
  check("the winner screen offers Plan it and Add to memories", () =>
    has(screen(), "Winner") && has(screen(), "Plan it"));
  click(byText("Plan it")); await sleep(30);
  check("planning the winner asks for a date and time", () => has(screen(), "Pick a date and time"));
  click(byText("Mark as done")); await sleep(40);
  check("completing a plan stores a memory", () => has(screen(), "just now") && has(screen(), "photos"));
  click(byText("Sunset picnic")); await sleep(20);
  check("memory detail shows the caption and five editable stars", () =>
    doc.querySelectorAll("#screen .stars span").length === 5);
  click(doc.querySelectorAll("#screen .stars span")[2]); await sleep(20);
  check("the rating can be changed", () => has(screen(), "3/5"));

  /* ------------------------------------------------------------- notifications */
  click(doc.querySelector('#screen [data-go="notifications"]')); await sleep(20);
  check("the inbox holds the alert the app raised", () => has(screen(), "is done"));
  click(byText("Mark all as read")); await sleep(20);

  /* ------------------------------------------------------------------ sign out */
  click(bar.querySelector('[data-tab="profile"]')); await sleep(20);
  check("profile counts only what exists", () => has(screen(), "Lerato") && has(screen(), "1"));
  click(byText("Settings")); await sleep(20);
  click(byText("Sign out")); await sleep(20);
  check("signing out returns to login and hides the bar", () =>
    has(screen(), "Welcome back") && bar.hidden === true);

  check("no JavaScript errors during the walkthrough", () => errors.length === 0);
  if (errors.length) console.log(errors.join("\n"));
  console.log(`\n${passed} checks passed${errors.length ? ", with errors above" : ""}`);
  process.exit(errors.length ? 1 : 0);
})();

/** The centred coral plus is the app's quick-add control. */
function byId(action, doc) {
  const el = doc.querySelector(`#fab[data-action="${action}"]`);
  if (el) return el;
  return [...doc.querySelectorAll(`#screen [data-action="${action}"]`)][0];
}
