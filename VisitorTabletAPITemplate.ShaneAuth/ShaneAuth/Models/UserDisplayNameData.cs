namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class UserDisplayNameData
    {
        public Guid Uid { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public string Email { get; set; } = default!;
        public string? AvatarUrl { get; set; }
        public string? AvatarThumbnailUrl { get; set; }
    }
}
