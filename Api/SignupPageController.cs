using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Signup.Api;

/// <summary>
/// Serves the public signup page.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class SignupPageController : ControllerBase
{
    private const string PublicPageResource = "Jellyfin.Plugin.Signup.Pages.public.html";
    private static readonly Lazy<string> PublicPage = new(() => ReadResource(PublicPageResource));

    /// <summary>
    /// Gets the public signup page.
    /// </summary>
    /// <returns>Public signup HTML.</returns>
    [HttpGet("~/signup.html")]
    public ContentResult GetPublicSignupPage()
    {
        return Content(PublicPage.Value, "text/html", Encoding.UTF8);
    }

    private static string ReadResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
