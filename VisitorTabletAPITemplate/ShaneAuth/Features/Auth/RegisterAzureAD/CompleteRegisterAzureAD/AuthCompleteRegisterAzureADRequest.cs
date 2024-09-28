namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.RegisterAzureAD.CompleteRegisterAzureAD
{
    public sealed class AuthCompleteRegisterAzureADRequest
    {
        public Guid? AzureTenantId { get; set; }
        public Guid? AzureObjectId { get; set; }
        public string? Token { get; set; }
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public Guid? RegionId { get; set; }
        public Guid? BuildingId { get; set; }
        public Guid? FunctionId { get; set; }
    }
}
