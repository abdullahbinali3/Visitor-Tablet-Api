using VisitorTabletAPITemplate.FileStorage.Enums;

namespace VisitorTabletAPITemplate.FileStorage.Models
{
    public sealed class DeletedAttachmentFile
    {
        public Guid Id { get; set; }
        public FileStorageRelatedObjectType RelatedObjectType { get; set; }
        public Guid? RelatedObjectId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string FileUrl { get; set; } = default!;
    }
}
