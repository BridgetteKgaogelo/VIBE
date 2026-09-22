# VIBE — setup and run guide

Everything needed to open, build and demonstrate **VIBE** ("*Stop arguing. Start vibing.*"):
the Android client, the ASP.NET Core API it talks to, and the offline / language /
notification behaviour that the PoE is marked on.

---

## 1. What is in the repository

| Path | What it is |
| --- | --- |
| `app/` | Android client (Kotlin, Jetpack Compose, RoomDB, Retrofit, FCM) |
| `api-container/` | ASP.NET Core REST API + Azure SQL schema + xUnit tests |
| `docs/` | This guide, architecture, API reference and the decision algorithm |
| `gradle/libs.versions.toml` | Single place where every dependency version lives |

Package/namespace: `com.vibe.app` · applicationId: `com.vibe.app`.

---

## 2. Android client

### 2.1 Requirements

| Tool | Version used here | Notes |
| --- | --- | --- |
| JDK | 17 or newer (toolchain 21 defined in `gradle/gradle-daemon-jvm.properties`) | AGP 9 requires JDK 17+ |
| Android Studio | latest stable | or the Gradle wrapper from the CLI |
| Android SDK | API 36 platform, build-tools 36.0.0 | compileSdk 36.1, minSdk 24, targetSdk 36 |
| Emulator / device | Android 7.0 (API 24) or newer | Android 13+ shows the per-app language screen |

The Gradle wrapper is committed, so no local Gradle install is needed.

### 2.2 Build and run

```bash
# debug build against the bundled demo backend (no server needed)
./gradlew :app:assembleDebug

# install on a connected device/emulator
./gradlew :app:installDebug
```

The app runs **fully offline out of the box**: `USE_DEMO_BACKEND = true` in
`app/build.gradle.kts` makes the client talk to `com.vibe.app.data.remote.DemoVibeApi`,
which implements the same `VibeApi` contract as the real service and is backed by the
same RoomDB. You can create groups, add activities, run YES/NO rounds, plan, and store
memories before a single line of the API is deployed.

**The app starts empty.** Nothing is pre-loaded: no groups, no Vibe List, no plans and
no memories. The only row the bundled backend creates is the demo account
(`lerato@vibe.app` / `Vibe2026go`), so sign-in works before you register. Everything on
screen is what you created - sign in, tap *Create group*, name it, and the group appears
with an invite code you can share.

### 2.3 Pointing the app at the deployed API

```kotlin
// app/build.gradle.kts -> defaultConfig
buildConfigField("boolean", "USE_DEMO_BACKEND", "false")
buildConfigField("String", "API_BASE_URL", "\"https://vibe-api.azurewebsites.net/\"")
```

* `API_BASE_URL` must end with a trailing slash (Retrofit requirement).
* HTTPS only: `res/xml/network_security_config.xml` refuses cleartext traffic, which is
  the PoE's "HTTPS everywhere" rule. Add a debug-only `domain-config` if you must test
  against a local HTTP server.
* The bearer token is attached by `com.vibe.app.core.AuthInterceptor`.

### 2.4 Google Sign-In (optional)

1. Create an OAuth client of type *Android* in Google Cloud for package `com.vibe.app`
   and your debug/release SHA-1.
2. Create a *Web* client and put its id in the server client id field:

   ```kotlin
   buildConfigField("String", "GOOGLE_SERVER_CLIENT_ID", "\"<web-client-id>.apps.googleusercontent.com\"")
   ```

3. In `app/build.gradle.kts` declare `USE_DEMO_BACKEND = false` so the id token is sent to
   `POST /api/v1/auth/google`, which validates `aud`, `iss` and `exp` before issuing a
   VIBE session.

Email + password login works without any Google configuration, and
`DemoVibeApi.googleSignIn` returns the demo member so the button is never a dead end in a
demo.

### 2.5 Push notifications (optional)

1. Add the Firebase **google-services** plugin and drop `google-services.json` into `app/`
   (Firebase console → project settings → Android app `com.vibe.app`). The plugin is
   intentionally not applied in this repository so that a fresh clone builds without any
   private credentials.
2. `app/src/main/AndroidManifest.xml` already registers
   `com.vibe.app.notifications.VibeMessagingService` and the default channel
   (`@string/notification_channel_plans`).
