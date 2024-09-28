namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.DeleteUser
{
    public sealed class MasterDeleteUserRequest
    {
        public Guid? Uid { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
