namespace Cthangover.Validator
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Utils;

    /// <summary>
    /// Phase: Reconciliation. A stat with <c>Binds</c> (for example <c>hp</c> binds
    /// <c>health</c>) writes its value into a battle attribute of every character
    /// that takes part in the stats, so the same field in the character's own
    /// template (<c>Health</c>, <c>Attack</c>, <c>Defence</c>, <c>Point</c>) is dead
    /// data: the bound stat replaces it. Reports <c>STAT_FIELD_OVERRIDDEN</c> for
    /// such a field. Character participation is read from its own data: a
    /// non-empty <c>Stats</c>, an <c>Archetype</c> or <c>Traits</c>. A character
    /// patch (<c>patches/character.json</c>) is checked the same way: it may set a
    /// bound field on a character that takes part in stats, either by its own data
    /// or by the patch that gives it stats (plan 2.3, C3).
    /// </summary>
    public class StatBindingStrategy : IValidationStrategy
    {
        private static readonly Dictionary<string, string> FieldOfBind = new(StringComparer.Ordinal)
        {
            ["health"] = "Health",
            ["attack"] = "Attack",
            ["defence"] = "Defence",
            ["point"] = "Point",
        };

        public string Id => "stat_bindings";
        public ValidationPhase Phase => ValidationPhase.Reconciliation;
        public int Order => 250;
        public ValidationFeature Feature => ValidationFeature.Json;

        /// <summary>Collects <c>Binds</c> of every loaded stat, then checks every character template against them.</summary>
        public void Validate(ValidationContext context)
        {
            var binds = new Dictionary<string, (string StatId, string Mod)>(StringComparer.Ordinal);
            foreach (var mod in context.Mods)
            {
                context.ThrowIfCancellationRequested();
                foreach (var (file, root) in Envelopes(mod, "stat"))
                {
                    using (root.Document)
                        foreach (var item in Items(root.Items))
                        {
                            var bind = JsonReadHelper.GetString(item, "Binds");
                            var statId = JsonReadHelper.GetString(item, "Id", "ID", "id");
                            if (bind == null || statId == null || !FieldOfBind.ContainsKey(bind) || binds.ContainsKey(bind))
                                continue;
                            binds[bind] = (statId, mod.Id);
                        }
                }
            }

            if (binds.Count == 0)
                return;

            var participants = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mod in context.Mods)
            {
                context.ThrowIfCancellationRequested();
                foreach (var (file, root) in Envelopes(mod, "character"))
                {
                    using (root.Document)
                        foreach (var item in Items(root.Items))
                        {
                            if (!TakesPartInStats(item))
                                continue;
                            var characterId = JsonReadHelper.GetString(item, "Id", "ID", "id") ?? "?";
                            participants.Add(characterId);
                            foreach (var (bind, owner) in binds)
                            {
                                var field = FieldOfBind[bind];
                                if (JsonReadHelper.GetProperty(item, field) == null)
                                    continue;
                                context.AddError("STAT_FIELD_OVERRIDDEN",
                                    $"character '{characterId}' has '{field}', but stat '{owner.StatId}' (mod {owner.Mod}) binds '{bind}' and replaces it",
                                    mod.Id, file, 0,
                                    $"remove '{field}' from the character; the stat supplies the value");
                            }
                        }
                }
            }

            // A patch is checked against the character it changes: the character takes part in stats by its own
            // data (participants) or by the patch itself (its Stats, Archetype or Traits). Wildcard patches are skipped.
            foreach (var mod in context.Mods)
            {
                context.ThrowIfCancellationRequested();
                foreach (var (file, item) in CharacterPatches(mod))
                {
                    var characterId = JsonReadHelper.GetString(item, "Id", "ID", "id");
                    if (string.IsNullOrEmpty(characterId) || characterId == "*")
                        continue;
                    if (!participants.Contains(characterId) && !TakesPartInStats(item))
                        continue;
                    foreach (var (bind, owner) in binds)
                    {
                        var field = FieldOfBind[bind];
                        if (JsonReadHelper.GetProperty(item, field) == null)
                            continue;
                        context.AddError("STAT_FIELD_OVERRIDDEN",
                            $"patch of character '{characterId}' sets '{field}', but stat '{owner.StatId}' (mod {owner.Mod}) binds '{bind}' and replaces it",
                            mod.Id, file, 0,
                            $"remove '{field}' from the patch; the stat supplies the value");
                    }
                }
            }
        }

        /// <summary>Items of <c>patches/character.json</c> and <c>patches/character/**.json</c> of a mod, as <c>PatchResolver</c> reads them.</summary>
        private static IEnumerable<(string File, JsonElement Item)> CharacterPatches(IModInfo mod)
        {
            var provider = NestedFolders.Visible(mod);
            if (provider == null)
                yield break;

            foreach (var file in ModFileReader.ListFiles(provider, "patches", true))
            {
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;
                var relative = file.StartsWith("patches/", StringComparison.OrdinalIgnoreCase) ? file.Substring("patches/".Length) : file;
                if (!relative.Equals("character.json", StringComparison.OrdinalIgnoreCase) && !relative.StartsWith("character/", StringComparison.OrdinalIgnoreCase))
                    continue;

                string text;
                try
                {
                    text = ModFileReader.ReadResolved(provider, file);
                }
                catch (Exception)
                {
                    continue; // include errors are reported by the JSON strategy
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(text ?? string.Empty, JsonOptionsProvider.DocumentOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                using (document)
                {
                    var items = JsonReadHelper.GetProperty(document.RootElement, "Items");
                    if (!JsonReadHelper.IsArray(items))
                        continue;
                    foreach (var item in items.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                            yield return (file, item.Clone());
                    }
                }
            }
        }

        private static bool TakesPartInStats(JsonElement item)
        {
            var stats = JsonReadHelper.GetProperty(item, "Stats");
            if (stats is { ValueKind: JsonValueKind.Object } s && s.EnumerateObject().MoveNext())
                return true;
            var archetype = JsonReadHelper.GetString(item, "Archetype");
            if (!string.IsNullOrWhiteSpace(archetype))
                return true;
            var traits = JsonReadHelper.GetProperty(item, "Traits");
            return traits is { ValueKind: JsonValueKind.Array } t && t.GetArrayLength() > 0;
        }

        private static IEnumerable<(string File, (JsonDocument Document, JsonElement Items) root)> Envelopes(IModInfo mod, string kind)
        {
            var provider = NestedFolders.Visible(mod);
            if (provider == null)
                yield break;

            foreach (var file in ModFileReader.ListFiles(provider, string.Empty, true))
            {
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || file.StartsWith("patches/", StringComparison.Ordinal)
                    || file.StartsWith("collections/", StringComparison.Ordinal) || file == "manifest.json")
                    continue;

                string text;
                try
                {
                    text = ModFileReader.ReadResolved(provider, file);
                }
                catch (Exception)
                {
                    continue; // include errors are reported by the JSON strategy
                }

                if (string.IsNullOrEmpty(text) || !text.Contains($"\"{kind}\"", StringComparison.OrdinalIgnoreCase))
                    continue;

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(text, JsonOptionsProvider.DocumentOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                var declared = JsonReadHelper.GetString(document.RootElement, "Kind");
                var items = JsonReadHelper.GetProperty(document.RootElement, "Items");
                if (!string.Equals(declared, kind, StringComparison.Ordinal) || !JsonReadHelper.IsArray(items))
                {
                    document.Dispose();
                    continue;
                }

                yield return (file, (document, items.Value));
            }
        }

        private static IEnumerable<JsonElement> Items(JsonElement items)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                    yield return item;
            }
        }
    }
}
