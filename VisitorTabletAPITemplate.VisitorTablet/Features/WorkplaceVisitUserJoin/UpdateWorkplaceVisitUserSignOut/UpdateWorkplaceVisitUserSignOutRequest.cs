namespace VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisitUserJoin.UpdateWorkplaceVisitUserSignOut
{
    public sealed class UpdateWorkplaceVisitUserSignOutRequest
    {
        public Guid HostUid { get; set; }
        public List<Guid> Uid { get; set; }
        public DateTime SignOutDate { get; set; }
    }
}
