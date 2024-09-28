using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.DeleteUser
{
    public sealed class MasterDeleteUserEndpoint : Endpoint<MasterDeleteUserRequest>
    {
        private readonly UsersRepository _usersRepository;

        public MasterDeleteUserEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/master/users/delete");
            SerializerContext(MasterDeleteUserContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterDeleteUserRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            ValidateInput(req, userId.Value);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, UserData? userData) = await _usersRepository.MasterDeleteUserAsync(req, userId.Value, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        private void ValidateInput(MasterDeleteUserRequest req, Guid userId)
        {
            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.masterSettings.users.uidIsRequired");
            }
            else if (req.Uid.Value == userId)
            {
                AddError(m => m.Uid!, "You cannot delete your own account.", "error.masterSettings.users.cannotDeleteYourOwnAccount");
            }

            // Validate ConcurrencyKey
            if (req.ConcurrencyKey is null || req.ConcurrencyKey.Length == 0)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key is required.", "error.concurrencyKeyIsRequired");
            }
            else if (req.ConcurrencyKey.Length != 4)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key must be 4 bytes in length.", "error.concurrencyKeyLengthBytes|{\"length\":\"4\"}");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult, UserData? userData)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError(m => m.Uid!, "The user was deleted since you last accessed this page.", "error.masterSettings.users.deletedSinceAccessedPage");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                        break;
                    }

                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData, MasterDeleteUserContext.Default.UserData));
                    AddError("The user's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit again if you still wish to remove the user.", "error.masterSettings.users.deleteConcurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
