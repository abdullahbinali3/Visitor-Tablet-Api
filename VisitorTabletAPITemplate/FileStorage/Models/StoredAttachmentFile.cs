using VisitorTabletAPITemplate.FileStorage.Enums;

namespace VisitorTabletAPITemplate.FileStorage.Models
{
    public class StoredAttachmentFile
    {
        public Guid Id { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public FileStorageRelatedObjectType RelatedObjectType { get; set; }
        public Guid? RelatedObjectId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string FileName { get; set; } = default!;
        public string? MimeType { get; set; }
        public string FileUrl { get; set; } = default!;
        public int FileSizeBytes { get; set; }
    }
}
