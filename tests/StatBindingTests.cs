namespace Cthangover.Validator.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Providers;
    using Cthangover.Core.Mods.Testing;

    /// <summary>A character's own field that a bound stat replaces is an error (plan C3).</summary>
    public class StatBindingTests
    {
        private const string Stats = @"{""Kind"":""stat"",""Items"":[
              {""Id"":""probe_hp"",""Formula"":""12 + vit * 3"",""Binds"":""health""},
              {""Id"":""probe_atk"",""Formula"":""str"",""Binds"":""attack""},
              {""Id"":""probe_str"",""Default"":5}]}";

        [ModTest("stat_bindings/field_replaced_by_bound_stat_is_error")]
        static void FieldReplacedByBoundStat(ModTestContext ctx)
        {
            var root = Path.Combine(Path.GetTempPath(), "validator_binds_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "stats_owner/stats"));
                Directory.CreateDirectory(Path.Combine(root, "game/characters"));
                File.WriteAllText(Path.Combine(root, "stats_owner/stats/probe.json"), Stats);
                File.WriteAllText(Path.Combine(root, "game/characters/probe.json"),
                    @"{""Kind"":""character"",""Items"":[
                      {""Id"":""probe/with_stats_and_health"",""Stats"":{""probe_str"":4},""Health"":16,""Attack"":3},
                      {""Id"":""probe/with_stats_only"",""Stats"":{""probe_str"":4}},
                      {""Id"":""probe/health_without_stats"",""Health"":9},
                      {""Id"":""probe/archetype_and_health"",""Archetype"":""probe/warrior"",""Health"":7},
                      {""Id"":""probe/empty_stats_and_health"",""Stats"":{},""Health"":5}]}");
                using var owner = new FolderModFileProvider(Path.Combine(root, "stats_owner"), "stats_owner");
                using var game = new FolderModFileProvider(Path.Combine(root, "game"), "game");
                var mods = new IModInfo[] { new ModInfo { Id = "stats_owner", FileProvider = owner }, new ModInfo { Id = "game", FileProvider = game } };
                var context = new ValidationContext(mods, new ValidationReport(), new ValidationGraph(), CancellationToken.None);
                new StatBindingStrategy().Validate(context);

                var issues = context.Report.Issues.Where(i => i.Code == "STAT_FIELD_OVERRIDDEN").ToList();
                string All() => string.Join(" | ", context.Report.Issues.Select(i => i.Code + " " + i.Message));
                ModAssert.Equal(3, issues.Count, "Health and Attack of the first character, Health of the archetype one: " + All());
                ModAssert.True(issues.All(i => i.Severity == ValidationSeverity.Error && i.Mod == "game" && i.File == "characters/probe.json"),
                    "error, owned by the character's mod and file: " + All());
                ModAssert.True(issues.Any(i => i.Message.Contains("'Health'") && i.Message.Contains("probe_hp") && i.Message.Contains("probe/with_stats_and_health")),
                    "names the field, the stat and the character: " + All());
                ModAssert.True(issues.Any(i => i.Message.Contains("'Attack'") && i.Message.Contains("probe/with_stats_and_health")),
                    "Attack of the first character is replaced by its stat too: " + All());
                ModAssert.False(issues.Any(i => i.Message.Contains("probe/with_stats_only")), "no field, no finding: " + All());
                ModAssert.False(issues.Any(i => i.Message.Contains("probe/health_without_stats")), "a character without stats keeps its numbers: " + All());
                ModAssert.True(issues.Any(i => i.Message.Contains("probe/archetype_and_health")), "an archetype makes it take part: " + All());
                ModAssert.False(issues.Any(i => i.Message.Contains("probe/empty_stats_and_health")), "empty Stats do not count: " + All());
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [ModTest("stat_bindings/character_patch_replaces_bound_field_is_error")]
        static void CharacterPatchReplacesBoundField(ModTestContext ctx)
        {
            var root = Path.Combine(Path.GetTempPath(), "validator_patch_binds_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "stats_owner/stats"));
                Directory.CreateDirectory(Path.Combine(root, "game/characters"));
                Directory.CreateDirectory(Path.Combine(root, "game/patches"));
                File.WriteAllText(Path.Combine(root, "stats_owner/stats/probe.json"), Stats);
                File.WriteAllText(Path.Combine(root, "game/characters/probe.json"),
                    @"{""Kind"":""character"",""Items"":[
                      {""Id"":""probe/plain"",""Health"":9},
                      {""Id"":""probe/with_stats"",""Stats"":{""probe_str"":4}}]}");
                // Foreign patches of the same mod: one gives stats and Health to a plain character, one adds Attack to
                // a character with stats, one touches a character that takes no part in stats, one is a wildcard.
                File.WriteAllText(Path.Combine(root, "game/patches/character.json"),
                    @"{""Items"":[
                      {""Id"":""probe/plain"",""Stats"":{""probe_str"":2},""Health"":12},
                      {""Id"":""probe/with_stats"",""Attack"":5},
                      {""Id"":""probe/none"",""Defence"":3},
                      {""Id"":""*"",""Health"":1}]}");
                using var owner = new FolderModFileProvider(Path.Combine(root, "stats_owner"), "stats_owner");
                using var game = new FolderModFileProvider(Path.Combine(root, "game"), "game");
                var mods = new IModInfo[] { new ModInfo { Id = "stats_owner", FileProvider = owner }, new ModInfo { Id = "game", FileProvider = game } };
                var context = new ValidationContext(mods, new ValidationReport(), new ValidationGraph(), CancellationToken.None);
                new StatBindingStrategy().Validate(context);

                var issues = context.Report.Issues.Where(i => i.Code == "STAT_FIELD_OVERRIDDEN").ToList();
                string All() => string.Join(" | ", context.Report.Issues.Select(i => i.Code + " " + i.Message));
                ModAssert.Equal(2, issues.Count, "the patch's Health (gives stats) and Attack (character with stats): " + All());
                ModAssert.True(issues.All(i => i.Severity == ValidationSeverity.Error && i.Mod == "game" && i.File == "patches/character.json"),
                    "error, owned by the patching mod and its patch file: " + All());
                ModAssert.True(issues.Any(i => i.Message.Contains("patch of character 'probe/plain'") && i.Message.Contains("'Health'")),
                    "a patch that gives stats to a plain character is checked: " + All());
                ModAssert.True(issues.Any(i => i.Message.Contains("patch of character 'probe/with_stats'") && i.Message.Contains("'Attack'")),
                    "a patch on a character with stats of its own data is checked: " + All());
                ModAssert.False(issues.Any(i => i.Message.Contains("probe/none")), "a character without stats is not touched: " + All());
                ModAssert.False(issues.Any(i => i.Message.Contains("'*'") || i.Message.Contains("patch of character '*'")), "a wildcard patch is skipped: " + All());
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
