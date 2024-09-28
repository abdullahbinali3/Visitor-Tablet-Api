namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.RevokeDisable2fa
{
    public sealed class AuthRevokeDisable2faRequest
    {
        public Guid? Uid { get; set; }
        public Guid? Token { get; set; }
    }
}
