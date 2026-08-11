using Microsoft.AspNetCore.Authorization;

namespace YasPortal.Application.Authorization;

public sealed record PermissionRequirement(string Code) : IAuthorizationRequirement;
