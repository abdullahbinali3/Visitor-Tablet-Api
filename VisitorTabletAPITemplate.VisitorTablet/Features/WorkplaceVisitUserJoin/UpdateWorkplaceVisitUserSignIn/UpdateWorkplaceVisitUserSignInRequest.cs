namespace VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisitUserJoin.UpdateWorkplaceVisitUserSignIn
{
    public sealed class UpdateWorkplaceVisitUserSignInRequest
    {
        public Guid HostUid { get; set; }
        public List<Guid> Uid { get; set; }
        public DateTime SignInDate { get; set; }
    }
}
