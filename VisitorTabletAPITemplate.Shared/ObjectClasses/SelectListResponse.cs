namespace VisitorTabletAPITemplate.ObjectClasses
{
    public sealed class SelectListResponse
    {
        public long? RequestCounter { get; set; }
        public List<SelectListItemGuid>? Records { get; set; }
    }

    public sealed class SelectListItemGuid
    {
        public Guid Value { get; set; }
        public string Text { get; set; } = default!;
    }

    public sealed class SelectListResponseInt
    {
        public long? RequestCounter { get; set; }
        public List<SelectListItemInt>? Records { get; set; }
    }

    public sealed class SelectListItemInt
    {
        public int Value { get; set; }
        public string Text { get; set; } = default!;
    }
}
