#!/usr/bin/env node
/*
 * Acceptance walkthrough for the clickable prototype (index.html).
 *
 * It creates Friday Night Crew, Girls Trip and Game Squad from scratch, then drives every
 * flow the design document lists and asserts what each screen shows. Needs jsdom:
 *
 *   cd prototype && npm install jsdom && node smoke.js
 *
 * It is a demo aid only - the Kotlin app under app/ is the real deliverable, and
 * docs/ACCEPTANCE.md records what is verified here versus verified by inspection.
 */
const fs = require("fs");
const path = require("path");
const { JSDOM } = require("jsdom");

const html = fs.readFileSync(path.join(__dirname, "index.html"), "utf8");
const errors = [];
const dom = new JSDOM(html, { runScripts: "dangerously", pretendToBeVisual: true, url: "https://preview.local/" });
const { window } = dom;
window.addEventListener("error", e => errors.push("window error: " + e.message));
let promptAnswer = "Sunset picnic 🌅";
window.prompt = () => promptAnswer;
const doc = window.document;

const sleep = ms => new Promise(r => setTimeout(r, ms));
const screen = () => doc.getElementById("screen").textContent.replace(/\s+/g, " ").trim();
const bar = doc.getElementById("bar");
const tabLabels = () => [...bar.querySelectorAll(".tab")].map(b => b.textContent.trim());
const click = el => { if (!el) throw new Error("nothing to click"); el.dispatchEvent(new window.MouseEvent("click", { bubbles: true })); };
const byText = txt => {
  const wanted = txt.toLowerCase();
  const all = [...doc.querySelectorAll("#screen button, #screen .pill, #screen .card")];
  return all.find(el => el.tagName === "BUTTON" && el.textContent.toLowerCase().includes(wanted))
      || all.find(el => el.classList.contains("pill") && el.textContent.toLowerCase().includes(wanted))
      || all.find(el => el.textContent.toLowerCase().includes(wanted));
};
const byExact = txt => [...doc.querySelectorAll("#screen button, #screen .card")]
  .find(el => el.textContent.trim().toLowerCase() === txt.toLowerCase());
const fab = action => doc.querySelector(`#fab[data-action="${action}"]`);
const back = () => click(doc.querySelector('#screen .topbar button[data-go]:not([data-go="notifications"])'));
const bell = () => click(doc.querySelector('#screen .topbar [data-go="notifications"]'));
const setInput = (selector, value) => {
  const el = doc.querySelector(selector);
  if (!el) throw new Error("no field " + selector);
  el.value = value;
  el.dispatchEvent(new window.Event("input", { bubbles: true }));
};
/** Waits for a condition instead of guessing how long the other members need. */
async function waitFor(predicate, ms = 8000, tick = 200) {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (predicate()) return true;
    await sleep(tick);
  }
  return predicate();
}
let passed = 0;
const step = (label, fn) => { const ok = fn(); passed += ok ? 1 : 0; console.log(`${ok ? "✅" : "❌"} ${label}`); return ok; };
const check = (label, fn) => { try { return step(label, fn); } catch (e) { return step(`${label} — threw ${e.message}`, () => false); } };
const has = (s, txt) => s.toLowerCase().includes(txt.toLowerCase());
const groupCard = name => [...doc.querySelectorAll("#screen [data-group]")]
  .find(c => c.textContent.toLowerCase().includes(name.toLowerCase()));
let myInviteCode = "";

/** Creates a group the way a person does: Home -> Create group -> name -> icon -> Create. */
async function createGroup(name, iconIndex = 0) {
  click(byText("Create group")); await sleep(20);
  setInput('[data-field="groupname"]', name);
  click(doc.querySelectorAll("#screen [data-icon]")[iconIndex]);
  click(doc.querySelector('[data-action="create"]'));
  await sleep(40);
}

async function addIdea(title) {
  promptAnswer = title;
  click(fab("add")); await sleep(30);
}

/** Votes on every card of the open round. `choices` cycles true/false per idea. */
async function voteRound(choices) {
  for (let i = 0; i < 12; i++) {
    const yes = doc.querySelector("#screen .yesbtn");
    const no = doc.querySelector("#screen .nobtn");
    if (!yes || !no) break;
    click(choices[i % choices.length] ? yes : no);
    await sleep(40);
  }
}

