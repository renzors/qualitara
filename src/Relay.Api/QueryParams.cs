using System.Globalization;
using Relay.Data;
using Relay.Domain;

namespace Relay.Api;

/// <summary>Parses and validates the shared asOf / window / sensitivity query parameters (§7).</summary>
public static class QueryParams
{
    public static bool TryParse(HttpRequest http, DatasetBounds bounds, out AssessmentRequest request, out Dictionary<string, string[]> errors)
    {
        errors = [];
        var q = http.Query;
        DateTime asOf = bounds.LastEventUtc;
        int window = 7;
        var sensitivity = Sensitivity.Normal;

        if (q.TryGetValue("asOf", out var a) && !string.IsNullOrWhiteSpace(a))
        {
            if (!DateTime.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out asOf))
                errors["asOf"] = ["asOf must be an ISO-8601 timestamp, e.g. 2026-07-27T22:20:34Z."];
            else if (asOf < bounds.StartUtc || asOf > bounds.LastDayEndUtc)
                errors["asOf"] = [$"asOf must be between {bounds.StartUtc:yyyy-MM-ddTHH:mm:ssZ} and {bounds.LastDayEndUtc:yyyy-MM-ddTHH:mm:ssZ}."];
            asOf = DateTime.SpecifyKind(asOf, DateTimeKind.Utc);
        }

        if (q.TryGetValue("window", out var w) && !string.IsNullOrWhiteSpace(w))
        {
            if (!int.TryParse(w, NumberStyles.Integer, CultureInfo.InvariantCulture, out window) || !AssessmentRequest.AllowedWindows.Contains(window))
                errors["window"] = ["window must be 1, 7 or 30 (days)."];
        }

        if (q.TryGetValue("sensitivity", out var s) && !string.IsNullOrWhiteSpace(s))
        {
            if (!Enum.TryParse(s, ignoreCase: true, out sensitivity) || !Enum.IsDefined(sensitivity) || int.TryParse(s, out _))
                errors["sensitivity"] = ["sensitivity must be relaxed, normal or strict."];
        }

        request = new AssessmentRequest(asOf, window, sensitivity);
        return errors.Count == 0;
    }

    public static ParamsDto ToDto(AssessmentRequest r, EngineOptions o)
    {
        var t = o.ThresholdsFor(r.Sensitivity);
        return new ParamsDto(r.AsOfUtc, r.WindowDays, r.Sensitivity, t.Look, t.Unusual);
    }
}
