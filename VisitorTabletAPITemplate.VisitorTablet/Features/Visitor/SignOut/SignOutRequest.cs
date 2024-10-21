namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.SignOut
{
    public sealed class SignOutRequest
    {
        public Guid HostUid { get; set; }
        public List<Guid> Uid { get; set; }
        public DateTime SignOutDate { get; set; }
    }
}
