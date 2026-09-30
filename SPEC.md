# Relay — "Is this normal?" Activity Assessment Tool — Spec v1

## 1. Purpose

An internal tool for **Customer Success / Support**. It answers one question for any customer account: *"Is this account's recent activity normal?"* It gives a clear verdict, lists the reasons in plain English, and lets the agent drill down by location.

Out of scope for v1: customer self-serve access, authentication, alerting/notifications, industry peer comparison, real-time streaming.

## 2. Stack

| Layer | Choice |
|---|---|
| API | ASP.NET Core on **.NET 10 (LTS)**, controllers or minimal APIs, ProblemDetails for errors |
| Data | **EF Core 10 + SQLite** (file DB, e.g. `relay.db`) |
| Schema + seed | Applied through **regular EF Core migrations** (see §3) |
| UI | **Angular (latest stable major at scaffold time)**: standalone components, signals, router. Charts are small hand-built SVG components (sparkline, distance bar, events-per-week chart), matching the mockups, with no chart library |
| Tests | xUnit for the stats engine (+ a thin integration test over a migrated SQLite DB) |
| Auth | None |
| Dev runner | One command from the repo root: `npm start` (see §12) |

Layout (all application code under `src/`):
```
/package.json                 root scripts: setup, start, test (runs API + web together)
/Relay.sln
/src/Relay.Api                ASP.NET Core host, endpoints, DI
/src/Relay.Domain             Pure assessment engine (no EF, no I/O)
/src/Relay.Data               DbContext, entities, migrations, embedded sql/*.sql
/src/Relay.Domain.Tests       xUnit tests for the engine
/src/web                      Angular app (dev server proxies /api to the API)
/sql                          squema.sql, seed.sql (source of truth, unchanged)
/design                       approved mockups (b-scorecard is the chosen direction)
README.md                     prerequisites, one-command run, design decisions, next steps
```

## 3. Data & migrations

- `Relay.Data` owns the migrations. `sql/squema.sql` and `sql/seed.sql` are embedded as resources and stay the source of truth.
  - `0001_InitialCreate`: creates `accounts` and `activity_events` (EF model matches `squema.sql`), plus an index on `activity_events(account_id, occurred_at)`.
  - `0002_SeedData`: runs the embedded `seed.sql` via `migrationBuilder.Sql(...)` in one transaction. `Down` deletes the seeded rows.
- The API calls `Database.Migrate()` on startup, so running the API is enough to get a ready database.
- `occurred_at` / `created_at` are stored in UTC. A value converter forces `DateTimeKind.Utc` on read.
- Raw data is **never mutated**. All cleaning (dedupe, NULL handling) happens at query/engine time.

### Known data characteristics (from profiling the seed)

| Fact | Handling |
|---|---|
| 12,626 events, 2026-02-01 → 2026-07-27 22:20 UTC | Default `asOf` = latest event (§4) |
| 20 accounts, 1–15 locations each, ~6 to ~70 events/week | Self-baseline only; low-data rules (§6) |
| Account 20 (Quiet Harbor Spa) has **zero events** | "No activity on record" state |
| Account 6: **805 events on 2026-06-03** (normally ~11/day), all 15 sites | Robust baseline absorbs it; acceptance test (§10) |
| **12 exact-duplicate rows** (same content, different ids) | Deduped + surfaced (§5.5) |
| ~400 NULL outcomes, 313 calls with NULL duration | Excluded from denominators + surfaced (§5.5) |
| Call durations max out at exactly 1500s repeatedly | Possible cap/truncation; noted in README, no special handling |
| Timezones include `America/Phoenix` (no DST) and `UTC` | IANA conversion via `TimeZoneInfo` (ICU) |
| Outcomes by type: calls `connected/missed/voicemail`; leads `open/converted`; appointments `completed/no_show` | Used for the rate metrics |

## 4. Core definitions

- **asOf**: a UTC instant marking "now". Default = `MAX(occurred_at)` across the dataset. It can be overridden (UI date picker → end of the selected UTC day).
- **Window (W)**: 1, 7 or 30 days. **Default 7**, because it keeps the weekday mix constant (weekends are ~4× quieter) and gives small accounts enough events.
- **Recent period** = `(asOf − W, asOf]`.
- **Baseline periods**: prior periods of the same length, immediately preceding the recent one:
  - W=1: the same 24h slot on the **same weekday** for the prior **8 weeks** (controls weekday effects)
  - W=7: the prior **8** weeks
  - W=30: the prior **4** 30-day periods (limited by ~6 months of data)
  - A baseline period counts as usable only if it lies entirely after the dataset start *and* after the account's `created_at`. Periods before that are excluded, **not** counted as zeros.
- **Sensitivity**: `relaxed | normal | strict` maps to robust-z thresholds:

