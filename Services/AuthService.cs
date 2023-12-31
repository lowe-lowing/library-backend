namespace backend.Services;

using AutoMapper;
using BCrypt.Net;
using backend.Entities;
using backend.Models.Auth;
using backend.Models.Users;
using backend.Models.Response;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using static System.Net.Mime.MediaTypeNames;

public enum TokenType
{
    Access,
    Refresh
}

public interface IAuthService
{
    Response Register(RegisterRequest model);
    Response Login(LoginRequest model);
    Response Verify(string token);
    Response RefreshAccessToken(string token);
}

public class AuthService : IAuthService
{
    private IUserService _userService;
    private readonly IMapper _mapper;
    protected readonly IConfiguration Configuration;


    public AuthService(IUserService userService, IMapper mapper, IConfiguration configuration)
    {
        _userService = userService;
        _mapper = mapper;
        Configuration = configuration;
    }

    public Response Register(RegisterRequest model)
    {
        var userExists = _userService.UserExists(model.Username);
        
        if (userExists) return new Response { code = 409, message = "Username already exists" };

        var userToCreate = _mapper.Map<CreateUserRequest>(model);
        userToCreate.Password = BCrypt.HashPassword(model.Password, BCrypt.GenerateSalt());
        _userService.Create(userToCreate);
        return new Response { code = 200, message = "Successfully registered your account, try signing in." };
    }

    public Response Login(LoginRequest model)
    {
        var user = _userService.GetByUsername(model.Username);
        if (user == null) return new Response { code = 404, message = "User not found" };

        bool passwordMatches = BCrypt.Verify(model.Password, user.Password);
        if (!passwordMatches)
            return new Response { code = 401, message = "Incorrect password" };

        return new Response
        {
            code = 200,
            message = "Successfully logged in",
            accessToken = GenerateAccessToken(user),
            refreshToken = GenerateRefreshToken(user)
        };
    }

    public Response Verify(string token)
    {
        if (!IsTokenValid(token, TokenType.Access, out string username)) return new Response { code = 422, message = "Invalid token" };

        var user = _userService.GetByUsername(username);
        if (user == null) return new Response { code = 404, message = "User not found" };

        if (IsTokenExpired(token)) return new Response { code = 401, message = "Token expired" };

        return new Response
        {
            code = 200,
            message = "User is verified",
        };
    }

    public Response RefreshAccessToken(string refreshToken)
    {
        if (!IsTokenValid(refreshToken, TokenType.Refresh, out string username)) return new Response { code = 422, message = "Invalid token" };

        var user = _userService.GetByUsername(username);
        if (user == null) return new Response { code = 404, message = "User not found" };

        if (IsTokenExpired(refreshToken)) return new Response { code = 401, message = "Token expired" };

        return new Response
        {
            code = 200,
            message = "Successfully refreshed tokens",
            accessToken = GenerateAccessToken(user),
            refreshToken = GenerateRefreshToken(user)
        };
    }

    // Token Helper Functions
    private string GenerateAccessToken(User user)
    {
        var secret = Configuration.GetSection("AppSettings:AccessSecret").Value!;
        var claims = new[]
        {
            new Claim("Id", user.Id.ToString()), new Claim("Username", user.Username),
            new Claim("Type", TokenType.Access.ToString()),
        };

        return GenerateToken(claims, secret, TimeSpan.FromMinutes(15));
    }

    private string GenerateRefreshToken(User user)
    {
        var secret = Configuration.GetSection("AppSettings:RefreshSecret").Value!;
        var claims = new[]
        {
            new Claim("Id", user.Id.ToString()), new Claim("Username", user.Username),
            new Claim("Type", TokenType.Refresh.ToString()),
        };

        return GenerateToken(claims, secret, TimeSpan.FromMinutes(15));
    }

    private string GenerateToken(Claim[] claims, string secret, TimeSpan expiresIn)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.Add(expiresIn),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Token Validation Functions
    private bool IsTokenValid(string token, TokenType type, out string username)
    {
        username = null;

        //string typeStr = type.ToString();
        //var secret = Configuration.GetSection($"AppSettings:{typeStr}Secret").Value!;
        //var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        //var validationParameters = new TokenValidationParameters()
        //{
        //    ValidateIssuerSigningKey = true,
        //    IssuerSigningKey = key,
        //    ValidateIssuer = false,
        //    ValidateAudience = false,
        //    ValidateLifetime = true,
        //};

        var tokenHandler = new JwtSecurityTokenHandler();

        var splitToken = token.Split(' ')[1];
        if (splitToken == null || splitToken == "null") return false;

        //tokenHandler.ValidateToken(splitToken, validationParameters, out SecurityToken validatedToken);
        //if (validatedToken == null) return false;

        var tokenData = ReadTokenData(token);
        if (tokenData == null) return false;

        var claims = tokenData.Claims;

        string tokenType = claims.FirstOrDefault(c => c.Type == "Type")?.Value;
        if (tokenType != type.ToString()) return false;

        username = claims.FirstOrDefault(c => c.Type == "Username")?.Value;

        return !string.IsNullOrEmpty(username);
    }
    private bool IsTokenExpired(string token)
    {
        var tokenData = ReadTokenData(token);
        if (tokenData == null) return true;

        var expClaim = tokenData.Claims.FirstOrDefault(c => c.Type == "exp")?.Value;
        if (expClaim == null) return true;

        var expDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expClaim)).UtcDateTime;

        return expDate < DateTime.UtcNow;
    }
    private JwtSecurityToken ReadTokenData(string token)
    {
        var splitToken = token.Split(' ')[1];
        if (splitToken == null || splitToken == "null") return null;

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenData = tokenHandler.ReadJwtToken(splitToken);

        return tokenData;
    }
}