using Microsoft.AspNetCore.Identity;

namespace AI.Factory.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<long>
{
    public bool IsActive { get; set; } = true;
}
