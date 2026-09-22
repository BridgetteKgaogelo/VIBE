# VIBE — acceptance record

What has actually been executed, what has only been read, and what is deliberately out of
scope. The design document is the specification; this file says how far each requirement has
been taken in this repository.

Two levels of evidence are used:

| Level | Meaning |
| --- | --- |
| **Executed** | Driven end to end by `prototype/smoke.js` against `prototype/index.html` in jsdom, asserting what each screen shows. 93 checks, green across repeated runs. |
| **Inspected** | Kotlin/Compose, Room, Retrofit and ASP.NET Core code written and traced by hand, with `tools/check_strings.py` and brace/paren audits. **Not compiled**: this environment has no JDK, no Android SDK and no .NET SDK, and the artifact hosts (Maven Central, dl.google.com) are unreachable. |

`./gradlew :app:testDebugUnitTest`, `./gradlew :app:connectedDebugAndroidTest` and
`cd api-container && dotnet test` are the commands that turn the "Inspected" rows into
executed ones, and they are the first thing to run on a machine with the toolchains.

---

## 1. Feature coverage

| Requirement (design document) | Prototype — executed | Android app — inspected | API — inspected |
| --- | --- | --- | --- |
| Splash/onboarding: logo, tagline, 3 explainers, coral *Get started* | ✅ starts on onboarding; the bar and the plus are hidden until sign-in | `ui/screens/AuthScreens.kt` `OnboardingScreen` | — |
| Register / login / logout, Google SSO, password recovery | ✅ login, Google button, *Forgot password* answers; sign-out hides the bar | `AuthScreens.kt`, `data/repository/AuthRepository.kt`, `identity/GoogleSignInGateway.kt`, `data/prefs/SettingsStore.kt` | `Endpoints/AuthEndpoints.cs`, `Security/PasswordHasher.cs` (PBKDF2-SHA256, per-account salt), `Security/GoogleTokenValidator.cs` |
| Home: greeting, bell with unread count, group cards, Create/Join | ✅ greeting, bell badge, group cards with owner/member line, empty state | `ui/screens/HomeScreen.kt` + `HomeViewModel` | `GroupEndpoints` list |
| Quick Start: *Can't Decide?*, *Surprise Me*, *My Activities*, centred coral plus | ✅ all three cards driven; *Can't Decide?* opens a round in a group you own, *Surprise Me* accepts/skips, *My Activities* lists ideas across groups | `HomeScreen.kt` quick-start cards; plus opens the quick-actions dialog | `DecisionService.StartAsync` (owner-only, ≥2 ideas) |
| Create group: name, icon, invite code | ✅ created from scratch three times, code minted and shown | `GroupScreens.kt` `CreateGroupScreen`, `domain/Validation.kt` invite alphabet | `DecisionEngine.GenerateInviteCode`, unique index on `Groups.InviteCode` |
| Join by invite code | ✅ malformed code, own code and a friend's code all handled; joining lands as a member | `GroupScreens.kt` `JoinGroupScreen`, `GroupRepository.joinGroup` | `GroupEndpoints` join by code, 404 on unknown code |
| Group + Vibe List, members, owner role | ✅ member list, invite code, "you own it" / "you are a member", owner-only *Start decision* | `GroupScreens.kt` `GroupDetailScreen` (`MemberAvatars`, `InviteActions`) | `GroupEndpoints.RequireMember` / owner checks |
| Vibe List filters All / Suggested / Completed / Favourites | ✅ each pill filtered the list; favourite toggling persisted | `domain/ActivityFilters.kt`, `ui/screens/ActivityScreens.kt` | `ActivityFilters` equivalents in `GroupEndpoints` |
| Idea detail: edit title/icon, favourite, delete | ✅ edited an idea, toggled the star, planned it | `ActivityScreens.kt` `ActivityDetailScreen` / `ActivityEditScreen` | activities upsert/delete + sync batch |
| Decide For Us: one card, round indicator, response count, equal NO/YES, no results early | ✅ one card at a time, "idea n of m", equal buttons, waiting state, results only after the round closes | `DecisionScreens.kt` `VoteScreen`, `domain/DecisionEngine.kt` | `Services/DecisionService.cs` |
| Eliminations of options that cannot progress | ✅ eliminated ideas marked in the summary and kept on the list | `DecisionEngine` + `DecisionRepository.buildRound` | `Domain/DecisionEngine.cs` (parity test suite) |
| Progressive rounds, tie handling | ✅ round 1 closed with survivors offered round 2; a decisive round crowned a winner | `DecisionEngine` MAX_ROUNDS = 5, `Tie` outcome | same engine, `DecisionEngineTests.cs` |
| Winner: confetti only on a confirmed winner, vote summary, *Plan it* / *Add to memories* | ✅ confetti only on the winner screen, summary with ✅/❌/⏳ | `DecisionScreens.kt` `WinnerScreen` (14-lane confetti) | `DecisionService` settles winner |
| Surprise Me: random eligible idea, accept/reject | ✅ accept opens the plan, skip proposes another | `DecisionScreens.kt` `SurpriseScreen`, `DecisionEngine.surpriseMe` | `DecisionService.SurpriseAsync` |
| Plan: date and time | ✅ both pickers present, saving returns home | `PlanMemoryScreens.kt` `PlanScreen` | plans endpoints |
| Mark completed, memories with photos, caption, 1–5 rating | ✅ completion stores a memory, rating changed, caption edited, photo added | `PlanMemoryScreens.kt` `MemoriesScreen` / `MemoryDetailScreen` | memories endpoints, rating 0..5 validated |
| Profile: photo, decisions/activities/groups stats, six rows | ✅ all three statistics and all rows; edit profile saved a new name | `ProfileScreens.kt` `ProfileScreen.kt` + `EditProfileScreen` | `users` endpoints |
| Settings: notifications, privacy, theme, password, linked accounts, language | ✅ toggles flip, theme repaints, three languages switch labels, password screen, Google link/unlink | `ProfileScreens.kt` `SettingsScreen`, `LanguageScreen`, `ChangePasswordScreen`; `data/prefs/SettingsStore.kt` | users patch, password change |
| Bottom bar: Home, Memories, Profile | ✅ browse screens keep it with the right area selected; focused flows hide it; a hidden bar is inert | `ui/components/VibeComponents.kt` `VibeBottomBar` + `onSelectTab`, wired in `VibeNavHost.kt` | — |
| Offline: local cache, pending actions with timestamps, sync on reconnect | ✅ badge counts queued changes, reconnect drains and reports | `data/local/*` (Room v1), `data/sync/SyncQueue.kt`, `data/sync/SyncManager.kt` | `POST /api/v1/sync/batch` with idempotency receipts |
| Conflicts reported, not silently overwritten | ✅ a clashing change raised a conflict with *Keep mine* / *Keep theirs* | `SyncManager.resolveKeepingLocal` / `resolveKeepingServer` | `Services/SyncService.cs` (409 + both versions) |
| Push alerts for invitations, new ideas, deadlines, winners | ✅ simulated in-app: invite accepted, a member adds an idea, a round opens, a winner | `notifications/VibeMessagingService.kt`, `NotificationRepository` | `Services/NotificationService.cs` (inbox first, then `IFcmSender`) |
| English, isiZulu, Sesotho with alert text following the choice | ✅ labels and tab bar switch in all three | `res/values`, `values-zu`, `values-st` (292 keys each), `core/LocaleManager.kt` | notification copy localised per user language |
| Security: salted one-way hashes, HTTPS, verified Google token, role checks, server-side validation | prototype only shows the flow | `identity/`, `AuthRepository`, role error paths | `Security/PasswordHasher.cs`, `JwtIssuer`, `GoogleTokenValidator`, `Validators.cs`, HTTPS redirect + HSTS outside Development |

---

## 2. Known limits of the prototype

The prototype is a demonstration aid, not the product. It deliberately fakes three things:

1. **Photos.** "Add a photo" increments a counter and draws placeholders; the app uses the
   system photo picker and stores URIs (`MemoryDetailScreen`).
2. **Other members.** They live in the same browser tab: another member's idea and an
   owner's round arrive on a timer instead of over FCM. The rules they follow are the real
   ones (owner-only rounds, one vote each, others answer only after you have).
3. **Persistence.** State is in memory for the session; the app keeps it in RoomDB and the
   API in Azure SQL. Closing the tab forgets everything, which is the point - the app starts
   empty too.

## 3. What still needs a machine with toolchains

* `./gradlew :app:assembleDebug` — Kotlin compile, Compose, Room codegen (KSP).
* `./gradlew :app:testDebugUnitTest` — decision engine, filters, validators, Room codecs.
* `./gradlew :app:connectedDebugAndroidTest` — the offline queue against a real RoomDB.
* `cd api-container && dotnet run --project Vibe.Api` and `dotnet test` — API boot and xUnit.
* `azure` deployment steps in `docs/SETUP.md` §3, and the FCM/Google credential wiring in
  §2.4–2.5.
