namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.UpdateNote
{
    public sealed class UserUpdateNoteRequest
    {
        public Guid? OrganizationId { get; set; }
        public string? Note { get; set; }
    }
}
