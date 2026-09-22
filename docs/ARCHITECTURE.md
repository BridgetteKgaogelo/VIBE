# VIBE — architecture

> *Stop arguing. Start vibing.* A group decision app for South African students:
> everybody drops ideas on a Vibe List, the app runs fair YES/NO rounds until one
> option survives, and the plan becomes a memory.

---

## 1. Layers

```
┌──────────────────────────── Android client (Kotlin, Compose) ────────────────────────────┐
│  ui/            screens, components, theme, navigation, formatters                        │
│  ui/…ViewModel  one view model per feature, StateFlow in, user intents out                │
│  domain/        models, DecisionEngine, ActivityFilters, validators  ← pure Kotlin, no    │
│                 Android, Retrofit or Room dependency → unit testable on the JVM           │
│  data/repository/  offline-first repositories: RoomDB is the truth on screen, the API     │
│                    is refreshed into it, every mutation is queued                         │
│  data/local/    RoomDB entities, DAOs, codecs, mappers                                    │
│  data/remote/   Retrofit contract (VibeApi), DTOs, mappers, DemoVibeApi                    │
│  data/sync/     SyncQueue + SyncManager (drain on reconnect, report conflicts)             │
│  core/          service locator, time provider, connectivity, auth interceptor, results   │
└───────────────────────────────────────────────────────────────────────────────────────────┘
                     ▲ HTTPS + JWT                                    ▲ FCM push
┌────────────────────┴──────────────────────────┐   ┌───────────────┴──────────────────────┐
│  ASP.NET Core REST API (api-container)        │   │  Firebase Cloud Messaging            │
│  auth · groups · activities · decisions ·     │   │  invitations, new options,           │
│  votes · plans · memories · notifications ·   │   │  deadlines, winners                  │
│  sync batch                                   │   └──────────────────────────────────────┘
└────────────────────┬──────────────────────────┘
                     ▼
             Azure SQL Database (EF Core)  ·  Azure App Service (HTTPS)
```

Why this shape:

* **One rule book, two implementations.** `DecisionEngine.kt` and
  `api-container/Services/DecisionService.cs` implement the same algorithm, and both are
  unit tested. The client can therefore decide a round offline and the server cannot
  disagree when the group reconnects.
* **Room is the screen's source of truth.** Screens observe RoomDB flows only, so a screen
  never renders "loading…" for cached data and every list works in airplane mode.
* **Nothing is lost offline.** Every mutation writes to RoomDB *and* appends a row to
  `sync_queue` with the timestamp the member acted, so ordering survives a week of no
  signal.
* **Conflicts are surfaced, never swallowed.** A 409 from the API leaves the queued row in
  `SyncState.CONFLICT` with the server's copy attached; Settings shows both versions and the
  member chooses.

---

## 2. Data model (client and server agree 1:1)

| Entity | Fields |
| --- | --- |
| `User` | id (UUID), displayName, username, email, passwordHash (server only), language, themeMode, notificationsEnabled, privacyMembersOnly, photoUri, createdAt |
| `Group` | id, name, icon (emoji), inviteCode (6 chars), ownerId, createdAt |
| `GroupMember` | groupId, userId, role (`OWNER`/`MEMBER`), joinedAt |
| `Activity` | id, groupId, title, description, icon, status (`SUGGESTED`/`ACTIVE`/`ELIMINATED`/`WINNER`/`COMPLETED`), createdBy, favourite, yesVotes, participantCount, createdAt |
| `Decision` | id, groupId, roundNumber, state (`OPEN`/`CLOSED`/`SETTLED`), deadlineEpochMillis, winnerActivityId, startedBy, startedAt |
| `Vote` | decisionId + activityId + userId (composite key), choice (YES/NO), epochMillis |
| `Plan` | id, groupId, activityId, scheduledAtEpochMillis, completed, createdBy |
| `Memory` | id, groupId, planId, activityId, completedAt, caption, rating 1–5, photoUris, createdBy |
| `Notification` | id, userId, title, body, type (`INVITE`/`NEW_ACTIVITY`/`DEADLINE`/`WINNER`/`MEMORY`), read, createdAt |
| `SyncItem` (client only) | id (`action:entity:queuedAt`), action, entityId, payloadJson, queuedAt, state (`PENDING`/`SYNCED`/`CONFLICT`/`FAILED`), attempts, lastError, serverPayloadJson |

**All timestamps are epoch milliseconds (UTC)** in RoomDB, in the JSON payloads and in the
queue. One representation means vote order, deadlines and "who changed it first" are
comparable everywhere without timezone guesswork.

RoomDB stores the two list-shaped values in text columns through `Codecs` (values separated
by the ASCII unit separator, tallies as `activityId:yes:no:pending`); `RoomCodecsTest`
round-trips them, including damaged rows from an older build.

---

## 3. The decision algorithm ("Decide For Us")

Rules, in the order the engine applies them:

1. **Entry.** Only `SUGGESTED` and `ACTIVE` activities enter a round. Completed, eliminated
   and winning options never come back into play on their own.
2. **One equal vote.** Every group member casts exactly one YES/NO vote **per activity**.
   A later vote replaces the member's earlier one, and a vote from outside the group is
   ignored — so no member can outweigh another and nobody can vote twice.
