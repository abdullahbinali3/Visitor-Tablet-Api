namespace VisitorTabletAPITemplate.FileStorage.Models.LogModels
{
    public sealed class FileStorageLog
    {
        public Guid id { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public Guid UpdatedByUid { get; set; }
        public string? UpdatedByDisplayName { get; set; }
        public string? UpdatedByIpAddress { get; set; }
        public string LogDescription { get; set; } = default!;
        public Guid FileStorageId { get; set; }
        public byte RelatedObjectType { get; set; }
        public Guid? RelatedObjectId { get; set; }
        public Guid? OrganizationId { get; set; }
        public string FileName { get; set; } = default!;
        public string? MimeType { get; set; }
        public string FileUrl { get; set; } = default!;
        public int FileSizeBytes { get; set; }
        public bool Deleted { get; set; }
        public bool? OldDeleted { get; set; }
        public string LogAction { get; set; } = default!;
        public string? CascadeFrom { get; set; }
        public Guid? CascadeLogId { get; set; }
    }
}
