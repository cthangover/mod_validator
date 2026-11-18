namespace Cthangover.Validator
{
    using System;

    /// <summary>
    /// Explicit Values schema entries declare property-bag keys even when the selected game has no instance of that action.
    /// </summary>
    internal static class ValidationSchemaPropertyKeys
    {
        internal static void Declare(ValidationContext context)
        {
            foreach (var kind in ValidationSchemas.Of(context).Kinds.Values)
            {
                if (kind.Fields == null)
                    continue;

                foreach (var field in kind.Fields)
                {
                    if (!string.Equals(field?.Type, "values", StringComparison.OrdinalIgnoreCase) || field.Of == null)
                        continue;

                    foreach (var value in field.Of)
                        context.Graph.AddDeclaredKey(value?.Key);
                }
            }
        }
    }
}
