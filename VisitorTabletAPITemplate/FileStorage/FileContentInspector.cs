using MimeDetective;
using MimeDetective.Storage;
using System.Collections.Immutable;

namespace VisitorTabletAPITemplate.FileStorage
{
    public sealed class FileContentInspector
    {
        public static readonly FileContentInspector Instance = new FileContentInspector();
        public readonly ContentInspector PdfContentInspector;
        public readonly ContentInspector VideoContentInspector;
        public readonly ContentInspector ExcelContentInspector;
        public readonly ContentInspector ContentInspector;

        private FileContentInspector()
        {
            ImmutableArray<Definition> AllDefinitions = new MimeDetective.Definitions.ExhaustiveBuilder()
            {
                UsageType = MimeDetective.Definitions.Licensing.UsageType.PersonalNonCommercial
            }.Build();

            // Generic file content inspector
            ImmutableArray<Definition> ScopedDefinitions = AllDefinitions
                .TrimMeta() // If you don't care about the meta information (definition author, creation date, etc)
                .TrimDescription() // If you don't care about the description
                .ToImmutableArray();

            ContentInspector = new ContentInspectorBuilder()
            {
                Definitions = ScopedDefinitions,
            }.Build();

            // PdfContentInspector

            // Valid file attachment formats
            ImmutableHashSet<string> PdfExtensions = new[] {
                "pdf"
            }.ToImmutableHashSet(StringComparer.InvariantCultureIgnoreCase);

            ImmutableArray<Definition> PdfScopedDefinitions = AllDefinitions
                .ScopeExtensions(PdfExtensions) // Limit results to only the extensions provided
                .TrimMeta() // If you don't care about the meta information (definition author, creation date, etc)
                .TrimDescription() // If you don't care about the description
                .ToImmutableArray();

            PdfContentInspector = new ContentInspectorBuilder()
            {
                Definitions = PdfScopedDefinitions,
            }.Build();

            // VideoContentInspector

            // Valid file attachment formats
            ImmutableHashSet<string> VideoExtensions = new[] {
                "mp4", "webm", "ogv"
            }.ToImmutableHashSet(StringComparer.InvariantCultureIgnoreCase);

            ImmutableArray<Definition> VideoScopedDefinitions = AllDefinitions
                .ScopeExtensions(VideoExtensions) // Limit results to only the extensions provided
                .TrimMeta() // If you don't care about the meta information (definition author, creation date, etc)
                .TrimDescription() // If you don't care about the description
                .ToImmutableArray();

            VideoContentInspector = new ContentInspectorBuilder()
            {
                Definitions = VideoScopedDefinitions,
            }.Build();

            // ExcelContentInspector

            // Valid file attachment formats
            ImmutableHashSet<string> ExcelExtensions = new[] {
                "xlsx"
            }.ToImmutableHashSet(StringComparer.InvariantCultureIgnoreCase);

            ImmutableArray<Definition> ExcelScopedDefinitions = AllDefinitions
                .ScopeExtensions(ExcelExtensions) // Limit results to only the extensions provided
                .TrimMeta() // If you don't care about the meta information (definition author, creation date, etc)
                .TrimDescription() // If you don't care about the description
                .ToImmutableArray();

            ExcelContentInspector = new ContentInspectorBuilder()
            {
                Definitions = ExcelScopedDefinitions,
            }.Build();
        }
    }
}
