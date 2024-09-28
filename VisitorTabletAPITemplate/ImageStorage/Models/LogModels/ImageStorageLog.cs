namespace VisitorTabletAPITemplate.ImageStorage.Models.LogModels
{
    public sealed class ImageStorageLog
    {
        public Guid id { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public Guid UpdatedByUid { get; set; }
        public string? UpdatedByDisplayName { get; set; }
        public string? UpdatedByIpAddress { get; set; }
        public string LogDescription { get; set; } = default!;
        public Guid ImageStorageId { get; set; }
        public byte RelatedObjectType { get; set; }
        public Guid? RelatedObjectId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string? MimeType { get; set; }
        public string FileUrl { get; set; } = default!;
        public int FileSizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Deleted { get; set; }
        public bool? OldDeleted { get; set; }
        public string LogAction { get; set; } = default!;
        public string? CascadeFrom { get; set; }
        public Guid? CascadeLogId { get; set; }
    }
}
