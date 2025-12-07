using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dtce.Identity;
using Dtce.Identity.Models;
using System.Security.Claims;

namespace Dtce.WebClient.Controllers;

[Authorize]
public class ApiKeyController : Controller
{
    private readonly IUserService _userService;
    private readonly ILogger<ApiKeyController> _logger;

    public ApiKeyController(IUserService userService, ILogger<ApiKeyController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var apiKeys = await _userService.GetUserApiKeysAsync(userId);
        return View(apiKeys);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Json(new { success = false, message = "API key name is required" });
        }

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var apiKey = await _userService.GenerateApiKeyAsync(userId, name);
        
        return Json(new { success = true, apiKey = apiKey.Key, name = apiKey.Name, createdAt = apiKey.CreatedAt });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _userService.RevokeApiKeyAsync(userId, id);
        
        if (result)
        {
            return Json(new { success = true });
        }
        
        return Json(new { success = false, message = "Failed to revoke API key" });
    }
}


