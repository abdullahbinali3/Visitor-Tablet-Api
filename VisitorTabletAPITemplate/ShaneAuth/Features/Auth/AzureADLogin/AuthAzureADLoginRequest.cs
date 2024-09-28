namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.AzureADLogin
{
    public sealed class AuthAzureADLoginRequest
    {
        public Guid? OrganizationId { get; set; }
        public string? AuthorizationCode { get; set; }
        public string? UserAgentBrowserName { get; set; }
        public string? UserAgentOsName { get; set; }
        public string? UserAgentDeviceInfo { get; set; }
    }
}
