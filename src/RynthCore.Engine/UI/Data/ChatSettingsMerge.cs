// ============================================================================
//  RynthCore.Engine - UI/Data/ChatSettingsMerge.cs
//  Three-way merge of rynthchat_settings.json for a save that finds another
//  client changed the file since this client last read it.
//
//  Every client shares the file and saves its whole state. Before this merge, a
//  client that hadn't picked up another client's change yet (its chat window was
//  closed, so it never polled; or the change was under 2 s old) wrote its stale
//  rules and standard filters back over it, and every other client then reloaded
//  that stale copy: filters "came back", or switched off, on all clients.
//
//  Per top-level setting (filters, presets, tabs, font size, colours, ...): the
//  one this client changed since its base wins; one it left alone takes the file's.
//  "filters" and "presets" are each one setting, so two clients editing rules at
//  the same moment still end with the later save's list, but a client that only
//  changed something else no longer undoes another client's rules.
//
//  Pure (BCL only, no engine state): tools/ChatFilterTests compiles this file.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RynthCore.Engine.UI.Data;

internal static class ChatSettingsMerge
{
    /// <summary>
    /// The settings to write. <paramref name="baseJson"/> is what this client last loaded or wrote,
    /// <paramref name="mine"/> what it would save now, <paramref name="theirs"/> the file on disk.
    /// Returns <paramref name="mine"/> unchanged when there is nothing to merge or any input
    /// isn't a JSON object (a broken file is never merged into a save).
    /// </summary>
    public static string Merge(string? baseJson, string mine, string? theirs)
    {
        if (string.IsNullOrEmpty(baseJson) || string.IsNullOrEmpty(theirs)) return mine;
        if (string.Equals(theirs, baseJson, StringComparison.Ordinal)) return mine;   // nobody else saved
        if (string.Equals(theirs, mine, StringComparison.Ordinal)) return mine;

        JsonObject? b = ParseObject(baseJson);
        JsonObject? m = ParseObject(mine);
        JsonObject? t = ParseObject(theirs);
        if (b == null || m == null || t == null) return mine;

        // Keys in the order this client writes them, then any only the other client has.
        var keys = new List<string>();
        foreach (var kv in m) keys.Add(kv.Key);
        foreach (var kv in t) if (!m.ContainsKey(kv.Key)) keys.Add(kv.Key);
        foreach (var kv in b) if (!m.ContainsKey(kv.Key) && !t.ContainsKey(kv.Key)) keys.Add(kv.Key);

        var merged = new JsonObject();
        foreach (string key in keys)
        {
            string? mineText = TextOf(m, key);
            bool changedHere = !string.Equals(mineText, TextOf(b, key), StringComparison.Ordinal);
            // Changed here: this client's value (absent = removed here). Else the file's.
            JsonObject from = changedHere ? m : t;
            if (from.TryGetPropertyValue(key, out JsonNode? node))
                merged[key] = node?.DeepClone();
        }
        return merged.ToJsonString();
    }

    private static JsonObject? ParseObject(string json)
    {
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    /// <summary>The setting's JSON text (null when absent): two equal values compare equal.</summary>
    private static string? TextOf(JsonObject obj, string key)
        => obj.TryGetPropertyValue(key, out JsonNode? node) ? (node?.ToJsonString() ?? "null") : null;
}
