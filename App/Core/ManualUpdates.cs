// Copyright (c) Mouse Without Borders Portable contributors
// Licensed under the MIT license.
using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MouseWithoutBorders.Core;

internal static class ManualUpdates
{
    internal const string Releases = "https://github.com/aeae1/MouseWithoutBorders-Portable/releases";
    internal static async Task<string> Check(string current, CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1024 * 1024 };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MouseWithoutBorders-Portable/" + current);
        string json = await http.GetStringAsync("https://api.github.com/repos/aeae1/MouseWithoutBorders-Portable/releases?per_page=100", token);
        return SelectNewer(json, current);
    }
    internal static string SelectNewer(string json, string current)
    {
        var installed = Parse(current) ?? throw new InvalidOperationException("This app version cannot be compared. Open GitHub to check manually.");
        return JArray.Parse(json).Where(r => r["draft"]?.Value<bool>() == false)
            .Select(r => (Tag: r["tag_name"]?.Value<string>(), Pre: r["prerelease"]?.Value<bool>() == true))
            .Select(r => (r.Tag, r.Pre, V: Parse(r.Tag?.Replace("mwb-v", "", StringComparison.Ordinal))))
            .Where(r => r.V != null && (installed.Rc != int.MaxValue || !r.Pre)
                && r.Pre == (r.V.Value.Rc != int.MaxValue) && Compare(r.V.Value, installed) > 0)
            .OrderByDescending(r => r.V.Value.Version).ThenByDescending(r => r.V.Value.Rc)
            .Select(r => r.Tag).FirstOrDefault();
    }
    private static int Compare((Version Version, int Rc) a, (Version Version, int Rc) b)
    { int result = a.Version.CompareTo(b.Version); return result == 0 ? a.Rc.CompareTo(b.Rc) : result; }
    private static (Version Version, int Rc)? Parse(string value)
    {
        var match = Regex.Match(value ?? "", @"^(\d+\.\d+\.\d+)(?:-rc\.(\d+))?(?:\+[A-Za-z0-9.-]+)?$");
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var v)) return null;
        int rc = int.MaxValue;
        if (match.Groups[2].Success && !int.TryParse(match.Groups[2].Value, out rc)) return null;
        return (v, rc);
    }
}
