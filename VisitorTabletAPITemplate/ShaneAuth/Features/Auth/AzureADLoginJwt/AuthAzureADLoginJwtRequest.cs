namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.AzureADLoginJwt
{
    public sealed class AuthAzureADLoginJwtRequest
    {
        public Guid? OrganizationId { get; set; }
        public string? AuthorizationCode { get; set; }
        public string? UserAgentBrowserName { get; set; }
        public string? UserAgentOsName { get; set; }
        public string? UserAgentDeviceInfo { get; set; }
    }
}
