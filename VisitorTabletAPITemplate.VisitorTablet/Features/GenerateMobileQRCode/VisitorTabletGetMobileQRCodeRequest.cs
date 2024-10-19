namespace VisitorTabletAPITemplate.VisitorTablet.Features.GenerateMobileQRCode
{
    public sealed class VisitorTabletGetMobileQRCodeRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
    }
}
