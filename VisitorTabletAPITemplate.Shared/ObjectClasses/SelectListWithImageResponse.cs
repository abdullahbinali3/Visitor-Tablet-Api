namespace VisitorTabletAPITemplate.ObjectClasses
{
    public sealed class SelectListWithImageResponse
    {
        public long? RequestCounter { get; set; }
        public List<SelectListItemGuidWithImage>? Records { get; set; }
    }

    public sealed class SelectListItemGuidWithImage
    {
        public Guid Value { get; set; }
        public string Text { get; set; } = default!;
        public string? SecondaryText { get; set; }
        public string? ImageUrl { get; set; }
    }
}
