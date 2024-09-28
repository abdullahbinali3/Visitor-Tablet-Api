using VisitorTabletAPITemplate.ImageStorage.Enums;

namespace VisitorTabletAPITemplate.ImageStorage.Models
{
    public sealed class StoredImageFile
    {
        public Guid Id { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public ImageStorageRelatedObjectType RelatedObjectType { get; set; }
        public Guid? RelatedObjectId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string MimeType { get; set; } = default!;
        public string FileUrl { get; set; } = default!;
        public int FileSizeBytes { get; set; }
        public short Width { get; set; }
        public short Height { get; set; }
    }
}
