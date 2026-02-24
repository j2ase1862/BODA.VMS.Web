using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class AuthService : IAuthService
{
    private readonly BodaVmsDbContext _db;
    private readonly JwtTokenService _jwt;

    public AuthService(BodaVmsDbContext db, JwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return null;

        if (!user.IsApproved)
            return null;

        var token = _jwt.GenerateToken(user.Id, user.Username, user.DisplayName, user.Role);

        return new LoginResponse
        {
            Token = token,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role
        };
    }

    public async Task<(bool Success, string Message)> RegisterAsync(RegisterRequest request)
    {
        var exists = await _db.Users.AnyAsync(u => u.Username == request.Username);
        if (exists)
            return (false, "Username already exists");

        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            DisplayName = request.DisplayName,
            Role = "Pending",
            IsApproved = false
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return (true, "Registration successful. Please wait for admin approval.");
    }

    public async Task<UserDto?> GetUserByIdAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null) return null;

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsApproved = user.IsApproved,
            CreatedAt = user.CreatedAt,
            ApprovedAt = user.ApprovedAt
        };
    }
}
