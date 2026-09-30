# Relay — "Is this normal?"

An internal tool for Customer Success. For any account it answers one question: **is this account's recent activity normal?** You get a verdict (Unusual / Worth a look / Normal), the reasons in plain English, and a per-location breakdown.

- **API:** ASP.NET Core on .NET 10 with EF Core + SQLite. The statistics engine is a pure library with no I/O.
- **UI:** Angular 22, the "Scorecard" design. It has a portfolio matrix and an account page with an events-per-week chart, a signal ledger, locations × signals, data quality and unusual hours.

The full behaviour is specified in [SPEC.md](SPEC.md), and the approved mockups are in [design/](design/) (`b-scorecard` is the chosen direction).

## Run it

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Node.js](https://nodejs.org/) (current LTS). You don't need a database server.

```bash
npm install     # restores .NET packages and installs the web app (via postinstall)
npm start       # runs the API and the web app together; Ctrl+C stops both
```

| What | URL |
|---|---|
| Web app | http://localhost:4200 |
| API | http://localhost:5080/api (e.g. `/api/meta`, `/api/accounts`) |

`npm start` uses [`concurrently`](https://www.npmjs.com/package/concurrently) to run `dotnet watch` for the API and `ng serve` for the UI, with output prefixed `[api]` / `[web]`. The Angular dev server proxies `/api` to port 5080 ([proxy.conf.json](src/web/proxy.conf.json)), so the browser only talks to one origin and the API needs no CORS setup.

**Database:** on startup the API applies its EF Core migrations. `InitialCreate` builds the schema from [sql/squema.sql](sql/squema.sql), and `SeedData` runs [sql/seed.sql](sql/seed.sql), which is embedded into `Relay.Data` at build time. The first run creates `src/Relay.Api/relay.db`. **To reset the data, stop the app, delete `src/Relay.Api/relay.db*` and start again.**

Other scripts:

| Script | Does |
|---|---|
| `npm test` | `dotnet test` (engine unit tests + an integration test over a freshly migrated SQLite DB), then the Angular unit tests |
| `npm run setup` | re-runs `dotnet restore` + `npm install` for the web app |
| `npm run start:api` / `start:web` | runs one side only |

## Using it

- **Top bar:**
  - **As of**: pick a date to replay any past moment. A picked date means the end of that day in UTC. **Latest** goes back to the newest data.
  - **Window**: 1 day, 7 days (default) or 30 days.
  - **Sensitivity**: Relaxed, Normal or Strict.
  - All three live in the URL, so any view can be shared by link.
- **All accounts:** one row per account.
  - Each cell shows the recent value and the signed distance from usual (a robust z-score).
  - Colour shows direction: blue is better than usual, orange is worse, violet is different but neither good nor bad. Stronger colour means further from usual.
  - "Last 25 weeks" is the account's weekly verdict history. The dots are its locations.
- **Account page**, top to bottom:
  - The verdict and its reasons.
  - **Events per week** on a log scale, with the usual band as it was at the time. Click a week to check that date.
  - The signal ledger.
  - Locations × signals. Select a site to see its reasons.
  - Data quality.
  - Unusual hours, as a weekday × local-hour heatmap.

## Design decisions

Full detail is in [SPEC.md §4–6 and §11](SPEC.md). In short:

- **Compare the account to its own history.** The recent window is compared with the same-length periods just before it:
  - 7 days → the prior 8 weeks.
  - 1 day → the same weekday in each of the prior 8 weeks, which controls for weekday effects.
  - 30 days → the prior 4 periods.

  Periods before the data starts, or before the account was created, are dropped rather than counted as zero.
- **Robust statistics**, so a past spike can't hide later changes:
  - **Counts:** median ± spread, where spread = max(1.4826·MAD, √median, 1). The √median floor stops tiny changes from flagging when history is perfectly flat.
  - **Rates** (missed calls, voicemail, conversion, no-shows, odd hours, no outcome): the pooled baseline rate, with spread = max(binomial noise at the recent sample size, week-to-week MAD). Small samples therefore don't over-flag. Each baseline week's weight in the pool is capped (D6).
  - **Talk time:** the median of connected calls vs the median of weekly medians, with a 30-second floor on the spread.
- **Sensitivity** only moves the thresholds: |z| ≥ 2 / 3 for normal, 2.5 / 4 for relaxed, 1.5 / 2.5 for strict. Relaxing can therefore never add flags.
- **Verdicts are checked in this order:**
  1. No activity on record
  2. Insufficient history (fewer than 4 comparison periods, or usually fewer than 3 events per period)
  3. Unusual
  4. Worth a look
  5. Normal

  Every deviation is flagged whatever its direction. Polarity only labels it better, worse or neutral. The top 3 flagged signals become the reasons.
- **Data is cleaned at read time and never modified:**
  - Exact duplicate rows count once, and the number removed is shown.
  - Missing outcomes are left out of rate denominators, and their share is itself a signal.
  - Missing durations are left out of talk time.
- **Unusual hours** are local hours holding under 2% of the account's baseline activity. Local time uses the account's IANA timezone, so DST is handled (Phoenix and UTC included).
- **Architecture:** `Relay.Domain` is a pure engine (events + parameters in, assessments out) and is unit-tested on synthetic data. `Relay.Data` maps the SQLite rows. `Relay.Api` only loads events and shapes the DTOs. The UI draws its charts as hand-built SVG, with no chart library.

### Two additions made while implementing

- **D5, a large surge still counts in low-volume scopes.** A site that usually sees ~1 event a day would otherwise show "not enough history" on Account 6's 805-event day. An unusual surge of 10 or more events is now flagged.
- **D6, capped pooling for rates.** One spike week of 800 connected calls used to dominate the pooled missed-call rate and flag ordinary weeks. Capping each week's weight at 3× the median week fixes that.

## Project layout

```
package.json           npm install / start / test for the whole repo
Relay.sln
src/Relay.Domain       assessment engine (no EF, no I/O)
src/Relay.Data         DbContext, entities, migrations; embeds sql/seed.sql
src/Relay.Api          minimal API endpoints, validation (ProblemDetails), DI
src/Relay.Domain.Tests xUnit: engine tests + seed integration tests (SPEC §10)
src/web                Angular app (standalone components, signals, httpResource)
sql/                   schema + seed (source of truth, unchanged)
design/                mockups from the design exploration
```

API endpoints (all accept `asOf`, `window=1|7|30`, `sensitivity=relaxed|normal|strict`):

| Endpoint | Returns |
|---|---|
| `GET /api/meta` | data time span, default and allowed parameters |
| `GET /api/accounts` | every account with its verdict, reasons, metrics, 25-week verdict history and site verdicts |
| `GET /api/accounts/{id}/assessment` | full assessment: periods, metrics, reasons, data quality, locations |
| `GET /api/accounts/{id}/series?metric=&location=` | weekly series with the band as computed at each week's end |
| `GET /api/accounts/{id}/heatmap?location=` | weekday × local-hour grid: recent counts and the baseline average |

Invalid parameters return 400 with ProblemDetails, and an unknown account or location returns 404.

## Known data quirks

- **Account 20 (Quiet Harbor Spa)** has no events, so it shows "No activity".
- **Account 6** logged 805 events on 2026-06-03, against about 11 on a normal day, across all 15 sites. It is flagged when replayed and ignored by later baselines.
- There are **12 exact duplicate rows**, about 400 events with no outcome, and 313 calls with no duration.
- **Call durations** hit exactly 1500s many times, which looks like a cap or truncation upstream. The engine does not correct for it.

## Troubleshooting

- **"An Application Control policy has blocked this file"** is Windows Smart App Control rejecting an unsigned build of one of the project's DLLs. It depends on the exact bytes of the build, so a trivial code change, or changing the `BuildTag` constant in `src/Relay.Domain/Stats.cs`, gets around it. Signing the assemblies would fix it properly.
- **`EPERM: operation not permitted, unlink ...node_modules...`** during `npm install`: a running `npm start` (the Angular dev server loads native modules such as `lmdb`) or an editor is holding the file. Stop the app with Ctrl+C in its terminal, then install again.
- **Port 5080 or 4200 already in use:** stop the other process, or change `applicationUrl` in [launchSettings.json](src/Relay.Api/Properties/launchSettings.json) and `target` in [proxy.conf.json](src/web/proxy.conf.json) together.

## Next steps

- **Calibration** ([SPEC §11](SPEC.md)):
  - Per-site and odd-hour checks can still flag on very few events.
  - Replaying history shows frequent flags for some accounts. A 2-week persistence rule or looser defaults are worth testing.
  - Lead conversion counts leads that are still open.
- **Performance:** cache events in memory. The dataset is static, and the list endpoint recomputes 25 weekly verdicts per account on every request (~150 ms today).
- **Location drill-down:** use the existing `location` filter on `/series` and `/heatmap` to open a site's own chart and heatmap.
- **Handover:** a short "copy summary for the ticket" text on the account page.
- **Production hardening:** auth, a hosted build (API serving the built SPA), and signed assemblies.
