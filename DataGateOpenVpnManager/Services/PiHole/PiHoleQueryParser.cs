using Newtonsoft.Json.Linq;

namespace DataGateOpenVpnManager.Services.PiHole;

public static class PiHoleQueryParser
{
    public static IReadOnlyList<PiHoleQueryRecord> ParseQueriesResponse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<PiHoleQueryRecord>();

        var root = JToken.Parse(json);
        var queriesToken = root["queries"] ?? root["data"]?["queries"] ?? root;
        if (queriesToken is not JArray array)
            return Array.Empty<PiHoleQueryRecord>();

        var result = new List<PiHoleQueryRecord>(array.Count);
        foreach (var item in array)
        {
            if (item is not JObject obj)
                continue;

            var id = ReadLong(obj, "id");
            if (id <= 0)
                continue;

            var domain = ReadString(obj, "domain");
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            var clientIp = ReadClientIp(obj);
            if (string.IsNullOrWhiteSpace(clientIp))
                continue;

            var queriedAt = ReadTimestamp(obj);
            var status = ReadStatus(obj);
            var queryType = ReadString(obj, "type");

            result.Add(new PiHoleQueryRecord(
                id,
                clientIp,
                domain,
                queryType,
                status,
                queriedAt));
        }

        return result;
    }

    public static int? ReadRecordsTotal(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var root = JToken.Parse(json);
        var total = root["recordsTotal"] ?? root["data"]?["recordsTotal"];
        return total?.Type switch
        {
            JTokenType.Integer => total.Value<int>(),
            JTokenType.String when int.TryParse(total.Value<string>(), out var parsed) => parsed,
            _ => null
        };
    }

    public static string? ReadSessionId(string authJson)
    {
        if (string.IsNullOrWhiteSpace(authJson))
            return null;

        var root = JToken.Parse(authJson);
        return ReadString(root, "session", "sid")
               ?? ReadString(root, "sid");
    }

    private static string ReadClientIp(JObject obj)
    {
        var clientToken = obj["client"];
        if (clientToken is JObject clientObj)
        {
            return ReadString(clientObj, "ip")
                   ?? ReadString(clientObj, "name")
                   ?? string.Empty;
        }

        return clientToken?.Type == JTokenType.String
            ? clientToken.Value<string>() ?? string.Empty
            : ReadString(obj, "client_ip", "clientip");
    }

    private static DateTimeOffset ReadTimestamp(JObject obj)
    {
        var unix = ReadDouble(obj, "time")
                   ?? ReadDouble(obj, "timestamp")
                   ?? ReadDouble(obj, "query_time");
        if (unix is > 0)
            return DateTimeOffset.FromUnixTimeSeconds((long)unix);

        var iso = ReadString(obj, "time", "timestamp");
        if (!string.IsNullOrWhiteSpace(iso) && DateTimeOffset.TryParse(iso, out var parsed))
            return parsed.ToUniversalTime();

        return DateTimeOffset.UtcNow;
    }

    private static string ReadStatus(JObject obj)
    {
        var status = ReadString(obj, "status", "reply", "reply_type");
        return string.IsNullOrWhiteSpace(status) ? "unknown" : status;
    }

    private static string ReadString(JToken token, params string[] names)
    {
        foreach (var name in names)
        {
            if (token[name] is not JToken value || value.Type == JTokenType.Null)
                continue;

            if (value is JObject nested && names.Length > 1 && name != names[^1])
            {
                var nestedValue = ReadString(nested, names[^1]);
                if (!string.IsNullOrWhiteSpace(nestedValue))
                    return nestedValue;
            }

            var text = value.Type switch
            {
                JTokenType.String => value.Value<string>(),
                JTokenType.Integer => value.Value<long>().ToString(),
                JTokenType.Float => value.Value<double>().ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString()
            };

            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return string.Empty;
    }

    private static long ReadLong(JObject obj, string name)
    {
        var token = obj[name];
        if (token is null || token.Type == JTokenType.Null)
            return 0;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float => (long)token.Value<double>(),
            JTokenType.String when long.TryParse(token.Value<string>(), out var parsed) => parsed,
            _ => 0
        };
    }

    private static double? ReadDouble(JObject obj, string name)
    {
        var token = obj[name];
        if (token is null || token.Type == JTokenType.Null)
            return null;

        return token.Type switch
        {
            JTokenType.Integer => token.Value<long>(),
            JTokenType.Float => token.Value<double>(),
            JTokenType.String when double.TryParse(
                token.Value<string>(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }
}
