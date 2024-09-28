using VisitorTabletAPITemplate.ImageStorage.Models.LogModels;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.ImageStorage.Features.GetImageStorageLogByLogId
{
    public sealed class GetImageStorageLogByLogIdEndpoint : Endpoint<GetImageStorageLogByLogIdRequest>
    {
        private readonly ImageStorageRepository _imageStorageRepository;
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public GetImageStorageLogByLogIdEndpoint(ImageStorageRepository imageStorageRepository,
            UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _imageStorageRepository = imageStorageRepository;
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/logs/imageStorage/{organizationId}/getLogByLogId/{logId}");
            SerializerContext(GetImageStorageLogByLogIdContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetImageStorageLogByLogIdRequest req, CancellationToken ct)
        {
            // Get logged in user's UID
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req, userId.Value, ct);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            ImageStorageLog? imageStorageLog = await _imageStorageRepository.GetImageStorageLogByLogIdAsync(req.LogId!.Value, req.OrganizationId!.Value, ct);

            // Validate result
            ValidateOutput(imageStorageLog);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(imageStorageLog!);
        }

        private async Task ValidateInputAsync(GetImageStorageLogByLogIdRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user is a Master, ignoring the organization permission, OR is a Super Admin and has access to the organization
            if (!await this.ValidateMasterOrUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate LogId
            if (!req.LogId.HasValue)
            {
                AddError(m => m.LogId!, "Log Id is required.", "error.logIdIsRequired");
            }
        }

        private void ValidateOutput(ImageStorageLog? imageStorageLog)
        {
            if (imageStorageLog is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError(m => m.LogId!, "The selected image storage log did not exist.", "error.imageStorage.logDidNotExist");
            }
        }
    }
}
