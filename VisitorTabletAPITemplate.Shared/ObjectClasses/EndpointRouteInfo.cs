namespace VisitorTabletAPITemplate.ObjectClasses
{
    public sealed class EndpointRouteInfo
    {
        public string[]? Verbs { get; set; }
        public string Route { get; set; } = default!;
        public string ReqDtoType { get; set; } = default!;
    }
}
