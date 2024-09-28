namespace VisitorTabletAPITemplate.Features.Functions.DeleteFunction
{
    public sealed class DeleteFunctionResponse_FunctionInUse
    {
        public List<DeleteFunctionResponse_FunctionInUse_Desk>? Desks { get; set; }
        public List<DeleteFunctionResponse_FunctionInUse_User>? Users { get; set; }
    }

    public sealed class DeleteFunctionResponse_FunctionInUse_Desk
    {
        public Guid DeskId { get; set; }
        public string DeskName { get; set; } = default!;
        public Guid FloorId { get; set; }
        public string FloorName { get; set; } = default!;
    }

    public sealed class DeleteFunctionResponse_FunctionInUse_User
    {
        public Guid Uid { get; set; }
        public string? DisplayName { get; set; }
        public string Email { get; set; } = default!;
        public string? AvatarThumbnailUrl { get; set; }
    }
}
