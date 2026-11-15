using Microsoft.AspNetCore.Authorization;

namespace YasPortal.Application.Authorization;

// A rule used by the web framework's authorization system: "the person must hold the permission with this code".
// Pages and policies are declared with this requirement, and a handler checks it against the person's grants.
public sealed record PermissionRequirement(string Code) : IAuthorizationRequirement;
