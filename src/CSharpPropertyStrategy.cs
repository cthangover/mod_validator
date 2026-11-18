namespace Cthangover.Validator
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Compilation;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    /// <summary>
    /// Phase: Contracts. Parses every mod's declared C# sources with Roslyn and
    /// extracts typed property accesses (<c>GetInt("Turn")</c>,
    /// <c>SetFloat("Attack", ...)</c>) from <c>DataBlob</c>, <c>PropertyData</c>
    /// and <c>ActionCharacter</c>, which share the same method names. The
    /// analysis is syntactic on purpose: it needs no metadata references, so it
    /// runs on the background thread and never triggers assembly loading.
    ///
    /// Findings: non-literal keys (<c>DYNAMIC_KEY</c>, info), the same key used
    /// with different value types (<c>TYPE_CONFLICT</c>, error), reads of keys
    /// that are neither declared in JSON nor written anywhere
    /// (<c>READ_UNDECLARED</c> / <c>READ_UNDECLARED_WITH_DEFAULT</c>), and writes
    /// that no code ever reads (<c>WRITE_UNUSED</c>, info).
    /// </summary>
    public class CSharpPropertyStrategy : IValidationStrategy
    {
        private static readonly Dictionary<string, string> TypeByMethod = new(StringComparer.Ordinal)
        {
            { "GetInt", "int" },
            { "SetInt", "int" },
            { "GetFloat", "float" },
            { "SetFloat", "float" },
            { "GetString", "string" },
            { "SetString", "string" },
            { "GetStr", "string" },
            { "SetStr", "string" },
            { "GetBool", "bool" },
            { "SetBool", "bool" },
            { "GetLong", "long" },
            { "SetLong", "long" },
        };

        /// <summary>Uses the existing C# contract phase and registry identity.</summary>
        public string Id => "csharp_properties";
        /// <summary>Uses the existing C# contract phase and registry identity.</summary>
        public ValidationPhase Phase => ValidationPhase.Contracts;
        /// <summary>Uses the existing C# contract phase and registry identity.</summary>
        public int Order => 200;
        /// <summary>Uses the existing C# contract phase and registry identity.</summary>
        public ValidationFeature Feature => ValidationFeature.CSharp;

        /// <summary>
        /// Scans compiled mod sources for property-bag usages against persisted and explicit schema keys.
        /// Unknown reads and incompatible reader/writer types remain findings.
        /// </summary>
        public void Validate(ValidationContext context)
        {
            ValidationSchemaPropertyKeys.Declare(context);
            var usages = new List<CodePropertyUsage>();
            var literalCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var mod in context.Mods)
            {
                context.ThrowIfCancellationRequested();

                foreach (var file in CollectSources(mod, context.CancellationToken))
                {
                    context.ThrowIfCancellationRequested();
                    ParseSource(mod.Id, file.Path, file.Source, usages, literalCounts);
                }
            }

            ReportDynamicKeys(context, usages);
            ReportTypeConflicts(context, usages);
            ReportUndeclaredReads(context, usages);
            ReportUnusedWrites(context, usages, literalCounts);
        }

        private static List<(string Path, string Source)> CollectSources(IModInfo mod, CancellationToken cancellation)
        {
            // Match the compiler's declared source boundary, including recursive patterns,
            // provider-relative paths, mounted suites and the compiler's fallback policy.
            if (mod is not ModInfo info || info.FileProvider == null || info.Manifest?.Sources?.Count is not > 0)
                return new List<(string, string)>();

            return ModSourceCollector.CollectSourceFiles(info, cancellation);
        }

        private static void ParseSource(
            string modId,
            string path,
            string source,
            List<CodePropertyUsage> usages,
            Dictionary<string, int> literalCounts)
        {
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(), path);
            var root = tree.GetRoot();

            foreach (var token in root.DescendantTokens())
            {
                if (!token.IsKind(SyntaxKind.StringLiteralToken))
                    continue;

                var text = token.ValueText;
                literalCounts.TryGetValue(text, out var count);
                literalCounts[text] = count + 1;
            }

            foreach (var node in root.DescendantNodes())
            {
                if (node is not InvocationExpressionSyntax invocation)
                    continue;

                if (invocation.Expression is not MemberAccessExpressionSyntax member)
                    continue;

                var method = member.Name.Identifier.ValueText;
                if (!TypeByMethod.TryGetValue(method, out var type))
                    continue;

                var arguments = invocation.ArgumentList.Arguments;
                if (arguments.Count == 0)
                    continue;

                var key = TryGetLiteralKey(arguments[0].Expression);

                // A helper of the shape GetString(value, "Name", "ID", ...) has
                // nothing to do with property bags, so it must not surface as a
                // dynamic-key finding.
                if (key == null && HasOnlyStringLiteralTail(arguments))
                    continue;

                usages.Add(new CodePropertyUsage
                {
                    ModId = modId,
                    File = path,
                    Line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    Key = key,
                    Type = type,
                    IsWrite = method.StartsWith("Set", StringComparison.Ordinal),
                    HasDefault = arguments.Count > 1,
                    Method = method,
                });
            }
        }

        private static string TryGetLiteralKey(ExpressionSyntax expression)
        {
            if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                return literal.Token.ValueText;

            return null;
        }

        private static bool HasOnlyStringLiteralTail(SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            if (arguments.Count < 2)
                return false;

            for (var i = 1; i < arguments.Count; i++)
            {
                if (arguments[i].Expression is not LiteralExpressionSyntax literal
                    || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                    return false;
            }

            return true;
        }

        private static void ReportDynamicKeys(ValidationContext context, List<CodePropertyUsage> usages)
        {
            foreach (var usage in usages)
            {
                if (usage.Key != null)
                    continue;

                context.AddInfo("DYNAMIC_KEY",
                    $"{usage.Method}(...) uses a non-literal key; static analysis skipped",
                    usage.ModId, usage.File, usage.Line);
            }
        }

        private static void ReportTypeConflicts(ValidationContext context, List<CodePropertyUsage> usages)
        {
            var byKey = new Dictionary<string, List<CodePropertyUsage>>(StringComparer.OrdinalIgnoreCase);
            foreach (var usage in usages)
            {
                if (usage.Key == null)
                    continue;

                if (!byKey.TryGetValue(usage.Key, out var list))
                {
                    list = new List<CodePropertyUsage>();
                    byKey[usage.Key] = list;
                }

                list.Add(usage);
            }

            foreach (var entry in byKey)
            {
                var types = new List<string>();
                foreach (var usage in entry.Value)
                {
                    if (!types.Contains(usage.Type))
                        types.Add(usage.Type);
                }

                if (types.Count < 2)
                    continue;

                var first = entry.Value[0];
                var related = new List<string>();
                for (var i = 1; i < entry.Value.Count; i++)
                {
                    var other = entry.Value[i];
                    related.Add($"{other.ModId}:{other.File}:{other.Line} ({other.Type})");
                }

                context.AddError("TYPE_CONFLICT",
                    $"property '{entry.Key}' is accessed as {string.Join(", ", types)}",
                    first.ModId, first.File, first.Line,
                    "use one type for all readers and writers of this property",
                    related);
            }
        }

        private static void ReportUndeclaredReads(ValidationContext context, List<CodePropertyUsage> usages)
        {
            var writeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var usage in usages)
            {
                if (usage.IsWrite && usage.Key != null)
                    writeKeys.Add(usage.Key);
            }

            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var usage in usages)
            {
                if (usage.IsWrite || usage.Key == null)
                    continue;

                if (context.Graph.IsKeyDeclared(usage.Key) || writeKeys.Contains(usage.Key))
                    continue;

                if (!reported.Add(usage.ModId + "|" + usage.File + "|" + usage.Key))
                    continue;

                var message = $"property '{usage.Key}' is read as {usage.Type} but is never declared or written";
                var suggestion = "declare the property in JSON (Values/extra fields) or write it before reading";

                if (usage.HasDefault)
                    context.AddWarning("READ_UNDECLARED_WITH_DEFAULT", message, usage.ModId, usage.File, usage.Line, suggestion);
                else
                    context.AddError("READ_UNDECLARED", message, usage.ModId, usage.File, usage.Line, suggestion);
            }
        }

        private static void ReportUnusedWrites(
            ValidationContext context,
            List<CodePropertyUsage> usages,
            Dictionary<string, int> literalCounts)
        {
            var readKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var writeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var usage in usages)
            {
                if (usage.Key == null)
                    continue;

                if (usage.IsWrite)
                {
                    writeCounts.TryGetValue(usage.Key, out var count);
                    writeCounts[usage.Key] = count + 1;
                }
                else
                {
                    readKeys.Add(usage.Key);
                }
            }

            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var usage in usages)
            {
                if (!usage.IsWrite || usage.Key == null)
                    continue;

                if (readKeys.Contains(usage.Key))
                    continue;

                if (!reported.Add(usage.ModId + "|" + usage.Key))
                    continue;

                // A key that appears as a string literal more often than it is
                // written is probably read through a helper the syntactic pass
                // cannot follow (GetString(key, ...) with a literal argument).
                literalCounts.TryGetValue(usage.Key, out var occurrences);
                writeCounts.TryGetValue(usage.Key, out var writes);
                if (occurrences > writes)
                    continue;

                context.AddInfo("WRITE_UNUSED",
                    $"property '{usage.Key}' is written but never read",
                    usage.ModId, usage.File, usage.Line);
            }
        }
    }
}
