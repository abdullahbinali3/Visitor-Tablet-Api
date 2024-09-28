using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.GetUser
{
    public sealed class MasterGetUserEndpoint : Endpoint<MasterGetUserRequest>
    {
        private readonly UsersRepository _usersRepository;

        public MasterGetUserEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Get("/master/users/get/{uid}");
            SerializerContext(MasterGetUserContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterGetUserRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            UserData? userData = await _usersRepository.GetUserByUidAsync(req.Uid!.Value, false, true, ct);

            // Validate result
            ValidateOutput(userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // If user is the logged in user and is a master user, also populate MasterInfo
            if (req.Uid == User.GetId() && userData!.UserSystemRole == UserSystemRole.Master)
            {
                userData.ExtendedData.MasterInfo = await _usersRepository.GetMasterInfoAsync(ct);
            }

            await SendAsync(userData!);
        }

        private void ValidateInput(MasterGetUserRequest req)
        {
            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.user.uidIsRequired");
            }
        }

        private void ValidateOutput(UserData? userData)
        {
            if (userData is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The selected user did not exist.", "error.user.didNotExist");
            }
        }
    }
}
