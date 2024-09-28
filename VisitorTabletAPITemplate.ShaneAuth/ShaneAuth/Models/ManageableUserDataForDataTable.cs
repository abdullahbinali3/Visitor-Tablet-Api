namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class ManageableUserDataForDataTable
    {
        public Guid Uid { get; set; }
        public string Email { get; set; } = default!;
        public string? DisplayName { get; set; }
        public string? AvatarThumbnailUrl { get; set; }
        public Guid FunctionId { get; set; }
        public string FunctionName { get; set; } = default!;
        public bool Disabled { get; set; }
    }
}
