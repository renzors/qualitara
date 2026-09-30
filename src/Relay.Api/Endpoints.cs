using Relay.Data;
using Relay.Domain;

namespace Relay.Api;

public static class Endpoints
{
    private const int HistoryWeeks = 25;

    public static void MapRelayApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/meta", async (ActivityStore store, CancellationToken ct) =>
        {
            if (await store.BoundsAsync(ct) is not { } b) return NoData();
            return Results.Ok(new MetaDto(b.FirstEventUtc, b.LastEventUtc, b.StartUtc, b.LastDayEndUtc, b.LastEventUtc,
                AssessmentRequest.AllowedWindows, 7, Enum.GetValues<Sensitivity>(), Sensitivity.Normal));
        });

        api.MapGet("/accounts", async (HttpRequest http, ActivityStore store, AssessmentEngine engine, CancellationToken ct) =>
        {
            if (await store.BoundsAsync(ct) is not { } b) return NoData();
            if (!QueryParams.TryParse(http, b, out var req, out var errors)) return Results.ValidationProblem(errors);

            var events = await store.AllEventsAsync(ct);
            var items = new List<AccountListItemDto>();
            foreach (var acct in await store.AccountsAsync(ct))
            {
                var prepared = new PreparedAccount(acct, events[acct.Id]);
                var a = engine.Assess(prepared, req, b.StartUtc);
                items.Add(new AccountListItemDto(
                    AccountDto(prepared), a.Overall.Verdict, a.Overall.Note, a.Overall.Reasons, a.Overall.RecentTotal, a.Overall.ExpectedTotal,
                    a.Overall.Metrics.Select(MetricDto.From).ToList(),
                    engine.VerdictHistory(prepared, req, b.StartUtc, HistoryWeeks),
                    a.Locations.Select(l => l.Assessment.Verdict).ToList()));
            }

            var sorted = items
                .OrderBy(i => Verdicts.Rank(i.Verdict))
                .ThenByDescending(i => i.Metrics.Where(m => m.Status != MetricStatus.InsufficientData && m.Z is not null)
                    .Select(m => Math.Abs(m.Z!.Value)).DefaultIfEmpty(0).Max())
                .ThenBy(i => i.Account.Name, StringComparer.Ordinal)
                .ToList();
            var plan = PeriodPlan.Build(req.AsOfUtc, req.WindowDays, b.StartUtc, engine.Options);
            return Results.Ok(new AccountListDto(QueryParams.ToDto(req, engine.Options), PeriodDto.From(plan.Recent), plan.BaselineRequested, sorted));
        });

        api.MapGet("/accounts/{id:int}/assessment", async (int id, HttpRequest http, ActivityStore store, AssessmentEngine engine, CancellationToken ct) =>
        {
            var (prepared, b, req, error) = await LoadAsync(id, http, store, ct);
            if (error is not null) return error;

            var a = engine.Assess(prepared!, req, b!.StartUtc);
            return Results.Ok(new AssessmentDto(
                AccountDto(prepared!), QueryParams.ToDto(req, engine.Options), PeriodDto.From(a.Plan.Recent),
                a.Plan.Baseline.Select(PeriodDto.From).ToList(), a.Plan.BaselineRequested, ScopeDto.From(a.Overall),
                new DataQualityDto(a.Quality.DuplicatesRemoved, a.Quality.RecentEvents, a.Quality.RecentNoOutcome, a.Quality.RecentCalls,
                    a.Quality.RecentCallsNoDuration, a.Quality.BaselineNote),
                a.RareHours, a.RareHoursText,
                a.Locations.Select(l => new LocationDto(l.Location, l.IsNew, ScopeDto.From(l.Assessment))).ToList()));
        });

        api.MapGet("/accounts/{id:int}/series", async (int id, string? metric, string? location, HttpRequest http, ActivityStore store,
            AssessmentEngine engine, CancellationToken ct) =>
        {
            var (prepared, b, req, error) = await LoadAsync(id, http, store, ct);
            if (error is not null) return error;

            var defs = Metrics.All;
            if (!string.IsNullOrEmpty(metric))
            {
                if (Metrics.Find(metric) is not { } def)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["metric"] = [$"Unknown metric. Use one of: {string.Join(", ", Metrics.All.Select(m => m.Key))}."],
                    });
                defs = [def];
            }
            if (!string.IsNullOrEmpty(location) && !prepared!.Locations.Contains(location)) return UnknownLocation(location);

            var weeks = engine.WeeklySeries(prepared!, req, b!.StartUtc, b.LastEventUtc, NullIfEmpty(location));
            var series = defs.Select(d => new MetricSeriesDto(d.Key, d.Label, d.Kind, weeks.Select(w =>
            {
                var m = w.Metrics.FirstOrDefault(x => x.Definition.Key == d.Key);
                return m is null
                    ? new SeriesPointDto(null, null, null, null, null, MetricStatus.InsufficientData, "–")
                    : new SeriesPointDto(m.Value, m.Expected, m.ExpectedLow, m.ExpectedHigh, m.Z is double z ? Math.Round(z, 2) : null,
                        m.Status, m.Display.Value);
            }).ToList())).ToList();

            return Results.Ok(new SeriesDto(QueryParams.ToDto(req, engine.Options), NullIfEmpty(location),
                weeks.Select(w => new WeekDto(w.Period.StartUtc, w.Period.EndUtc, w.Verdict, w.Period.EndUtc == req.AsOfUtc, w.PeakDay)).ToList(),
                series));
        });

        api.MapGet("/accounts/{id:int}/heatmap", async (int id, string? location, HttpRequest http, ActivityStore store,
            AssessmentEngine engine, CancellationToken ct) =>
        {
            var (prepared, b, req, error) = await LoadAsync(id, http, store, ct);
            if (error is not null) return error;
            if (!string.IsNullOrEmpty(location) && !prepared!.Locations.Contains(location)) return UnknownLocation(location);

            var h = engine.Heatmap(prepared!, req, b!.StartUtc, NullIfEmpty(location));
            return Results.Ok(new HeatmapDto(QueryParams.ToDto(req, engine.Options), NullIfEmpty(location), h.Recent, h.BaselineAverage,
                h.RareHours, h.RareHoursText, h.BaselinePeriods, h.RecentTotal, h.RecentRare));
        });
    }

    private static async Task<(PreparedAccount?, DatasetBounds?, AssessmentRequest, IResult?)> LoadAsync(
        int id, HttpRequest http, ActivityStore store, CancellationToken ct)
    {
        var placeholder = new AssessmentRequest(DateTime.UnixEpoch);
        if (await store.BoundsAsync(ct) is not { } b) return (null, null, placeholder, NoData());
        if (!QueryParams.TryParse(http, b, out var req, out var errors)) return (null, b, placeholder, Results.ValidationProblem(errors));
        if (await store.AccountAsync(id, ct) is not { } acct)
            return (null, b, req, Results.Problem(statusCode: 404, title: "Account not found", detail: $"There is no account with id {id}."));
        return (new PreparedAccount(acct, await store.EventsAsync(id, ct)), b, req, null);
    }

    private static AccountDto AccountDto(PreparedAccount p) =>
        new(p.Account.Id, p.Account.Name, p.Account.Industry, p.Account.TimeZoneId, p.Account.CreatedAtUtc, p.Locations.Count);

    private static IResult NoData() =>
        Results.Problem(statusCode: 503, title: "No data",
            detail: "The database has no activity events. Delete relay.db and restart the API to re-seed it.");

    private static IResult UnknownLocation(string location) =>
        Results.Problem(statusCode: 404, title: "Location not found", detail: $"This account has no location named \"{location}\".");

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
