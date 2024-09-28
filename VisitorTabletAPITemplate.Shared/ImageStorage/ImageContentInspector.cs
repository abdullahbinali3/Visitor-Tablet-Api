using MimeDetective;
using MimeDetective.Storage;
using System.Collections.Immutable;

namespace VisitorTabletAPITemplate.ImageStorage
{
    public sealed class ImageContentInspector
    {
        public static readonly ImageContentInspector Instance = new ImageContentInspector();
        public readonly ContentInspector ContentInspector;

        private ImageContentInspector()
        {
            ImmutableArray<Definition> AllDefinitions = new MimeDetective.Definitions.ExhaustiveBuilder()
            {
                UsageType = MimeDetective.Definitions.Licensing.UsageType.PersonalNonCommercial
            }.Build();

            // SixLabors.ImageSharp supported image formats:
            // https://docs.sixlabors.com/articles/imagesharp/imageformats.html
            // As well as SVG.
            ImmutableHashSet<string> Extensions = new[] {
                "bmp", "gif", "jpeg", "jpg", "pbm", "png", "tiff", "tga", "webp", "svg"
            }.ToImmutableHashSet(StringComparer.InvariantCultureIgnoreCase);

            ImmutableArray<Definition> ScopedDefinitions = AllDefinitions
                .ScopeExtensions(Extensions) // Limit results to only the extensions provided
                .TrimMeta() // If you don't care about the meta information (definition author, creation date, etc)
                .TrimDescription() // If you don't care about the description
                .ToImmutableArray();

            ContentInspector = new ContentInspectorBuilder()
            {
                Definitions = ScopedDefinitions,
            }.Build();
        }
    }
}