3. The server sends invitations, new options, deadline reminders and winner alerts through
   FCM (`POST /api/v1/devices/register` stores the token). Every push is mirrored into the
   local notification inbox, so alerts are still readable offline.

### 2.6 Language and theme

* `res/values/strings.xml` (English), `res/values-zu/strings.xml` (isiZulu) and
  `res/values-st/strings.xml` (Sesotho) hold **the same 292 keys** — a check script is
  described in §5.
* The choice is applied through `AppCompatDelegate.setApplicationLocales` (see
  `core/LocaleManager.kt`) and declared in `res/xml/locales_config.xml`, so it also shows
  up in the Android 13+ system settings.
* Theme: dark ("midnight navy") is the default; light and "follow the system" are in
  Settings → Theme.

---

## 3. API (`api-container/`)

```bash
cd api-container
dotnet restore
dotnet run --project Vibe.Api            # http://localhost:5080, Swagger at /swagger
dotnet test                              # xUnit: decision algorithm, auth, groups, sync
```

Storage:

* local development → SQLite (`appsettings.Development.json`, file `vibe.dev.db`);
* Azure → Azure SQL (`ConnectionStrings:Vibe`), migrations in `Data/Migrations`.

Deployment sketch (Azure App Service):

```bash
dotnet publish api-container/Vibe.Api -c Release -o publish
az webapp up --name vibe-api --resource-group vibe-rg --os-type Linux \
  --runtime "DOTNETCORE:10.0" --src-path publish
```

Set `ConnectionStrings__Vibe`, `Jwt__SigningKey`, `Jwt__Issuer`, `Fcm__ServerKey`
(or a service-account json) in App Service → Configuration. Full contract:
[`docs/API.md`](API.md).

---

## 4. Demonstrating the PoE requirements

| Requirement | Where to show it |
| --- | --- |
| Register / login / logout | Onboarding → Register; Profile → Settings → Sign out |
| Google SSO | Login → *Continue with Google* (returns a verified id token to the API) |
| Password recovery | Login → *Forgot password*; Settings → Change password |
| Create / join group by invite code | Home → Create New Group / Join with code |
| Manage group + activities | Group screen: members, Vibe List, add / edit / delete |
| Owner starts a round | Group → *Start decision* (members see a clear "owner only" error) |
| One YES/NO vote per member | Vote screen: one card, equal NO/YES buttons, results hidden until the round closes |
| Eliminations, winner, participation | Round indicator, "X of Y voted", eliminated options listed after the close |
| Surprise Me | Home → Surprise Me: random eligible activity, accept or reject |
| Plan with date + time | Winner → *Plan it* → date/time pickers |
| Mark completed + memories | Plan → *Mark as done* → caption, 1–5 stars, photos |
| Offline | Turn on airplane mode: add an activity, vote, then check Settings → "N change(s) waiting to sync"; turn connectivity back on and it drains |
| Conflicts reported | Settings → *Review conflicts* → *Keep mine* / *Keep theirs* |
| Push alerts | Notifications inbox (bell) + FCM payloads |
| Language (en/zu/st), theme | Settings → Language / Theme |

---

## 5. Tests

```bash
./gradlew :app:testDebugUnitTest        # JVM: decision engine, filters, validators, Room codecs
./gradlew :app:connectedDebugAndroidTest # instrumented: offline sync queue on a real RoomDB
cd api-container && dotnet test         # xUnit: API + decision service
```

Static checks used while writing this (no Android SDK required):

```bash
# every R.string / R.plurals / R.array used in Kotlin must exist in all three locales
python3 tools/check_strings.py          # see tools/, exits non-zero when a key is missing
```

---

## 6. Troubleshooting

| Symptom | Fix |
| --- | --- |
| `Module was compiled with an incompatible version of Kotlin` | The AndroidX libraries are compiled with a newer Kotlin than the build uses: bump `kotlin` / `ksp` in `gradle/libs.versions.toml`. |
| `Cleartext HTTP traffic not permitted` | Use HTTPS or add a debug-only exception in `res/xml/network_security_config.xml`. |
| Notifications never arrive | `google-services.json` missing, or the device did not grant POST_NOTIFICATIONS on Android 13+. |
| Empty Home screen | Expected on a fresh install: VIBE ships no demo content. Tap *Create group* (or *Join group* with a friend's invite code). |
| Vote screen says "Only the group owner can start a round" | Correct behaviour: rounds are started by the owner, everyone votes. |
