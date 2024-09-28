namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateNote
{
    public sealed class MasterUserUpdateNoteRequest
    {
        public Guid? Uid { get; set; }
        public Guid? OrganizationId { get; set; }
        public string? Note { get; set; }
    }
}
