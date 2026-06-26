namespace SocialCrawler.Services;

public static class PeriodParser
{
    public static int ParseToSeconds(string periodStr)
    {
        var p = periodStr.Trim().ToLower();

        var simple = new Dictionary<string, int>
        {
            ["1 day"] = 86400, ["1d"] = 86400, ["day"] = 86400, ["ngày"] = 86400, ["1 ngày"] = 86400,
            ["1 week"] = 7 * 86400, ["1w"] = 7 * 86400, ["week"] = 7 * 86400, ["tuần"] = 7 * 86400, ["1 tuần"] = 7 * 86400,
            ["1 month"] = 30 * 86400, ["1m"] = 30 * 86400, ["month"] = 30 * 86400, ["tháng"] = 30 * 86400, ["1 tháng"] = 30 * 86400,
        };

        if (simple.TryGetValue(p, out var val)) return val;

        var match = System.Text.RegularExpressions.Regex.Match(p,
            @"^(\d+)\s*(d|w|m|day|week|month|ngày|tuần|tháng)s?$");
        if (match.Success)
        {
            var num = int.Parse(match.Groups[1].Value);
            return match.Groups[2].Value switch
            {
                "d" or "day" or "ngày" => num * 86400,
                "w" or "week" or "tuần" => num * 7 * 86400,
                "m" or "month" or "tháng" => num * 30 * 86400,
                _ => 86400
            };
        }

        if (int.TryParse(p, out var days))
            return days < 365 ? days * 86400 : days;

        return 86400;
    }
}
