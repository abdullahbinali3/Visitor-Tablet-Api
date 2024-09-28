namespace VisitorTabletAPITemplate.FileStorage.Features.GetFileStorageLogByLogId
{
    public sealed class GetFileStorageLogByLogIdRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? LogId { get; set; }
    }
}
