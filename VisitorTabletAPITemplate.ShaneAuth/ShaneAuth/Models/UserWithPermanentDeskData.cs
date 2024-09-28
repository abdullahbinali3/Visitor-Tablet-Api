namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class UserWithPermanentDeskData
    {
        public Guid Uid { get; set; }
        public string Email { get; set; } = default!;
        public string? DisplayName { get; set; }
        public string? AvatarThumbnailUrl { get; set; }
        public Guid FunctionId { get; set; }
        public string FunctionName { get; set; } = default!;
        public Guid? PermanentDeskId { get; set; }
        public string? PermanentDeskName { get; set; }
        public Guid? PermanentDeskFloorId { get; set; }
        public string? PermanentDeskFloorName { get; set; }
        public Guid? PermanentDeskBuildingId { get; set; }
        public string? PermanentDeskBuildingName { get; set; }
        public string? PermanentDeskLocation {  get; set; }
        public bool Disabled { get; set; }
    }
}
