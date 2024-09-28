namespace VisitorTabletAPITemplate.ObjectClasses
{
    public sealed class ReturnListAndItemResponse<T>
    {
        public T? Item { get; set; }
        public List<T>? ItemList { get; set; }
    }

    public sealed class ReturnListAndItemsResponse<T>
    {
        public List<T>? Items { get; set; }
        public List<T>? ItemList { get; set; }
    }
}
