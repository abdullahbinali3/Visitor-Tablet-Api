namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Register.CompleteRegister
{
    public sealed class AuthCompleteRegisterRequest
    {
        public string? Email { get; set; }
        public string? Token { get; set; }
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public string? LocalPassword { get; set; }
        public string? LocalPasswordConfirm { get; set; }
        public Guid? RegionId { get; set; }
        public Guid? BuildingId { get; set; }
        public Guid? FunctionId { get; set; }
    }
}
