namespace VisitorTabletAPITemplate.Features.Buildings.UpdateBuildingCheckInQRCode
{
    public sealed class UpdateBuildingCheckInQRCodeRequest
    {
        public Guid? id { get; set; }
        public Guid? OrganizationId { get; set; }
        public string? CheckInQRCode { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
