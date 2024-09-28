using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ImageStorage.Models.LogModels;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ImageStorage.Features.ListImageStorageLogsByObjectIdForDataTable
{
    public sealed class ListImageStorageLogsByObjectIdForDataTableEndpoint : Endpoint<ListImageStorageLogsByObjectIdForDataTableRequest>
    {
        private readonly ImageStorageRepository _imageStorageRepository;
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public ListImageStorageLogsByObjectIdForDataTableEndpoint(ImageStorageRepository imageStorageRepository,
            UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _imageStorageRepository = imageStorageRepository;
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/logs/imageStorage/{organizationId}/listLogsByObjectId/{relatedObjectId}");
            SerializerContext(ListImageStorageLogsByObjectIdForDataTableContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListImageStorageLogsByObjectIdForDataTableRequest req, CancellationToken ct)
        {
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
            DataTableResponse<ImageStorageLog> imageStorageLogDataTable = await _imageStorageRepository.ListImageStorageLogsByObjectIdForDataTableAsync(req.RelatedObjectId!.Value, req.RelatedObjectTypes, req.OrganizationId!.Value, req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, SearchQueryParser.SplitTerms(req.Search), ct);

            await SendAsync(imageStorageLogDataTable);
        }

        private async Task ValidateInputAsync(ListImageStorageLogsByObjectIdForDataTableRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user is a Master, ignoring the organization permission, OR is a Super Admin and has access to the organization
            if (!await this.ValidateMasterOrUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate RelatedObjectId
            if (!req.RelatedObjectId.HasValue)
            {
                AddError(m => m.RelatedObjectId!, "Related Object Id is required.", "error.imageStorage.relatedObjectIdIsRequired");
            }

            // If no page number specified just use page 1 as default
            if (!req.PageNumber.HasValue)
            {
                req.PageNumber = 1;
            }

            // If no page size specified just use 30 as default
            if (!req.PageSize.HasValue)
            {
                req.PageSize = 30;
            }

            // If no sort specified just use Created Date as default
            if (!req.Sort.HasValue || req.Sort == SortType.Unsorted)
            {
                req.Sort = SortType.Created;
            }
        }
    }
}
