namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.RegisterAzureAD.CheckRegisterAzureAD
{
    public sealed class AuthCheckRegisterAzureADRequest
    {
        public Guid? AzureTenantId { get; set; }
        public Guid? AzureObjectId { get; set; }
        public string? Token { get; set; }
    }
}
