namespace VisitorTabletAPITemplate.ImageStorage.Features.GetImageStorageLogByLogId
{
    public sealed class GetImageStorageLogByLogIdRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? LogId { get; set; }
    }
}