| Sensitivity | "Worth a look" at \|z\| ≥ | "Unusual" at \|z\| ≥ |
|---|---|---|
| relaxed | 2.5 | 4.0 |
| **normal** (default) | 2.0 | 3.0 |
| strict | 1.5 | 2.5 |

## 5. Metrics

Every metric produces: `recentValue`, `expected` (center), `expectedLow/High` (band at the "worth a look" threshold), `z`, `status` (normal/worth_a_look/unusual/insufficient_data), `direction` (higher/lower), `polarity` (good/bad/neutral), `reason` (plain-English sentence), and `sampleSize`.

### 5.1 Volume (per event type + total)
- Counts per period for `call_received`, `lead_created`, `appointment_set`, and total.
- Center = **median** of baseline counts. Spread = `max(1.4826·MAD, √median, 1)`. The √median (Poisson) floor stops a MAD of zero from flagging tiny changes.
- `z = (recent − median) / spread`.
- Zero recent events with a baseline median ≥ 3 is always at least "Unusual".

### 5.2 Outcome rates
| Metric | Numerator / denominator (known outcomes only) |
|---|---|
| Missed-call rate | `missed` / (`connected`+`missed`+`voicemail`) |
| Voicemail rate | `voicemail` / same |
| Lead conversion rate | `converted` / (`open`+`converted`) |
| Appointment no-show rate | `no_show` / (`completed`+`no_show`) |

