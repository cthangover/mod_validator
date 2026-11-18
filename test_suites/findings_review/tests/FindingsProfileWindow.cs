namespace Cthangover.Validator.FindingsReview
{
    using System.Collections.Generic;
    using Cthangover.Tools.WorldStream3d;

    /// <summary>Exposes the editor's protected provider contract without saving fabricated world content.</summary>
    public partial class FindingsProfileWindow : StreamProfileEditorWindow
    {
        /// <summary>The same document-level providers consumed by the production Save flow.</summary>
        public IEnumerable<string> DocumentProviders => ExtraDocumentProviders();
    }
}
