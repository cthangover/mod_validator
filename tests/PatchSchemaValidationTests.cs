namespace Cthangover.Validator.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Providers;
    using Cthangover.Core.Mods.Testing;

    /// <summary>The validator reads <c>patches/</c> against the schemas of the patched kind (plan A4).</summary>
    public class PatchSchemaValidationTests
    {
        private const string Schema = @"{""Kinds"":[{""Kind"":""probe_item"",""Fields"":[
              {""Key"":""Name"",""Type"":""locale""},{""Key"":""Cost"",""Type"":""int""},{""Key"":""Tags"",""Type"":""list""}]}]}";

        [ModTest("patches/typo_type_and_add_on_scalar_reported_with_their_file")]
        static void PatchFilesAreChecked(ModTestContext ctx)
        {
            var root = Path.Combine(Path.GetTempPath(), "validator_patches_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "owner/schemas"));
                Directory.CreateDirectory(Path.Combine(root, "owner/items"));
                Directory.CreateDirectory(Path.Combine(root, "patcher/patches/probe_item"));
                File.WriteAllText(Path.Combine(root, "owner/schemas/probe.json"), Schema);
                File.WriteAllText(Path.Combine(root, "owner/items/probe.json"),
                    @"{""Kind"":""probe_item"",""Items"":[{""Id"":""probe/a"",""Cost"":1,""Tags"":[""x""]}]}");
                File.WriteAllText(Path.Combine(root, "patcher/patches/probe_item.json"),
                    @"{""Items"":[{""Id"":""probe/a"",""Cots"":2},{""Id"":""probe/a"",""Cost"":""many""},{""Id"":""*"",""Name$add"":[""broken""]},{""Id"":""probe/a"",""Tags$add"":[""y""]}]}");
                File.WriteAllText(Path.Combine(root, "patcher/patches/probe_item/more.json"),
                    @"{""Items"":[{""Id"":""probe/a"",""Tags"":""y""}]}");
                using var owner = new FolderModFileProvider(Path.Combine(root, "owner"), "owner");
                using var patcher = new FolderModFileProvider(Path.Combine(root, "patcher"), "patcher");
                var mods = new IModInfo[] { new ModInfo { Id = "owner", FileProvider = owner }, new ModInfo { Id = "patcher", FileProvider = patcher } };
                var context = new ValidationContext(mods, new ValidationReport(), new ValidationGraph(), CancellationToken.None);
                new SchemaValidationStrategy().Validate(context);

                var issues = context.Report.Issues.Where(i => i.Mod == "patcher").ToList();
                string Of(string code) => string.Join(" | ", issues.Where(i => i.Code == code).Select(i => $"{i.Severity} {i.File}: {i.Message}"));
                ModAssert.True(issues.Any(i => i.Code == "SCHEMA_PATCH_UNKNOWN_FIELD" && i.Severity == ValidationSeverity.Warning
                    && i.File == "patches/probe_item.json" && i.Message.Contains("did you mean 'Cost'")), "typo: " + Of("SCHEMA_PATCH_UNKNOWN_FIELD"));
                ModAssert.True(issues.Any(i => i.Code == "SCHEMA_BAD_VALUE" && i.Severity == ValidationSeverity.Error), "text for an int: " + Of("SCHEMA_BAD_VALUE"));
                ModAssert.True(issues.Any(i => i.Code == "SCHEMA_PATCH_OP_ON_SCALAR" && i.Severity == ValidationSeverity.Error && i.Message.Contains("Name$add")),
                    "$add on a scalar: " + Of("SCHEMA_PATCH_OP_ON_SCALAR"));
                ModAssert.True(issues.Any(i => i.Code == "SCHEMA_PATCH_BAD_TYPE" && i.File == "patches/probe_item/more.json"),
                    "file inside patches/<kind>/: " + Of("SCHEMA_PATCH_BAD_TYPE"));
                ModAssert.Equal(4, issues.Count(i => i.Code.StartsWith("SCHEMA_", StringComparison.Ordinal)),
                    "the valid Tags$add is clean: " + string.Join(" | ", issues.Select(i => i.Code + " " + i.Message)));
                ModAssert.False(context.Report.Issues.Any(i => i.Mod == "owner" && i.Code.StartsWith("SCHEMA_PATCH", StringComparison.Ordinal)), "base data is not a patch");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