- Center p₀ = **pooled** baseline rate (Σ numerators / Σ denominators).
- Spread = `max( √(p₀(1−p₀)/n_recent), 1.4826·MAD(baseline period rates) )`. This combines binomial sampling noise (so small samples aren't over-flagged) with real week-to-week variability.
- Needs n_recent ≥ 10 known-outcome events, otherwise the metric is `insufficient_data`.
- The UI shows the Wilson interval of the recent rate for context.

### 5.3 Call duration
- **Default: connected calls with non-NULL duration only** (talk time; durations on missed calls aren't meaningful). *See open question Q1.*
- Metric = median duration in the recent period. Center/spread = median / `max(1.4826·MAD, 30s)` of the baseline period medians.
- Needs ≥ 10 qualifying calls recently and in ≥ 4 baseline periods.

### 5.4 Time-of-day (local)
- Convert every event to account-local time using the account's IANA timezone (DST-correct).
- Build the account's baseline 24-bucket local-hour histogram. **Rare hours** = hours holding < 2% of baseline events.
- Metric = share of recent events that fall in rare hours, compared to the baseline share using the rate method from §5.2.
- The reason text names the hours, e.g. *"14% of activity between 1–5am local (usually 1%)"*.
- The heatmap (weekday × local hour) shows recent counts next to the baseline average per period.

### 5.5 Data quality (reported, and partly assessed)
- **Duplicates**: rows identical on (account_id, location, event_type, occurred_at, duration_seconds, outcome) count once in every metric. Show the count as "N suspected duplicates removed".
- **NULL outcomes**: excluded from rate denominators. The NULL-outcome share is itself assessed as a rate metric (polarity *bad*), so a sudden jump in unknown outcomes gets flagged as a data-quality issue.
- **NULL durations**: excluded from the duration metric; share shown.

### 5.6 Polarity (direction → good/bad label)
| Metric | ↑ higher | ↓ lower |
|---|---|---|
| Volume (all types) | good | bad |
| Missed-call rate, voicemail rate | bad | good |
| Lead conversion rate | good | bad |
| No-show rate | bad | good |
| Call duration | neutral | neutral |
| Off-hours share | neutral | neutral |
| NULL-outcome share | bad | good |

Every deviation is flagged regardless of polarity; polarity only drives labelling and colour ("Unusually good" / "Unusually bad" / "Unusual").

## 6. Verdicts & low data

**Account / location verdict** (in order of evaluation):
1. **No activity on record**: no events ever (e.g. Account 20).
2. **Insufficient history**: fewer than 4 usable baseline periods (3 for W=30), **or** baseline median total volume < 3 events/period. The recent numbers are shown, but no verdict is given.
3. **Unusual**: any metric is `unusual`.
4. **Worth a look**: any metric is `worth_a_look`.
5. **Normal**.

**Reasons**: the top 3 flagged metrics by |z|, as sentences like *"Missed-call rate 41% vs usual 18–26% (bad)"*. Metrics that are `insufficient_data` never affect the verdict; they are listed as "not assessed".

**Locations**: the same engine runs per location with the same rules. Special cases:
- A location with no baseline but recent activity → **"New location"** note.
- A location with baseline activity but zero recent events → assessed normally, so it shows as a volume drop.

## 7. API

All endpoints accept `asOf` (ISO-8601 UTC, optional), `window` (`1|7|30`, default 7) and `sensitivity` (`relaxed|normal|strict`, default normal). Invalid values → 400 ProblemDetails.

| Endpoint | Returns |
|---|---|
| `GET /api/meta` | dataset min/max timestamps, default `asOf`, allowed windows/sensitivities |
| `GET /api/accounts` | list: id, name, industry, timezone, location count, verdict, top reasons, recent total vs expected |
| `GET /api/accounts/{id}/assessment` | full result: period bounds (recent + each baseline period), verdict, reasons, all metrics (§5), data-quality block, per-location verdicts + metrics |
| `GET /api/accounts/{id}/series?metric=&location=` | weekly series over the full history (weeks end on the `asOf` weekday and time): value, expected, band (rolling, as the engine would have computed at each week's end). Used by the ledger sparklines and, with `metric=total`, by the events-per-week chart |
| `GET /api/accounts/{id}/heatmap?location=` | 7×24 local grid: recent counts and baseline average |

Unknown account → 404. The engine in `Relay.Domain` is pure: it takes events plus parameters and returns assessments. The API only loads events and maps DTOs.

## 8. UI (Angular) — "Scorecard" direction

The approved direction is **Design B · Scorecard** ([design/b-scorecard/](design/b-scorecard/)), with one addition taken from Design D. It uses the Geist + Geist Mono type pairing and has light and dark themes. Colour encodes the *polarity* of a deviation: blue = better than usual, orange = worse, violet = different but neutral. Colour intensity scales with |z|. Verdict markers are red (unusual), amber (worth a look) and grey (normal).

- **Global controls** in the top bar (persisted in URL query params, so views are shareable): as-of date (bounded to the dataset range, with a "Latest" reset), window segmented control (1d / 7d / 30d), sensitivity segmented control (Relaxed / Normal / Strict).
- **Portfolio matrix** (`/accounts`), mockup: [portfolio.html](design/b-scorecard/portfolio.html)
  - Summary line: period description plus counts per verdict.
  - Legend for cell colours, the ±2 / ±3 thresholds, and "not enough data".
  - One row per account, sorted by verdict and then max |z|. Columns: verdict, account (industry · timezone city), 4 volume cells (All, Calls, Leads, Appts), 4 outcome cells (Missed, Voicemail, Converted, No-show), 2 behaviour cells (Talk time, Odd hours), 1 data cell (No outcome), last-25-weeks verdict strip, and one dot per site coloured by that site's verdict.
  - Each cell shows the recent value and the signed z. A tooltip gives "value vs usual lo–hi". Not-assessed cells show "·".
  - Clicking a row opens the account.
- **Account detail** (`/accounts/:id`), mockup: [account.html](design/b-scorecard/account.html), shown top to bottom:
  1. **Header**: verdict marker, name, industry, location count, and the recent period in the account's local time.
  2. **Events per week** (new; taken from [design/d-monitor/account.html](design/d-monitor/account.html) and restyled with B's tokens). A full-width overview of total weekly events across the whole history:
     - Log-scale y-axis, so a spike like Account 6's 805-event day stays readable without flattening normal weeks.
     - Shaded rolling "usual" band (median ± 2 × spread of the prior 8 weeks). Weeks with fewer than 4 prior weeks have no band.
     - The recent period is highlighted. Weeks that were unusual when they happened get an annotated marker (e.g. "881 · incl. 805 on Jun 3").
     - A track under the axis shows the baseline span and the recent window. Clicking a week sets `asOf` to that week's end, which replays the page.
     - Data source: `GET /series?metric=total`.
  3. **Signal ledger**: one row per signal, showing name + sample size, 25-week sparkline with the baseline band and this week's dot, this week's value, usual range + median, and a distance-from-usual bar with ±2/±3 ticks. Spikes above the sparkline's cap are clipped and labelled.
  4. **Locations × signals** matrix: same cell encoding as the portfolio, rows sorted by verdict. Outcome-rate columns are omitted when no site reaches the sample minimum.
  5. **Data quality**: duplicates removed, % no outcome, % no call duration, plus a note when a past spike sits inside the baseline.
  6. **Unusual hours**: the account's rare-hour range in local time and this week's share vs usual.
- **Display rule for bands**: count ranges are rounded inward (lower bound ceiling, upper bound floor), so a flagged value never appears to sit inside its displayed range.
- **Phone width**: both matrices scroll horizontally inside their own container. Ledger rows collapse to two columns, with the sparkline spanning full width.

## 9. Testing

Unit tests (xUnit, `Relay.Domain.Tests`) on synthetic data:
- Median/MAD math, the Poisson floor when MAD = 0, and zero-volume handling
- Rate metric with small n → no false flag; with large n → flags a clear shift
- Baseline excludes periods before dataset start / `created_at`; insufficient-history thresholds
- A single huge spike in the baseline doesn't widen the band (robustness)
- Dedupe of identical rows; NULL outcome/duration exclusion; NULL-share flagging
- Timezone conversion across US DST (2026-03-08 and 2026-11-01), `America/Phoenix`, `UTC`
- Weekday-matched baseline for W=1
- Verdict aggregation, polarity mapping, sensitivity thresholds, reason text

## 10. Acceptance scenarios (against the seed)

1. Default view (`asOf` = 2026-07-27 22:20 UTC, W=7): every account except 20 gets a verdict or an "insufficient" state; Account 20 shows "No activity on record".
2. Account 6, `asOf` = 2026-06-03 end of day, W=1 → **Unusual**, volume up, flagged across its locations.
3. Account 6, default view → the 2026-06-03 spike sits in the baseline but does **not** blow up the band (still assessed sensibly).
4. The data-quality panel shows 12 duplicates removed in total across accounts (per-account counts sum to 12).
5. Changing sensitivity from strict to relaxed never raises the number of flagged metrics.

## 11. Decisions (defaults confirmed)

- **D1** Call duration uses **connected calls only**.
- **D2** Higher volume is labelled **good**, including very large surges.
- **D3** Rare-hour threshold is **2%** of baseline events. Minimums are **n ≥ 10** per rate metric and **≥ 4 baseline periods** (3 for W=30). All are constants in one options class so they can be tuned later.
- **D4** A date picked in the UI sets `asOf` to the **end of that day in UTC**.
- **D5** *(added during implementation)* A **low-volume scope** (enough baseline periods, but a median under 3 events per period) normally gets "Insufficient history". The exception: an "unusual" **surge** in total volume of **≥ 10 events** is still flagged as Unusual. Without this, acceptance scenario 2 fails: each of Account 6's sites usually sees ~1 event on a Wednesday, so the 805-event day would have shown every site as "not enough history".
- **D6** *(added during implementation)* **Pooled baseline rates cap each period's weight** at 3× the median period size. The period keeps its rate but can't dominate the pool. Plain pooling isn't robust: one spike week of 800 connected calls outweighs 8 normal weeks and drags the "usual" missed-call rate toward 0%. Normal weeks pool exactly as before.

Interpretations made while implementing (not new behaviour):
- Rate metrics also need ≥ 10 known-outcome events in the pooled baseline. The binomial SD uses p₀ smoothed by half an event, so a 0% or 100% baseline still has noise.
- Duration: a baseline period counts only if it has ≥ 10 qualifying calls, and at least the minimum number of such periods (4, or 3 for W=30) is needed.
- Locations use the **account's** rare hours, not their own. Per-site hour histograms are too thin to define "rare".
- Dataset start = midnight UTC of the first event's day. The latest pickable `asOf` is the end of the last event's UTC day.
- The weekly series also includes weeks after a replayed `asOf` (up to the last event), so the chart can step forward again.
- The portfolio also has **Voicemail** and **No outcome** columns, so every signal that can drive a verdict is visible on the row.

Calibration notes from the mockups. These are not adopted; they are recorded for tuning after v1:
- The unusual-hours and per-site checks can flag on very few events (e.g. 4 of 9). A minimum n like the one for rate metrics may be needed.
- Replaying history, 10 of 19 accounts come out "unusual" 3 or more times in 21 weeks. Consider looser defaults or a 2-week persistence rule.
- Lead conversion counts leads that are still open. Consider excluding leads younger than N days.

## 12. Running locally

**Prerequisites:** .NET 10 SDK and Node.js (current LTS). No database server is needed, because SQLite is a file created on first run.

**One command:** a root `package.json` orchestrates both apps with [`concurrently`](https://www.npmjs.com/package/concurrently):

| Script | Does |
|---|---|
| `npm run setup` | `dotnet restore` + `npm ci` in `src/web` (also run automatically by `postinstall`) |
| `npm start` | Runs the API (`dotnet watch run --project src/Relay.Api`, http://localhost:5080) and the Angular dev server (`ng serve`, http://localhost:4200) together. Output is prefixed per app, and Ctrl+C stops both |
| `npm test` | `dotnet test` + Angular unit tests |

- The Angular dev server proxies `/api` to `http://localhost:5080` via `proxy.conf.json`, so the browser only talks to one origin and the API needs no CORS setup.
- On startup the API applies EF migrations, which create `relay.db` and load the seed. The first run therefore needs no manual database step.
- Why not the alternatives:
  - **.NET Aspire AppHost** would also give one command (`dotnet run`) plus a dashboard, but it adds a project and a workload for little gain here.
  - **Docker Compose** adds a Docker requirement.
  - **The API serving the built Angular app** needs a build step before every run and loses hot reload.

  `npm start` needs only the two SDKs a developer already has.
- The README documents: prerequisites, `npm install && npm start`, URLs, how to reset the DB (delete `relay.db`), how to run the tests, the design decisions (link to this spec), and next steps.
