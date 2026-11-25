using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RosterSensei.Infrastructure.Persistence;

namespace RosterSensei.Infrastructure.Identity;

public static class UserCreation
{
    // Creates the user like UserManager.CreateAsync, except that losing a race to the unique email or
    // username index gives Identity's normal DuplicateEmail / DuplicateUserName error instead of a 500.
    // Identity checks for a duplicate before it inserts, so two sign-ups with the same email at the same
    // time both pass that check and only the database stops the second one.
    public static async Task<IdentityResult> CreateUserAsync(
        this UserManager<AppUser> userManager, AppDbContext db, AppUser user, string? password = null)
    {
        try
        {
            return password == null
                ? await userManager.CreateAsync(user)
                : await userManager.CreateAsync(user, password);
        }
        catch (DbUpdateException)
        {
            // The failed insert stays tracked; without this, the next SaveChanges in the request tries it again.
            db.Entry(user).State = EntityState.Detached;

            if (user.Email != null && await userManager.FindByEmailAsync(user.Email) != null)
                return IdentityResult.Failed(userManager.ErrorDescriber.DuplicateEmail(user.Email));
            if (user.UserName != null && await userManager.FindByNameAsync(user.UserName) != null)
                return IdentityResult.Failed(userManager.ErrorDescriber.DuplicateUserName(user.UserName));
            throw;
        }
    }
}
