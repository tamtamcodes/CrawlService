using System.Text.Json;
using SocialCrawler.Models;

namespace SocialCrawler.Services;

public static class CookieService
{
    public static List<PlaywrightCookie> ParseJsonCookies(JsonElement data, string defaultDomain = ".tiktok.com")
    {
        var parsed = new List<PlaywrightCookie>();
        var seenCookies = new Dictionary<string, PlaywrightCookie>();

        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("name", out var nameProp) ||
                    !item.TryGetProperty("value", out var valueProp)) continue;

                var cookieName = nameProp.GetString() ?? "";
                if (string.IsNullOrEmpty(cookieName)) continue;

                var domain = item.TryGetProperty("domain", out var d) ? d.GetString() ?? defaultDomain : defaultDomain;
                var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "/" : "/";

                var cookie = new PlaywrightCookie
                {
                    Name = cookieName,
                    Value = valueProp.GetString() ?? valueProp.ToString(),
                    Domain = domain,
                    Path = path
                };

                if (item.TryGetProperty("expires", out var exp) && exp.ValueKind != JsonValueKind.Null)
                {
                    if (exp.TryGetDouble(out var expVal)) cookie.Expires = expVal;
                }
                else if (item.TryGetProperty("expirationDate", out var expDate) && expDate.ValueKind != JsonValueKind.Null)
                {
                    if (expDate.TryGetDouble(out var expDateVal)) cookie.Expires = expDateVal;
                }

                if (item.TryGetProperty("secure", out var sec) && sec.ValueKind == JsonValueKind.True)
                    cookie.Secure = true;
                if (item.TryGetProperty("httpOnly", out var ho) && ho.ValueKind == JsonValueKind.True)
                    cookie.HttpOnly = true;

                if (item.TryGetProperty("sameSite", out var ss) && ss.ValueKind != JsonValueKind.Null)
                {
                    var ssStr = ss.GetString()?.ToLower() ?? "";
                    cookie.SameSite = ssStr switch
                    {
                        "no_restriction" or "none" => "None",
                        "lax" => "Lax",
                        "strict" => "Strict",
                        _ => null
                    };
                }

                // Handle duplicates: keep the cookie with the more general domain (e.g., .tiktok.com over www.tiktok.com)
                var key = $"{cookieName}_{path}";
                if (!seenCookies.ContainsKey(key))
                {
                    seenCookies[key] = cookie;
                }
                else
                {
                    var existing = seenCookies[key];
                    // Prefer domains starting with '.' (more general)
                    if (domain.StartsWith(".") && !existing.Domain.StartsWith("."))
                    {
                        seenCookies[key] = cookie;
                    }
                    // Otherwise keep the one with longer expiration
                    else if (cookie.Expires.HasValue && existing.Expires.HasValue &&
                             cookie.Expires.Value > existing.Expires.Value)
                    {
                        seenCookies[key] = cookie;
                    }
                }
            }
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in data.EnumerateObject())
            {
                var cookie = new PlaywrightCookie
                {
                    Name = prop.Name,
                    Value = prop.Value.GetString() ?? prop.Value.ToString(),
                    Domain = defaultDomain,
                    Path = "/"
                };
                var key = $"{cookie.Name}_{cookie.Path}";
                if (!seenCookies.ContainsKey(key))
                {
                    seenCookies[key] = cookie;
                }
            }
        }

        return seenCookies.Values.ToList();
    }

    public static List<PlaywrightCookie> ParseCookiesFromElement(JsonElement element, string defaultDomain = ".tiktok.com")
    {
        if (element.ValueKind == JsonValueKind.Array || element.ValueKind == JsonValueKind.Object)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("cookies", out var inner))
                return ParseJsonCookies(inner, defaultDomain);
            return ParseJsonCookies(element, defaultDomain);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString() ?? "";
            try
            {
                var doc = JsonDocument.Parse(str);
                return ParseCookiesFromElement(doc.RootElement, defaultDomain);
            }
            catch
            {
                return ParseCookieString(str, defaultDomain);
            }
        }

        return new List<PlaywrightCookie>();
    }

    public static List<PlaywrightCookie> ParseCookieString(string input, string defaultDomain = ".tiktok.com")
    {
        var parsed = new List<PlaywrightCookie>();
        if (string.IsNullOrWhiteSpace(input)) return parsed;

        if (!input.Contains('='))
        {
            parsed.Add(new PlaywrightCookie
            {
                Name = "msToken",
                Value = input.Trim(),
                Domain = defaultDomain,
                Path = "/"
            });
            return parsed;
        }

        foreach (var part in input.Split("; "))
        {
            var idx = part.IndexOf('=');
            if (idx < 0) continue;
            parsed.Add(new PlaywrightCookie
            {
                Name = part[..idx].Trim(),
                Value = part[(idx + 1)..].Trim(),
                Domain = defaultDomain,
                Path = "/"
            });
        }

        return parsed;
    }

    public static (bool Expired, string Reason) CheckCookiesExpiration(List<PlaywrightCookie> cookies)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var cookie in cookies)
        {
            if (cookie.Name is "sessionid" or "msToken" && cookie.Expires.HasValue)
            {
                if (now > (long)cookie.Expires.Value)
                {
                    var readable = DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires.Value)
                        .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    return (true, $"Cookie '{cookie.Name}' expired at {readable}");
                }
            }
        }
        return (false, "");
    }

    public static (bool Expired, string Reason) CheckFacebookCookiesExpiration(List<PlaywrightCookie> cookies)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var cookie in cookies)
        {
            if (cookie.Name is "c_user" or "xs" && cookie.Expires.HasValue)
            {
                if (now > (long)cookie.Expires.Value)
                {
                    var readable = DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires.Value)
                        .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                    return (true, $"Cookie '{cookie.Name}' expired at {readable}");
                }
            }
        }
        return (false, "");
    }
}