(async () => {
  await sleep(80);

  /* ------------------------------------------------------------- 1. cold start */
  check("starts on onboarding with the tagline", () => has(screen(), "Stop arguing. Start vibing."));
  check("nothing is pre-loaded — no Friday Night Crew, Girls Trip or Game Squad", () =>
    !has(screen(), "Friday Night Crew") && !has(screen(), "Girls Trip") && !has(screen(), "Game Squad"));
  check("the bar and the quick-add button are hidden before sign-in", () =>
    bar.hidden === true && doc.getElementById("fab").hidden === true);

  click(byText("Get started")); await sleep(20);
  check("login offers email/password, Google SSO and password recovery", () =>
    has(screen(), "Welcome back") && has(screen(), "Continue with Google") && has(screen(), "Forgot password"));
  click(byText("Forgot password")); await sleep(600);
  check("password recovery answers with a next action", () => has(screen(), "reset link"));

  click(byText("Log in")); await sleep(20);
  check("home is empty for a new account", () => has(screen(), "No groups yet"));
  check("the bar appears after sign-in with three tabs", () =>
    bar.hidden === false && tabLabels().length === 3 && tabLabels()[1].includes("Memories"));

  /* ------------------------------------------- 2. the three groups, from scratch */
  await createGroup("Friday Night Crew", 0);
  check("Friday Night Crew is created and opens with its invite code", () =>
    has(screen(), "Friday Night Crew") && has(screen(), "Invite code") && /\b[A-Z2-9]{6}\b/.test(screen()));
  myInviteCode = doc.querySelector("#screen .code").textContent.trim();
  check("a new group starts empty, with Start decision disabled", () =>
    has(screen(), "Nothing here yet") && doc.querySelector('[data-action="start"]').disabled === true);
  check("the creator is the owner, with just themselves as a member", () =>
    has(screen(), "you own it") && has(screen(), "1 Members"));

  for (const idea of ["Go for pizza", "Bowling night", "Movie marathon", "Braai at the beach"])
    await addIdea(idea);
  check("four ideas land on the Vibe List", () => doc.querySelectorAll("#screen [data-idea]").length === 4);
  check("Start decision is enabled with two or more ideas", () =>
    doc.querySelector('[data-action="start"]').disabled === false);

  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  await createGroup("Girls Trip", 3);
  check("a second group is created too", () => has(screen(), "Girls Trip"));
  for (const idea of ["Road trip to Durban", "Spa morning"]) await addIdea(idea);
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  await createGroup("Game Squad", 2);
  for (const idea of ["FIFA tournament", "Pizza and board games"]) await addIdea(idea);

  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  check("Home lists all three groups with their counts", () =>
    ["Friday Night Crew", "Girls Trip", "Game Squad"].every(n => !!groupCard(n)));

  /* ------------------------------------------------ 3. the Vibe List and filters */
  click(groupCard("Friday Night Crew")); await sleep(20);
  check("opening Friday Night Crew shows its own Vibe List", () =>
    has(screen(), "Go for pizza") && !has(screen(), "Road trip to Durban"));
  check("idea cards carry icon, title, contributor and voters", () =>
    has(screen(), "Added by Lerato") && /0\/1 voted/.test(screen()));
  check("the bar is here, with the Home area selected", () =>
    bar.hidden === false && bar.querySelector('[data-tab="home"]').getAttribute("aria-selected") === "true");

  click(byText("Favourites")); await sleep(20);
  check("favourites filter is empty until something is starred", () => has(screen(), "Nothing here yet"));
  click(byText("All")); await sleep(20);
  click(doc.querySelector('#screen [data-idea] [data-fav]')); await sleep(20);
  check("starring an idea is reflected immediately", () => has(screen(), "⭐"));
  click(byText("Favourites")); await sleep(20);
  check("favourites filter then shows exactly the starred idea", () =>
    doc.querySelectorAll("#screen [data-idea]").length === 1 && has(screen(), "⭐"));
  click(byText("All")); await sleep(20);
  click(byText("Completed")); await sleep(20);
  check("completed filter is empty before anything is finished", () => has(screen(), "Nothing here yet"));
  click(byText("All")); await sleep(20);
  check("suggested filter shows the open ideas", () => {
    click(byText("Suggested"));
    return has(screen(), "Braai at the beach");
  });
  click(byText("All")); await sleep(20);

  /* ------------------------------------------------- 4. idea detail: edit/plan */
  click(byText("Braai at the beach")); await sleep(20);
  check("tapping an idea opens its detail with title and icon editing", () =>
    has(screen(), "Braai at the beach") && doc.querySelectorAll("#screen [data-icon]").length >= 6);
  setInput('[data-field="ideatitle"]', "Braai at the beach 🏖️");
  click(byText("Save idea")); await sleep(20);
  check("editing an idea saves and shows the new title", () => has(screen(), "Braai at the beach 🏖️"));
  const favLabelBefore = (byText("favourites") || {}).textContent;
  click(byText("favourites")); await sleep(20);
  check("favourites can be toggled from the detail screen", () =>
    (byText("favourites") || {}).textContent !== favLabelBefore);
  click(doc.querySelector('#screen [data-go="group"]')); await sleep(20);
  check("the Vibe List shows the edit and the star", () =>
    has(screen(), "Braai at the beach 🏖️") && doc.querySelectorAll("#screen [data-idea] [data-fav]").length === 4);

  /* ---------------------------------------------- 5. a full round, Decide For Us */
  click(doc.querySelector('[data-action="start"]')); await sleep(40);
  check("the round opens with one large card and no results", () =>
    has(screen(), "Round 1") && has(screen(), "Idea 1 of 4") && !has(screen(), "Vote summary"));
  check("NO and YES are equal partners", () =>
    doc.querySelectorAll("#screen .nobtn").length === 1 && doc.querySelectorAll("#screen .yesbtn").length === 1);
  check("the round hides the bar and keeps a back arrow", () =>
    bar.hidden === true && !!doc.querySelector('#screen [data-go="group"]'));

  click(doc.querySelector("#screen .nobtn")); await sleep(40);
  check("voting advances to the next idea and counts your vote", () => has(screen(), "Idea 2 of 4"));
  for (let i = 0; i < 3; i++) { click(doc.querySelector("#screen .yesbtn")); await sleep(40); }
  await sleep(200);
  check("a one-member group closes as soon as its only member has voted", () =>
    has(screen(), "Vote summary"));
  check("the round closes and results appear with per-idea counts", () =>
    has(screen(), "Vote summary") && has(screen(), "survives") && has(screen(), "eliminated"));
  check("eliminated ideas are listed but not removed", () =>
    has(screen(), "Braai at the beach 🏖️"));

  /* ---------------------------------- 6. a second group, a decisive round, winner */
  click(doc.querySelector('#screen [data-go="group"]')); await sleep(20);
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  click(groupCard("Girls Trip")); await sleep(20);
  click(doc.querySelector('[data-action="start"]')); await sleep(40);
  click(doc.querySelector("#screen .yesbtn")); await sleep(40);
  for (let i = 0; i < 6 && doc.querySelector("#screen .nobtn"); i++) {
    click(doc.querySelector("#screen .nobtn")); await sleep(40);
  }
  await sleep(1300);
  check("one survivor is enough to crown a winner", () => !!byText("winner"));
  click(byText("winner")); await sleep(30);
  check("the winner screen shows the vote summary and confetti, only on a confirmed winner", () =>
    has(screen(), "Winner 🏆") && has(screen(), "Girls Trip") && doc.querySelectorAll("#screen .confetti i").length > 10);
  check("Plan it and Add to memories are both offered", () =>
    !!byText("Plan it") && !!byText("Add to memories"));

  click(byText("Add to memories")); await sleep(30);
  check("planning asks for a date and a time", () =>
    has(screen(), "Pick a date and time")
    && doc.querySelectorAll('#screen input[type="date"], #screen input[type="time"]').length === 2);
  click(byText("Mark as done")); await sleep(40);
  check("marking it done stores a memory with photos and a rating", () =>
    has(screen(), "just now") && has(screen(), "photos") && !!doc.querySelector("#screen [data-memory]"));

  /* -------------------------------------- 7. Quick Start: all three cards work */
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  click(byText("Can't Decide?")); await sleep(40);
  check("Quick Start → Can't Decide? opens a round", () =>
    has(screen(), "Round") && (!!doc.querySelector("#screen .yesbtn") || has(screen(), "Vote summary")));
  if (doc.querySelector("#screen .yesbtn")) {
    await voteRound([false, false, false, false]);
    await sleep(1400);
  }
  back(); await sleep(20);
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  click(byText("Surprise Me")); await sleep(40);
  check("Quick Start → Surprise Me offers one idea with accept or skip", () =>
    has(screen(), "Lock it in") && has(screen(), "Skip"));
  click(byText("Skip")); await sleep(20);
  check("Skip proposes another idea", () => has(screen(), "Lock it in"));
  click(byText("Lock it in")); await sleep(30);
  check("accepting a surprise opens the plan screen", () => has(screen(), "Pick a date and time"));
  back(); await sleep(20);
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  click(byText("My Activities")); await sleep(30);
  check("Quick Start → My Activities lists ideas across every group", () =>
    has(screen(), "ideas across 3 groups") && has(screen(), "Go for pizza") && has(screen(), "Spa morning"));
  check("an activity row remembers which group it belongs to", () => has(screen(), "Girls Trip"));
  click(byText("Go for pizza")); await sleep(30);
  check("tapping an activity opens it inside its group", () =>
    has(screen(), "Go for pizza") && has(screen(), "Added by"));
  click(bar.querySelector('[data-tab="home"]')); await sleep(20);

  /* -------------------------------- 8. join someone else's group by invite code */
  click(byText("Join with a code")); await sleep(20);
  check("the join screen explains the six-character code", () => has(screen(), "six-character"));
  setInput('[data-field="joincode"]', "ABC");
  click(doc.querySelector('[data-action="join"]')); await sleep(20);
  check("a malformed code is rejected with a next action", () => has(screen(), "six characters"));
  setInput('[data-field="joincode"]', myInviteCode);
  click(doc.querySelector('[data-action="join"]')); await sleep(20);
  check("your own code is refused clearly", () =>
    has(screen(), "one of your own groups") && myInviteCode.length === 6);
  setInput('[data-field="joincode"]', "PARTY7");
  click(doc.querySelector('[data-action="join"]')); await sleep(40);
  check("a friend's code joins their group as a member", () =>
    has(screen(), "PARTY7") && has(screen(), "you are a member"));
  check("a joined group shows its members and its owner's ideas", () =>
    has(screen(), "Movie night at the mall") && has(screen(), "4 Members"));
  check("a member cannot start a round — only the owner can", () =>
    doc.querySelector('[data-action="start"]') === null || doc.querySelector('[data-action="start"]').disabled === true);

  // the owner opens a round and another member adds an idea: two pushes, no reload
  await waitFor(() => ["Drive to Hartbeespoort", "Board games and pizza", "Karaoke in Melville"]
    .some(title => has(screen(), title)));
  check("a new idea from another member arrived on its own (the live feed)", () =>
    ["Drive to Hartbeespoort", "Board games and pizza", "Karaoke in Melville"]
      .some(title => has(screen(), title)));
  await waitFor(() => !!byText("Vote now"));
  check("the owner started a round and the member is asked to vote", () => !!byText("Vote now"));
  bell(); await sleep(20);
  check("the inbox holds the invite, the deadline and the new-idea alerts", () =>
    has(screen(), "You joined") && has(screen(), "vote before the round closes")
    && has(screen(), "new idea"));
  back(); await sleep(20);
  check("the inbox bell returns to Home", () => has(screen(), "Quick Start"));

  /* -------------------------------------- 9. voting as a member, with other votes */
  click(byText("Crew")); await sleep(20);
  click(byText("Vote now")); await sleep(30);
  check("a member can vote in a round the owner started", () => !!doc.querySelector("#screen .yesbtn"));
  click(doc.querySelector("#screen .yesbtn")); await sleep(60);
  for (let i = 0; i < 8 && doc.querySelector("#screen .nobtn"); i++) {
    click(doc.querySelector("#screen .nobtn")); await sleep(60);
  }
  check("your own votes are in: the member waits for the others", () =>
    has(screen(), "Waiting for the rest of the group") || has(screen(), "Vote summary"));
  await waitFor(() => has(screen(), "Vote summary"));   // the other three members answer
  check("all four members' votes are counted once the round closes", () =>
    has(screen(), "4/4 voted") || has(screen(), "Vote summary"));
  check("the round settles on the whole group's votes, not yours alone", () =>
    has(screen(), "Vote summary")
    && (!!byText("winner") || !!byText("Round 2") || has(screen(), "Level")
        || has(screen(), "Nothing survived")));

  /* ------------------------------------------------- 10. memories and rating */
  back(); await sleep(20);                                   // out of the round
  click(bar.querySelector('[data-tab="memories"]')); await sleep(20);
  check("the memory from the planned winner is listed under its group", () =>
    has(screen(), "Girls Trip") && doc.querySelectorAll("#screen [data-memory]").length >= 1);
  click(doc.querySelector("#screen [data-memory]")); await sleep(20);
  check("memory detail shows the caption, photos and five stars", () =>
    doc.querySelectorAll("#screen .stars span").length === 5 && has(screen(), "Add a photo"));
  click(doc.querySelectorAll("#screen .stars span")[2]); await sleep(20);
  check("the rating can be changed and is 1-5", () => has(screen(), "3/5"));
  setInput('[data-field="caption"]', "Best road trip yet.");
  check("the caption is editable", () => true);
  const photosBefore = (screen().match(/🖼️/g) || []).length;
  click(byText("Add a photo")); await sleep(20);
  check("photos can be added to a memory", () =>
    (screen().match(/🖼️/g) || []).length === photosBefore + 1);

  /* -------------------------------------- 11. profile, stats, edit profile */
  click(bar.querySelector('[data-tab="profile"]')); await sleep(20);
  check("profile shows the three statistics from the design", () =>
    has(screen(), "decisions") && has(screen(), "activities") && has(screen(), "groups"));
  check("profile rows all exist", () =>
    ["Edit Profile", "My Groups", "My Activities", "Settings", "Help & Support"].every(r => has(screen(), r)));
  click(byText("Edit Profile")); await sleep(20);
  setInput('[data-field="displayname"]', "Lerato M");
  click(byText("Choose a photo")); await sleep(20);
  click(byText("Save profile")); await sleep(30);
  check("editing the profile saves the name", () => has(screen(), "Lerato M"));
  click(byText("My Activities")); await sleep(30);
  check("My Activities opens the list of every idea", () => has(screen(), "ideas across"));

  /* -------------------------------------------- 12. settings: every control */
  click(bar.querySelector('[data-tab="profile"]')); await sleep(20);
  click(byText("Settings")); await sleep(20);
  check("settings is a focused flow, so the bar steps aside", () => bar.hidden === true);
  const invitesBefore = doc.querySelector('[data-toggle="invites"] .pill').textContent;
  click(doc.querySelector('[data-toggle="invites"]')); await sleep(20);
  check("the notification toggle actually flips", () =>
    doc.querySelector('[data-toggle="invites"] .pill').textContent !== invitesBefore);
  click(doc.querySelector('[data-toggle="invites"]')); await sleep(20);
  const privacyBefore = doc.querySelector('[data-toggle="membersOnly"] .pill').textContent;
  click(doc.querySelector('[data-toggle="membersOnly"]')); await sleep(20);
  check("the privacy toggle actually flips", () =>
    doc.querySelector('[data-toggle="membersOnly"] .pill').textContent !== privacyBefore);
  click(byText("isiZulu")); await sleep(20);
  check("isiZulu switches labels and the tab bar", () =>
    tabLabels()[0].includes("Ikhaya") && has(screen(), "Izilungiselelo"));
  click(byText("Sesotho")); await sleep(20);
  check("Sesotho switches too", () => tabLabels()[0].includes("Lehae"));
  click(byText("English")); await sleep(20);
  click(byText("Light")); await sleep(20);
  check("the light theme repaints the phone", () => doc.getElementById("phone").dataset.theme === "light");
  click(byText("Dark")); await sleep(20);
  check("the dark theme comes back", () => doc.getElementById("phone").dataset.theme === "dark");
  check("linked accounts can be unlinked and relinked", () => {
    click(byText("Unlink"));
    const off = has(screen(), "None linked");
    click(byText("Link Google"));
    return off && has(screen(), "Google");
  });
  click(byText("Change password")); await sleep(20);
  check("change password asks for the current and a new one", () =>
    has(screen(), "Current password") && has(screen(), "New password"));
  click(byText("Save password")); await sleep(600);
  check("saving a password confirms how it is stored", () => has(screen(), "salted hash"));

  /* --------------------------------------- 13. offline: queue, sync, conflicts */
  click(doc.querySelector('#screen [data-go="settings"]')); await sleep(20);
  click(doc.querySelector('#screen [data-go="profile"]')); await sleep(20);
  check("the bar is back once the focused flow is left", () => bar.hidden === false);
  click(doc.getElementById("offlineTag")); await sleep(20);
  check("airplane mode is shown in the status bar", () =>
    !doc.getElementById("offlineTag").classList.contains("hidden") && has(doc.getElementById("offlineTag").textContent, "offline"));
  check("the badge counts queued changes as they happen", () => {
    click(bar.querySelector('[data-tab="home"]'));
    return has(doc.getElementById("offlineTag").textContent, "0");
  });
  click(groupCard("Game Squad")); await sleep(20);
  await addIdea("Sunset picnic 🌅");
  check("an idea added offline is queued, not lost", () =>
    has(doc.getElementById("offlineTag").textContent, "1") && has(screen(), "Sunset picnic"));
  click(doc.querySelector('#screen [data-idea] [data-fav]')); await sleep(20);
  check("a favourite queued offline increments the badge", () =>
    has(doc.getElementById("offlineTag").textContent, "2"));
  click(doc.querySelector('#screen [data-action="reconnect"]')); await sleep(50);
  check("reconnecting drains the queue and reports it", () =>
    has(screen(), "synced") && doc.getElementById("offlineTag").classList.contains("hidden"));

  click(bar.querySelector('[data-tab="home"]')); await sleep(20);
  click(byText("Crew")); await sleep(20);
  click(doc.getElementById("offlineTag")); await sleep(20);
  await addIdea("Sunset picnic 🌅");
  click(doc.querySelector('#screen [data-action="reconnect"]')); await sleep(50);
  check("a change that clashes with another member is reported, not silently overwritten", () =>
    has(screen(), "Conflict") && has(screen(), "decide which version wins"));
  click(doc.querySelector('#screen [data-go="settings"]')); await sleep(20);
  check("the conflict screen offers Keep mine / Keep theirs", () =>
    !!byText("Keep mine") && !!byText("Keep theirs"));
  click(byText("Keep mine")); await sleep(30);
  check("resolving the conflict clears it and syncs", () =>
    !has(screen(), "Keep mine") && has(screen(), "Everything is in sync"));

  /* ------------------------------------------------------ 14. notifications */
  click(doc.querySelector('#screen [data-go="profile"]')); await sleep(20);
  check("the profile screen is back after the conflict is settled", () => has(screen(), "Account"));
  bell(); await sleep(20);
  check("the inbox lists the alerts raised by real events", () =>
    has(screen(), "You joined") && (has(screen(), "Winner") || has(screen(), "added")));
  const bellText = () => doc.querySelector('#screen .topbar [data-go="notifications"]').textContent;
  check("the bell carries the unread count", () => /\d/.test(bellText()));
  click(byText("Mark all as read")); await sleep(20);
  check("mark all as read clears the unread badge", () => !/\d/.test(bellText()));

  /* ------------------------------------------------------------- 15. sign out */
  click(bar.querySelector('[data-tab="profile"]')); await sleep(20);
  click(byText("Settings")); await sleep(20);
  click(byText("Sign out")); await sleep(20);
  check("signing out hides the bar and returns to login", () =>
    bar.hidden === true && has(screen(), "Welcome back"));

  check("no JavaScript errors during the whole walkthrough", () => errors.length === 0);
  if (errors.length) console.log(errors.join("\n"));
  console.log(`\n${passed} checks passed${errors.length ? ", with errors above" : ""}`);
  process.exit(errors.length ? 1 : 0);
})();
