namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.SignOut
{
    public sealed class SignOutRequest
    {
        public Guid WorkplaceVisitId { get; set; }
        public List<Guid> Uid { get; set; }
        public DateTime? SignOutDateUtc { get; set; }
    }
}
