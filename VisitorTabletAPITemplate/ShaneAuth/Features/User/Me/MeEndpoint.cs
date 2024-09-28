using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.GetUser
{
    public sealed class MeEndpoint : EndpointWithoutRequest
    {
        private readonly UsersRepository _usersRepository;

        public MeEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Get("/user/me");
            SerializerContext(MeContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            Guid? userId = User.GetId();

            // Validate request
            ValidateInput(userId);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get user data
            UserData? userData = await _usersRepository.GetUserByUidAsync(userId!.Value, true, false, ct);

            // Validate result
            ValidateOutput(userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // For master users, also populate MasterInfo
            if (userData!.UserSystemRole == UserSystemRole.Master)
            {
                userData.ExtendedData.MasterInfo = await _usersRepository.GetMasterInfoAsync(ct);
            }

            await SendAsync(userData!);
        }

        public void ValidateInput(Guid? userId)
        {
            if (!userId.HasValue)
            {
                AddError("Uid is required.", "error.uidRequired");
            }
        }

        public void ValidateOutput(UserData? data)
        {
            if (data is null)
            {
                AddError("The specified user did not exist.", "error.userDidNotExist");
            }
        }
    }
}
