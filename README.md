<div align="center">

# VIBE

### *Stop arguing. Start vibing.*

A group decision app for students - drop ideas on a **Vibe List**, let everyone vote
**YES / NO** in fair rounds, and keep the night as a **memory**.

Android (Kotlin · Jetpack Compose · RoomDB) · ASP.NET Core REST API · Azure SQL · Firebase Cloud Messaging

</div>

---

## The problem it solves

Somebody says "let's do something" and forty minutes later the group is still arguing in
the group chat. VIBE replaces the argument with a game:

1. **Everyone adds ideas** to the group's Vibe List - pizza, bowling, a braai, a road trip.
2. **"Decide For Us"** runs rounds of equal YES/NO votes. Every member gets exactly one
   vote per idea, so nobody outweighs anybody.
3. Ideas that **cannot reach a majority are eliminated**; survivors go into the next round
   until one winner is left. Nothing is revealed until the round closes, so nobody can pile
   onto the winning side.
4. The winner becomes a **plan** with a date and time, and after the night it becomes a
   **memory** with photos, a caption and the group's rating.

Stuck, or just bored of the argument? **Surprise Me** proposes one eligible idea and the
group accepts or skips it.

## Screens

| | |
| --- | --- |
| **Onboarding / Login / Register** | the pitch, then email + password or Google Sign-In, with password recovery |
| **Home** | greeting, notification bell, group cards, quick actions (Can't Decide?, Surprise Me, My Activities) and the coral plus |
| **Group & Vibe List** | members, every idea with its contributor and vote count, owner-only "Start decision" |
| **Create / Join group** | pick a name and icon, share the invite code, or join with one |
| **Vote round** | one big card at a time, equal NO / YES buttons, "3 of 4 voted", no results until the round closes |
| **Winner** | confetti, the winning idea, the vote summary, "Plan it" or "Add to memories" |
| **Plan** | date and time pickers, mark as done |
| **Memories** | photo tiles with rating, caption and the group's score |
| **Profile & Settings** | stats (decisions, activities, groups), edit profile, groups, my activities, language, help, theme, password, linked accounts |

Plus the three-tab bottom bar (**Home · Memories · Profile**) and a persistent offline
banner whenever changes are waiting to sync.

## How it is built

```
app/                        Android client (Kotlin, Compose)
  domain/                   pure decision engine, models, validators  ← unit tested on the JVM
  data/local/               RoomDB cache: entities, DAOs, codecs
  data/remote/              Retrofit contract + a bundled demo backend
  data/repository/          offline-first repositories
  data/sync/                offline queue and the sync manager
  ui/                       screens, components, theme, navigation
api-container/              ASP.NET Core REST API (EF Core, JWT, PBKDF2, FCM)
docs/                       setup guide, architecture, API reference
tools/check_strings.py      static check for the three language catalogs
```

**One rule book, two implementations.** `domain/DecisionEngine.kt` and
`api-container/Domain/DecisionEngine.cs` implement the same eight rules and both are
covered by tests, so a round decided in airplane mode agrees with the server later.

**Offline is a feature, not an error state.** Every change is written to RoomDB
immediately and queued with the time the member acted. When connectivity returns the queue
drains; if somebody else changed the same row, VIBE shows both versions and asks which to
keep - it never overwrites silently.

## Get it running

```bash
# Android app against the bundled demo backend (no server needed)
./gradlew :app:assembleDebug
./gradlew :app:installDebug

# tests
./gradlew :app:testDebugUnitTest          # decision engine, filters, validators, Room codecs
./gradlew :app:connectedDebugAndroidTest  # offline queue against a real RoomDB

# API
cd api-container && dotnet run --project Vibe.Api    # Swagger at /openapi/v1.json, dev login lerato@vibe.app / Vibe2026go
cd api-container && dotnet test
```

Full instructions, including pointing the app at a deployed API and enabling Google
Sign-In and push: [`docs/SETUP.md`](docs/SETUP.md).

## Language and accessibility

English, **isiZulu** and **Sesotho** catalogs ship with identical keys and update both the
labels and the alerts; the dark midnight-navy theme is the default with a light theme and a
"follow the system" option. Icon-only controls carry content descriptions, and the YES/NO
buttons are 56 dp so a decision can be made one-handed on a taxi.

## Clickable prototype

`prototype/index.html` is the same flow and design system in a single file — open it in any
browser (or use the live preview) to create a group, add ideas, run a round of Decide For Us,
crown a winner, plan it, queue a change offline, and switch language and theme without
building the APK. Like the app, it starts **empty**: no groups, ideas, plans or memories are
pre-loaded, so the demo account is the only thing you begin with. It is a demo aid, not part
of the build.

```bash
cd prototype && npm install jsdom && node smoke.js   # asserts what each screen shows
```

## Documentation

* [`docs/SETUP.md`](docs/SETUP.md) - build, run, deploy, demonstrate
* [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) - layers, data model, algorithm, offline sync, security
* [`docs/API.md`](docs/API.md) - the REST contract, including the offline sync batch

## Team

Built for the PoE: VIBE - group decision making for students.
