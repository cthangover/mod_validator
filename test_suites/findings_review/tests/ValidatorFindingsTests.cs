namespace Cthangover.Validator.FindingsReview
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using Cthangover.Core.Games;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Providers;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Tools.EntityEditor;

    /// <summary>
    /// Fresh checks of compiler-source parity, schema declarations and active provider boundaries, including strict negative reports.
    /// </summary>
    public sealed class ValidatorFindingsTests
    {
        /// <summary>Declared runtime patterns exclude test sources; real conflicts and compiler fallback remain strict.</summary>
        [ModTest("validator_findings/01_declared_sources_and_runtime_conflicts")]
        public static void ADeclaredSources(ModTestContext test)
        {
            var files = new Dictionary<string, string>
            {
                ["src/nested/runtime.cs"] = "class Runtime { void M() { p.GetInt(\"Count\"); p.SetInt(\"Count\", 1); } }",
                ["tests/inactive.cs"] = "class Inactive { void M() { p.SetString(\"Count\", \"1\"); p.GetInt(\"InactiveOnly\"); } }"
            };
            var mod = Mod("source_probe", files, "src/**/*.cs");
            var clean = Context(mod);
            new CSharpPropertyStrategy().Validate(clean);
            ModAssert.Equal(0, Errors(clean).Count, Describe(clean));
            files["src/conflict.cs"] = "class Conflict { void M() { p.SetString(\"Count\", \"1\"); } }";
            var conflict = Context(mod);
            new CSharpPropertyStrategy().Validate(conflict);
            Require(conflict, "TYPE_CONFLICT", null);
            ModAssert.Equal(1, Errors(conflict).Count, Describe(conflict));
            var active = Context(Mod("active_suite", files, "tests/*.cs"));
            new CSharpPropertyStrategy().Validate(active);
            Require(active, "READ_UNDECLARED", "tests/inactive.cs");
            var dataOnly = Context(Mod("data_only", files));
            new CSharpPropertyStrategy().Validate(dataOnly);
            ModAssert.Equal(0, dataOnly.Report.Issues.Count, "Data-only mods do not compile C#");
            var fallback = Context(Mod("compiler_fallback", files, "missing/*.cs"));
            new CSharpPropertyStrategy().Validate(fallback);
            Require(fallback, "TYPE_CONFLICT", null);
            Require(fallback, "READ_UNDECLARED", "tests/inactive.cs");
            Save(test, "compiler-fallback.json", fallback.Report);
            Save(test, "sources.json", conflict.Report);
            Save(test, "active-suite.json", active.Report);
        }

        /// <summary>Explicit Values keys resolve absent-data reads while unknown keys, types and broken schemas remain errors.</summary>
        [ModTest("validator_findings/02_values_schema_keys_and_malformed_contracts")]
        public static void BSchemaKeys(ModTestContext test)
        {
            var files = new Dictionary<string, string>
            {
                ["schemas/actions.json"] = "{\"Kinds\":[{\"Kind\":\"action\",\"Fields\":["
                    + "{\"Key\":\"Values\",\"Type\":\"values\",\"Of\":[{\"Key\":\"Formula\",\"Type\":\"string\"}]}]}]}",
                ["src/reader.cs"] = "class Reader { void M() { p.GetString(\"Formula\"); p.GetInt(\"UnknownKey\"); "
                    + "p.GetInt(\"Values\"); p.GetInt(\"Conflict\"); p.SetString(\"Conflict\", \"bad\"); } }"
            };
            var context = Context(Mod("schema_probe", files, "src/**/*.cs"));
            new CSharpPropertyStrategy().Validate(context);
            ModAssert.True(context.Graph.IsKeyDeclared("Formula"), "Explicit Values.Of declares a key without fabricated action data");
            ModAssert.False(context.Graph.IsKeyDeclared("Values"), "The container field is not a property-bag key");
            ModAssert.Equal(3, Errors(context).Count, Describe(context));
            Require(context, "TYPE_CONFLICT", "src/reader.cs");
            files["schemas/broken.json"] = "{broken";
            var malformed = Context(Mod("schema_probe", files, "src/**/*.cs"));
            new SchemaValidationStrategy().Validate(malformed);
            Require(malformed, "SCHEMA_PARSE_ERROR", null);
            Save(test, "schema-keys.json", context.Report);
            Save(test, "malformed-schema.json", malformed.Report);
        }

        /// <summary>
        /// Inactive discovery hides the preserved fixture; explicit provider selection still reports its broken reference.
        /// </summary>
        [ModTest("validator_findings/03_inactive_and_selected_fixture_provider_boundary")]
        public static void CFixtureBoundary(ModTestContext test)
        {
            var baseMod = ModManager.Instance.Mods["world_stream3d_base"];
            ModAssert.False(baseMod.FileProvider.FileExists("worlds3d/unbound.json"),
                "Negative fixture is absent from production catalogue");
            ModAssert.False(ModFileReader.ListFiles(NestedFolders.Visible(baseMod), "", true)
                    .Contains("test_suites/editor_fixture/worlds3d/unbound.json"),
                "Inactive suite is excluded from parent discovery; direct explicit provider reads remain available");
            using var selected = new SubdirModFileProvider(
                baseMod.FileProvider, "test_suites/editor_fixture", "world_stream3d_base__editor_fixture");
            var text = selected.ReadFileText("worlds3d/unbound.json");
            ModAssert.NotNull(text, "Explicit selection retains the original malformed-reference fixture");
            using var document = JsonDocument.Parse(text);
            ModAssert.Equal("world_stream3d/unbound", document.RootElement.GetProperty("Items")[0].GetProperty("Location").GetString());
            var fixture = new ModInfo
            {
                Id = selected.Mod,
                FileProvider = selected,
                Manifest = new ModManifest()
            };
            var context = Context(baseMod, fixture);
            context.Graph.AddDeclaration("location3d", "known/location", "known_owner", "locations/known.json", 0);
            new SchemaValidationStrategy().Validate(context);
            new ReferenceStrategy().Validate(context);
            Require(context, "REF_NOT_FOUND", "worlds3d/unbound.json");
            ModAssert.Equal(1, Errors(context).Count, Describe(context));
            Save(test, "selected-negative-fixture.json", context.Report);
        }

        /// <summary>The editor names its engine provider directly; dependency insertion preserves presence and cycle rules.</summary>
        [ModTest("validator_findings/04_document_dependency_without_dummy_resource")]
        public static void DDocumentProvider(ModTestContext test)
        {
            if (!GameEnvironment.IsRuntime)
                throw new InvalidOperationException("The document-provider window contract requires the native test runtime");

            var window = new FindingsProfileWindow();
            try
            {
                var providers = new List<string>(window.DocumentProviders);
                ModAssert.Equal(1, providers.Count);
                ModAssert.Equal("world_stream3d_base", providers[0]);
                var outcome = ModDependencies.Insert(
                    "{\"id\":\"author_world\",\"depends\":[]}", providers[0], NoDependencies, out var changed);
                ModAssert.True(outcome == ModDependencyOutcome.Added);
                using var parsed = JsonDocument.Parse(changed);
                ModAssert.Equal("author_world", parsed.RootElement.GetProperty("id").GetString());
                ModAssert.Equal(1, parsed.RootElement.GetProperty("depends").GetArrayLength());
                ModAssert.Equal("world_stream3d_base>=0.1.0", parsed.RootElement.GetProperty("depends")[0].GetString());
                ModAssert.True(ModDependencies.Insert(changed, providers[0], NoDependencies, out var unchanged)
                    == ModDependencyOutcome.Present);
                ModAssert.Null(unchanged);
                ModAssert.True(ModDependencies.Insert("{\"id\":\"author_world\"}", providers[0], CycleDependencies, out var refused)
                    == ModDependencyOutcome.Cycle);
                ModAssert.Null(refused);
                File.WriteAllText(Path.Combine(test.SandboxDir, "document-provider.json"), changed);
            }
            finally
            {
                window.Free();
            }
        }

        /// <summary>Analyzes the actual selected mod set with its registered, enabled strategies; no game-scene lifecycle claim.</summary>
        [ModTest("validator_findings/05_current_selected_game_report", TimeoutSeconds = 180)]
        public static void ESelectedGame(ModTestContext test)
        {
            ModAssert.Equal("ognishchanin", ActiveGame.Id);
            ModAssert.True(ValidatorSettings.ParseCSharp, "C# validation must be enabled rather than bypassed");
            ModAssert.True(ValidatorSettings.ParseScenarios, "Scenario validation remains enabled");
            var ids = new HashSet<string>();
            foreach (var strategy in ValidationStrategyRegistry.GetStrategies())
                ids.Add(strategy.Id);
            foreach (var id in new[] { "csharp_properties", "json_entities", "schemas", "references" })
                ModAssert.True(ids.Contains(id), id);
            var mods = new List<IModInfo>();
            var registry = ModRegistry.Instance;
            foreach (var modId in registry.GetOrderedModIds())
            {
                if (registry.Mods.TryGetValue(modId, out var info) && info != null)
                    mods.Add(info);
            }
            ModAssert.True(mods.Count > 1);
            ModAssert.False(ModManager.Instance.Mods.ContainsKey("world_stream3d_base__editor_fixture"));
            ValidatorService.WarmUp();
            var report = new ValidationReport();
            foreach (var mod in mods)
            {
                report.Mods.Add(new ValidationModStatus
                {
                    Id = mod.Id,
                    Version = mod.Version,
                    Status = "ok"
                });
            }
            var context = ValidatorService.Analyze(mods, report, test.CancellationToken);
            Save(test, "selected-game-validation.json", report);
            ModAssert.Equal(0, Errors(context).Count, Describe(context));
        }

        private static ModInfo Mod(string id, Dictionary<string, string> files, params string[] sources)
        {
            return new ModInfo
            {
                Id = id,
                FileProvider = new FindingsMemoryProvider(id, files),
                Manifest = new ModManifest
                {
                    Sources = new List<string>(sources)
                }
            };
        }

        private static ValidationContext Context(params IModInfo[] mods)
        {
            return new ValidationContext(mods, new ValidationReport(), new ValidationGraph(), CancellationToken.None);
        }

        private static List<ValidationIssue> Errors(ValidationContext context)
        {
            var result = new List<ValidationIssue>();
            foreach (var issue in context.Report.Issues)
            {
                if (issue.Severity == ValidationSeverity.Error)
                    result.Add(issue);
            }
            return result;
        }
        private static string Describe(ValidationContext context)
        {
            return JsonSerializer.Serialize(context.Report);
        }

        private static void Require(ValidationContext context, string code, string file)
        {
            foreach (var issue in context.Report.Issues)
            {
                if (issue.Severity == ValidationSeverity.Error && issue.Code == code && (file == null || issue.File == file))
                    return;
            }
            ModAssert.True(false, code + " " + file + " " + Describe(context));
        }
        private static void Save(ModTestContext test, string filename, ValidationReport report)
        {
            report.Summary = new ValidationSummary();
            foreach (var issue in report.Issues)
            {
                if (issue.Severity == ValidationSeverity.Error)
                    report.Summary.Errors++;
                else if (issue.Severity == ValidationSeverity.Warning)
                    report.Summary.Warnings++;
                else
                    report.Summary.Infos++;
            }
            var json = JsonSerializer.Serialize(report);
            var restored = JsonSerializer.Deserialize<ValidationReport>(json);
            ModAssert.Equal(json, JsonSerializer.Serialize(restored), "Every report field survives the JSON round-trip");
            File.WriteAllText(Path.Combine(test.SandboxDir, filename), json);
        }
        private static IEnumerable<string> NoDependencies(string id)
        {
            return Array.Empty<string>();
        }

        private static IEnumerable<string> CycleDependencies(string id)
        {
            return new[] { "author_world" };
        }
    }
}
