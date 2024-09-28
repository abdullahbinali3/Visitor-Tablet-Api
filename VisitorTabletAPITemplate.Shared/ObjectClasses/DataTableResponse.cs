namespace VisitorTabletAPITemplate.ObjectClasses
{
    public sealed class DataTableResponse<T> where T : class, new()
    {
        public long? RequestCounter { get; set; }
        public List<T>? Records { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
    }
}
