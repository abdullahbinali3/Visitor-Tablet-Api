using VisitorTabletAPITemplate.ImageStorage.Enums;

namespace VisitorTabletAPITemplate.ImageStorage.Models
{
    public sealed class DeletedImageFile
    {
        public Guid Id { get; set; }
        public ImageStorageRelatedObjectType RelatedObjectType { get; set; }
        public Guid? RelatedObjectId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string FileUrl { get; set; } = default!;
    }
}
