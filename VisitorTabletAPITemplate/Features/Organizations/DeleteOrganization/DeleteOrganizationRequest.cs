namespace VisitorTabletAPITemplate.Features.Organizations.DeleteOrganization
{
    public sealed class DeleteOrganizationRequest
    {
        public Guid? id { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
