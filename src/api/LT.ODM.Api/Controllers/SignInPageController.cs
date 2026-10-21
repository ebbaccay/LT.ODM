using LT.ODM.Application.Admin;
using LT.ODM.Application.SignInPage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LT.ODM.Api.Controllers;

/// <summary>
/// Settings > Sign-in page: background picture and glass look of the sign-in pages. The appearance and the picture are
/// read without sign-in (the page shows before it); uploading and saving need the Admin role. Pictures uploaded but
/// not saved are removed at the next save.
/// </summary>
[ApiController]
[Route("api/v1/sign-in-page")]
[Authorize(Roles = AdminValidation.AdminRole)]
public sealed class SignInPageController(ISignInAppearanceStore appearances, ISignInImageStore images) : ControllerBase
{
    private string CurrentUserName => User.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? "";

    [HttpGet]
    [AllowAnonymous]
    public async Task<SignInAppearanceDto> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-cache";
        var (appearance, by, at) = await appearances.GetAsync(ct);
        return SignInAppearanceDto.From(appearance, by, at);
    }

    [HttpGet("images/{fileName}")]
    [AllowAnonymous]
    public Task<IActionResult> Image(string fileName, [FromQuery(Name = "w")] int? width, CancellationToken ct)
        => ImageUploads.ServeAsync(this, images, fileName, width, ct);

    /// <summary>Stores a picture (JPEG, PNG, GIF or WebP, at most 10 MB; made at most 2560 px). It is used once saved.</summary>
    [HttpPost("images")]
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        var (name, problem) = await ImageUploads.SaveAsync(this, images, file, ct);
        return problem ?? Ok(new { backgroundImage = name, backgroundUrl = SignInAppearanceDto.ImageUrlPrefix + name });
    }

    [HttpPut]
    public async Task<IActionResult> Save(SignInAppearance request, CancellationToken ct)
    {
        var errors = SignInPageRules.Validate(request);
        await CheckPictureAsync(errors, "backgroundImage", request.BackgroundImage);
        await CheckPictureAsync(errors, "dark.backgroundImage", request.Dark?.BackgroundImage);
        if (errors.Count > 0) return ValidationProblem(new ValidationProblemDetails(errors));

        await appearances.SaveAsync(request, CurrentUserName, ct);
        images.DeleteAllExcept(request.Images);
        return Ok(SignInAppearanceDto.From(request, CurrentUserName, DateTime.UtcNow));
    }

    private async Task CheckPictureAsync(Dictionary<string, string[]> errors, string field, string? file)
    {
        if (file is null) return;
        if (images.Open(file) is { } found) await found.Content.DisposeAsync();
        else errors[field] = ["The picture was not found. Upload it again."];
    }
}
