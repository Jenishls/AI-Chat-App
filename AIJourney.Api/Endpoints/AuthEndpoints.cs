using AIJourney.Api.Contracts;
using AIJourney.Api.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace AIJourney.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth")
            .WithTags("Auth");

        group.MapPost("/register", Register).WithName("Register");
        group.MapPost("/login", Login).WithName("Login");
        group.MapPost("/logout", Logout).RequireAuthorization().WithName("Logout");
        group.MapGet("/me", Me).RequireAuthorization().WithName("Me");

        return group;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager)
    {
        var email = request.Email.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest("Email and password are required.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };

        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return Results.BadRequest(result.Errors.Select(error => error.Description));
        }

        return Results.Created($"/api/auth/users/{user.Id}", new AuthUserDto(user.Id, email));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        SignInManager<ApplicationUser> signInManager)
    {
        var email = request.Email.Trim();
        var result = await signInManager.PasswordSignInAsync(
            email,
            request.Password,
            isPersistent: true,
            lockoutOnFailure: false);

        if (!result.Succeeded)
        {
            return Results.Unauthorized();
        }

        return Results.Ok();
    }

    private static async Task<IResult> Logout(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static IResult Me(ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? string.Empty;
        return Results.Ok(new AuthUserDto(userId ?? string.Empty, email));
    }

    private sealed record AuthUserDto(string Id, string Email);
}
