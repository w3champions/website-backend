using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using W3C.Contracts.Admin.Permission;
using W3C.Domain.Common.Services;
using W3C.Domain.Tracing;
using W3ChampionsStatisticService.RateLimiting.Models;
using W3ChampionsStatisticService.RateLimiting.Repositories;
using W3ChampionsStatisticService.WebApi.ActionFilters;

namespace W3ChampionsStatisticService.RateLimiting.Controllers;

[ApiController]
[Route("api/admin/api-tokens")]
[Trace]
public class ApiTokenController(
    IApiTokenRepository apiTokenRepository,
    IAuditLogService auditLogService) : ControllerBase
{
    private readonly IApiTokenRepository _apiTokenRepository = apiTokenRepository;
    private readonly IAuditLogService _auditLogService = auditLogService;
    
    [HttpGet]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<ActionResult<List<ApiToken>>> GetAllTokens()
    {
        var tokens = await _apiTokenRepository.GetAll();
        // Token values are now visible to admins
        return Ok(tokens);
    }
    
    [HttpGet("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    public async Task<ActionResult<ApiToken>> GetToken(string id)
    {
        var token = await _apiTokenRepository.GetById(id);
        if (token == null)
        {
            return NotFound();
        }
        return Ok(token);
    }
    
    [HttpPost]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    [InjectActingPlayerAuthCodeAttribute]
    public async Task<ActionResult<ApiToken>> CreateToken([FromBody] CreateApiTokenRequest request)
    {
        var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
        var battleTag = user?.BattleTag ?? "Unknown";
        
        var token = new ApiToken
        {
            Name = request.Name,
            Description = request.Description,
            ContactDetails = request.ContactDetails,
            AllowedIPs = request.AllowedIPs ?? Array.Empty<string>(),
            ExpiresAt = request.ExpiresAt,
            Scopes = request.Scopes ?? new Dictionary<string, ApiTokenScope>()
        };
        
        await _apiTokenRepository.Create(token);
        
        // Log the creation
        await _auditLogService.LogAdminAction(
            battleTag,
            "CREATE_API_TOKEN",
            "ApiToken",
            token.Id,
            oldValue: null,
            newValue: new { token.Name, token.Description, Scopes = token.Scopes?.Keys, token.ContactDetails },
            metadata: new Dictionary<string, object>
            {
                ["token_name"] = token.Name,
                ["scopes"] = token.Scopes?.Keys?.ToList() ?? new List<string>()
            });
        
        return Created($"/api/admin/api-tokens/{token.Id}", token);
    }
    
    [HttpPut("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    [InjectActingPlayerAuthCodeAttribute]
    public async Task<IActionResult> UpdateToken(string id, [FromBody] UpdateApiTokenRequest request)
    {
        var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
        var battleTag = user?.BattleTag ?? "Unknown";
        
        var token = await _apiTokenRepository.GetById(id);
        if (token == null)
        {
            return NotFound();
        }
        
        // Capture old values for audit
        var oldValue = new
        {
            token.Name,
            token.Description,
            token.ContactDetails,
            token.AllowedIPs,
            token.IsActive,
            token.ExpiresAt,
            Scopes = token.Scopes?.Keys
        };
        
        // Update only the allowed fields
        token.Name = request.Name;
        token.Description = request.Description;
        token.ContactDetails = request.ContactDetails;
        token.AllowedIPs = request.AllowedIPs ?? Array.Empty<string>();
        token.IsActive = request.IsActive;
        token.ExpiresAt = request.ExpiresAt;
        token.Scopes = request.Scopes ?? new Dictionary<string, ApiTokenScope>();
        
        await _apiTokenRepository.Update(token);
        
        // Log the update
        await _auditLogService.LogAdminAction(
            battleTag,
            "UPDATE_API_TOKEN",
            "ApiToken",
            token.Id,
            oldValue: oldValue,
            newValue: new
            {
                token.Name,
                token.Description,
                token.ContactDetails,
                token.AllowedIPs,
                token.IsActive,
                token.ExpiresAt,
                Scopes = token.Scopes?.Keys
            },
            metadata: new Dictionary<string, object>
            {
                ["token_name"] = token.Name
            });
        
        return NoContent();
    }
    
    [HttpDelete("{id}")]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    [InjectActingPlayerAuthCodeAttribute]
    public async Task<IActionResult> DeleteToken(string id)
    {
        var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
        var battleTag = user?.BattleTag ?? "Unknown";
        
        var token = await _apiTokenRepository.GetById(id);
        if (token == null)
        {
            return NotFound();
        }
        
        await _apiTokenRepository.Delete(id);
        
        // Log the deletion
        await _auditLogService.LogAdminAction(
            battleTag,
            "DELETE_API_TOKEN",
            "ApiToken",
            token.Id,
            oldValue: new { token.Name, token.Description, Scopes = token.Scopes?.Keys },
            newValue: null,
            metadata: new Dictionary<string, object>
            {
                ["token_name"] = token.Name,
                ["scopes"] = token.Scopes?.Keys?.ToList() ?? new List<string>()
            });
        
        return NoContent();
    }
    
    [HttpPost("{id}/regenerate")]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    [InjectActingPlayerAuthCodeAttribute]
    public async Task<ActionResult<ApiToken>> RegenerateToken(string id)
    {
        var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
        var battleTag = user?.BattleTag ?? "Unknown";
        
        var token = await _apiTokenRepository.GetById(id);
        if (token == null)
        {
            return NotFound();
        }
        
        var oldTokenValue = token.Token;
        
        // Generate new UUID for the token
        token.Token = Guid.NewGuid().ToString();
        token.CreatedAt = DateTimeOffset.UtcNow;
        
        await _apiTokenRepository.Update(token);
        
        // Log the regeneration (don't log actual token values for security)
        await _auditLogService.LogAdminAction(
            battleTag,
            "REGENERATE_API_TOKEN",
            "ApiToken",
            token.Id,
            oldValue: new { TokenId = oldTokenValue.Substring(0, 8) + "..." },
            newValue: new { TokenId = token.Token.Substring(0, 8) + "..." },
            reason: "Token regenerated by admin",
            metadata: new Dictionary<string, object>
            {
                ["token_name"] = token.Name
            });
        
        return Ok(token);
    }
    
    [HttpPost("{id}/deactivate")]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    [InjectActingPlayerAuthCodeAttribute]
    public async Task<IActionResult> DeactivateToken(string id)
    {
        var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
        var battleTag = user?.BattleTag ?? "Unknown";
        
        var token = await _apiTokenRepository.GetById(id);
        if (token == null)
        {
            return NotFound();
        }
        
        token.IsActive = false;
        await _apiTokenRepository.Update(token);
        
        // Log the deactivation
        await _auditLogService.LogAdminAction(
            battleTag,
            "DEACTIVATE_API_TOKEN",
            "ApiToken",
            token.Id,
            oldValue: new { IsActive = true },
            newValue: new { IsActive = false },
            metadata: new Dictionary<string, object>
            {
                ["token_name"] = token.Name
            });
        
        return NoContent();
    }
    
    [HttpPost("{id}/activate")]
    [BearerHasPermissionFilter(Permission = EPermission.Permissions)]
    [InjectActingPlayerAuthCodeAttribute]
    public async Task<IActionResult> ActivateToken(string id)
    {
        var user = InjectActingPlayerAuthCodeAttribute.GetActingPlayerUser(HttpContext);
        var battleTag = user?.BattleTag ?? "Unknown";
        
        var token = await _apiTokenRepository.GetById(id);
        if (token == null)
        {
            return NotFound();
        }
        
        token.IsActive = true;
        await _apiTokenRepository.Update(token);
        
        // Log the activation
        await _auditLogService.LogAdminAction(
            battleTag,
            "ACTIVATE_API_TOKEN",
            "ApiToken",
            token.Id,
            oldValue: new { IsActive = false },
            newValue: new { IsActive = true },
            metadata: new Dictionary<string, object>
            {
                ["token_name"] = token.Name
            });
        
        return NoContent();
    }
}

public class CreateApiTokenRequest
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string ContactDetails { get; set; }
    public string[] AllowedIPs { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Dictionary<string, ApiTokenScope> Scopes { get; set; }
}

public class UpdateApiTokenRequest
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string ContactDetails { get; set; }
    public string[] AllowedIPs { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Dictionary<string, ApiTokenScope> Scopes { get; set; }
}