3. **Progress rule.** An option survives when `YES > NO`.
4. **Elimination.** Options that cannot progress are dropped from the next round: at the
   end of a round, every option with `YES <= NO` is `ELIMINATED`.
5. **Early close.** While a round is open, the engine keeps quiet — no winner is claimed and
   no tally is shown — but it sets `canCloseEarly` as soon as at most one option could still
   reach a majority (`YES + pending > NO`). The round can then be closed without waiting for
   the last vote, because nothing else can win any more.
6. **Rounds.** Survivors go into the next round (`ContinueRounds`, up to
   `DecisionEngine.MAX_ROUNDS = 5`).
7. **Outcomes.** `Winner` (exactly one survivor), `NoWinner` (nothing progressed — the group
   should add fresh ideas), `Tie` (the final comparison is level, so the app does **not**
   invent a winner; the group picks or runs "Surprise Me").
8. **Transparency.** Tallies are only handed to the UI once the round is closed, which is
   what makes the result believable: nobody can watch the votes pile up and pile on.

`Participation` (members, votes cast, pending members, progress) is exposed the whole time,
because response counts are safe to show.

Two conveniences sit on top of the engine: **Surprise Me** (uniform random pick among
eligible, non-rejected activities) and **invite codes** (6 characters from
`ABCDEFGHJKLMNPQRSTUVWXYZ23456789` — no I, O, 0 or 1, so a code can be read out loud).

---

## 4. Offline-first and sync

```
member acts ──► RoomDB write (optimistic) ──► sync_queue row (action, payloadJson, queuedAt)
      │                                              │
      └── UI reads RoomDB flows immediately          │  connectivity returns
                                                     ▼
                              SyncManager.syncNow() ──► POST /api/v1/sync/batch
                                                     │
              applied ◄──────────────────────────────┼──────────────► conflict
        row deleted from the queue        row kept as CONFLICT with the server copy
```

* The queue id is `action:entityId:queuedAt` (`Ids.syncClientId`), so retrying a batch can
  never apply the same change twice — important when the request succeeds but the response
  is lost.
* `AndroidConnectivityObserver` reports when a validated network is available;
  `SyncManager.start()` drains the queue at that moment and once when the app starts.
* `SyncState.CONFLICT` is a first-class state: the row stays, the server payload is stored
  next to it, and Settings → *Review conflicts* offers **Keep mine** (requeue) or
  **Keep theirs** (drop the local change and refresh). The app never overwrites silently,
  which is exactly the PoE requirement.
* Votes are queued like everything else, with the time the member voted — so a vote cast in
  a taxi still counts with its real timestamp.

---

## 5. Security and non-functional rules

| Requirement | Implementation |
| --- | --- |
| Passwords | PBKDF2-SHA256, 210 000 iterations, 16-byte random salt, 32-byte hash, `CryptographicOperations.FixedTimeEquals` (`api-container/Security/PasswordHasher.cs`) — OWASP-aligned; the API never returns or logs a password |
| Transport | HTTPS only; the client refuses cleartext (`network_security_config.xml`) |
| Google SSO | The id token is validated server-side (`aud`, `iss`, `exp`) before a VIBE session is issued |
| Authorisation | JWT bearer + role checks: only the owner starts a round or removes members (403), only members of a group read or write its activities, one vote per member per activity |
| Validation | The same rules on both sides (`Validators.kt` ↔ `Security/Validators.cs`): email shape, 8+ characters with a digit, invite code alphabet, caption length, rating 1–5 |
| Errors | Every failure maps to a `FieldError` or an `ApiErrorKind`, and the UI renders copy that names the next action |
| Accessibility | 56 dp voting buttons, text labels always paired with the activity emoji (never icon-only), content descriptions on every icon, dark and light themes with contrast-checked colours |
| Performance | Every list is a RoomDB flow with an index on `groupId`; screens render cached rows before the network answers |
| Privacy | `privacyMembersOnly` keeps a group's Vibe List to its members; backup rules and data-extraction rules are declared in the manifest |

---

## 6. Languages

* English, isiZulu and Sesotho catalogs carry the same keys (`tools/check_strings.py`
  enforces parity, including `%1$s` placeholders).
* The switch is applied with `AppCompatDelegate.setApplicationLocales` and declared in
  `res/xml/locales_config.xml`, so the OS language settings show VIBE too.
* Notification copy follows the same setting, because alerts are stored per member on the
  server with the member's language.

## 7. Testing

| Level | Where | What it protects |
| --- | --- | --- |
| Unit (JVM) | `app/src/test` | decision engine (rounds, eliminations, equality of votes, hidden results, early close), Vibe List filters, validators and invite codes, Room codecs and mappers |
| Instrumented | `app/src/androidTest` | the offline queue against a real RoomDB: timestamps, draining on reconnect, conflict reported and resolvable, keep-mine/keep-theirs |
| API | `api-container/Vibe.Tests` | decision service parity with the client, password hashing, auth and group rules, sync batch outcomes |
| Manual / usability | `docs/SETUP.md` §4 | the demonstration script for the PoE panel |
