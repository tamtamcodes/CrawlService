using System.Text.Json;
using System.Text.Json.Nodes;

namespace SocialCrawler.Services;

public static class JsonFieldSelector
{
    public static object Apply(object source, IEnumerable<string>? fields, JsonSerializerOptions options)
    {
        var paths = Normalize(fields).ToList();
        if (paths.Count == 0 || paths.Any(p => p == "*")) return source;

        var json = JsonSerializer.Serialize(source, options);
        var node = JsonNode.Parse(json);
        if (node == null) return source;

        var result = new JsonObject();
        foreach (var path in paths)
        {
            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) continue;
            CopyPath(node, result, parts, 0);
        }

        return result;
    }

    private static IEnumerable<string> Normalize(IEnumerable<string>? fields)
    {
        if (fields == null) yield break;

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field)) continue;
            foreach (var part in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part)) yield return part;
            }
        }
    }

    private static void CopyPath(JsonNode source, JsonObject target, string[] parts, int index)
    {
        if (index >= parts.Length) return;
        if (source is not JsonObject sourceObj) return;

        var key = parts[index];
        if (!sourceObj.TryGetPropertyValue(key, out var value) || value == null) return;

        if (index == parts.Length - 1)
        {
            target[key] = value.DeepClone();
            return;
        }

        if (value is JsonArray arr)
        {
            var targetArray = target[key] as JsonArray;
            if (targetArray == null)
            {
                targetArray = new JsonArray();
                target[key] = targetArray;
            }

            for (var i = 0; i < arr.Count; i++)
            {
                if (arr[i] == null) continue;
                while (targetArray.Count <= i) targetArray.Add(new JsonObject());
                if (targetArray[i] is JsonObject itemTarget)
                    CopyPath(arr[i]!, itemTarget, parts, index + 1);
            }

            return;
        }

        if (value is JsonObject childObj)
        {
            var childTarget = target[key] as JsonObject;
            if (childTarget == null)
            {
                childTarget = new JsonObject();
                target[key] = childTarget;
            }

            CopyPath(childObj, childTarget, parts, index + 1);
        }
    }
}
