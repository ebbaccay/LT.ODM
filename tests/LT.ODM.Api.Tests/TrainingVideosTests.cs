using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LT.ODM.Application.Abstractions;
using LT.ODM.Application.Auth;
using LT.ODM.Application.Training;
using Microsoft.Extensions.DependencyInjection;

namespace LT.ODM.Api.Tests;

/// <summary>Stands in for nav.TrainingVideoState and nav.TrainingVideo.</summary>
public sealed class FakeTrainingVideoRepository : ITrainingVideoRepository
{
    public Dictionary<string, TrainingVideoState> States { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TrainingVideoDetails> Details { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Reset()
    {
        States.Clear();
        Details.Clear();
    }

    public Task<IReadOnlyList<TrainingVideoState>> ListStatesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TrainingVideoState>>(States.Values.ToList());

    public Task SetStateAsync(string fileName, string? state, string changedBy, CancellationToken ct = default)
    {
        if (state is null) States.Remove(fileName);
        else States[fileName] = new(fileName, state, changedBy, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TrainingVideoDetails>> ListDetailsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TrainingVideoDetails>>(Details.Values.ToList());

    public Task SaveDetailsAsync(string fileName, string? title, string? description, int? durationSeconds, string? roles, string? uploadFile,
        string changedBy, CancellationToken ct = default)
    {
        if (title is null && description is null && durationSeconds is null && roles is null && uploadFile is null) Details.Remove(fileName);
        else Details[fileName] = new(fileName, title, description, durationSeconds, roles, uploadFile,
            Details.TryGetValue(fileName, out var old) ? old.CreatedBy : changedBy, old?.CreatedUtc ?? DateTime.UtcNow, changedBy, DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string fileName, CancellationToken ct = default)
    {
        States.Remove(fileName);
        Details.Remove(fileName);
        return Task.CompletedTask;
    }
}

/// <summary>A folder of uploaded videos for the catalog tests (any plain .mp4 name).</summary>
public sealed class FolderUploadStore(string folder) : ITrainingVideoUploadStore
{
    public string Put(byte[] bytes)
    {
        Directory.CreateDirectory(folder);
        var name = Guid.NewGuid().ToString("N") + ".mp4";
        File.WriteAllBytes(Path.Combine(folder, name), bytes);
        return name;
    }

    public async Task<string> SaveAsync(Stream content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        return Put(ms.ToArray());
    }

    public string? PathOf(string? storedName)
        => storedName is not null && TrainingVideoCatalog.IsSafeFileName(storedName) && File.Exists(Path.Combine(folder, storedName)) ? Path.Combine(folder, storedName) : null;

    public void Delete(string? storedName)
    {
        if (PathOf(storedName) is { } path) File.Delete(path);
    }
}

public sealed class TrainingVideosTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Mh7!Rv92#Kp4w";

    private async Task<HttpClient> SignInAsync(params string[] roles)
    {
        var name = "vid" + Guid.NewGuid().ToString("N")[..8];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, roles);
        var client = factory.CreateHttpsClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ltodm-videos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static readonly Func<string, bool> Anyone = _ => false;

    private void WriteFactoryVideos()
    {
        Directory.CreateDirectory(factory.TrainingVideosFolder);
        File.WriteAllBytes(Path.Combine(factory.TrainingVideosFolder, "Basics.mp4"), Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());
        File.WriteAllBytes(Path.Combine(factory.TrainingVideosFolder, "Costing.mp4"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(factory.TrainingVideosFolder, "videos.json"),
            """
            [{ "file": "Basics.mp4", "title": "Getting started", "description": "The basics", "durationSeconds": 331 },
             { "file": "Costing.mp4", "title": "Costing", "roles": ["Admin", "Costing"] }]
            """);
        File.WriteAllText(Path.Combine(factory.TrainingVideosFolder, "secret.txt"), "not a video");
    }

    // ----- Catalog -----

    [Fact]
    public void The_manifest_sets_order_and_titles_and_unlisted_videos_follow_by_name()
    {
        var folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "B.mp4"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "A-first_video.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "Extra.mp4"), [1, 2]);
        File.WriteAllText(Path.Combine(folder, "videos.json"),
            """[{ "file": "B.mp4", "title": "Second in name, first in list", "durationSeconds": 60 }, { "file": "Missing.mp4", "title": "Gone" }, { "file": "A-first_video.mp4" }]""");

        var list = TrainingVideoCatalog.List(folder, Anyone);

        Assert.True(list.Available);
        Assert.Equal(["B.mp4", "A-first_video.mp4", "Extra.mp4"], list.Videos.Select(v => v.File));
        Assert.Equal("Second in name, first in list", list.Videos[0].Title);
        Assert.Equal(60, list.Videos[0].DurationSeconds);
        Assert.Equal(3, list.Videos[0].SizeBytes);
        Assert.Equal("A first video", list.Videos[1].Title);   // no title in the manifest: from the name
        Assert.Equal("Extra", list.Videos[2].Title);
    }

    [Fact]
    public void Without_a_manifest_every_video_shows_by_name()
    {
        var folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "LT-ODM-Basics.mp4"), [1]);

        Assert.Equal("LT ODM Basics", Assert.Single(TrainingVideoCatalog.List(folder, Anyone).Videos).Title);
    }

    [Fact]
    public void A_broken_manifest_shows_and_serves_nothing_so_limited_videos_never_open_up()
    {
        var folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "Costing.mp4"), [1]);
        File.WriteAllText(Path.Combine(folder, "videos.json"), "{ not json");

        Assert.False(TrainingVideoCatalog.List(folder, Anyone).Available);
        Assert.Null(TrainingVideoCatalog.Resolve(folder, "Costing.mp4", _ => true));
    }

    [Fact]
    public void A_video_with_roles_is_listed_and_served_only_to_those_roles()
    {
        var folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "Basics.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "Costing.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "Open.mp4"), [1]);
        File.WriteAllText(Path.Combine(folder, "videos.json"),
            """[{ "file": "Basics.mp4" }, { "file": "Costing.mp4", "roles": ["Admin", " Costing "] }, { "file": "Open.mp4", "roles": ["*"] }]""");
        Func<string, bool> viewer = r => r == "Viewer", costing = r => r == "Costing";

        Assert.Equal(["Basics.mp4", "Open.mp4"], TrainingVideoCatalog.List(folder, viewer).Videos.Select(v => v.File));
        Assert.Equal(["Basics.mp4", "Costing.mp4", "Open.mp4"], TrainingVideoCatalog.List(folder, costing).Videos.Select(v => v.File));
        Assert.Null(TrainingVideoCatalog.Resolve(folder, "Costing.mp4", viewer));
        Assert.Null(TrainingVideoCatalog.Resolve(folder, "costing.mp4", viewer));   // names compare without case
        Assert.NotNull(TrainingVideoCatalog.Resolve(folder, "Costing.mp4", costing));
        Assert.NotNull(TrainingVideoCatalog.Resolve(folder, "Basics.mp4", viewer));
    }

    [Fact]
    public void A_missing_or_empty_folder_is_not_available()
    {
        Assert.False(TrainingVideoCatalog.List(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")), Anyone).Available);
        Assert.False(TrainingVideoCatalog.List(NewFolder(), Anyone).Available);
    }

    [Theory]
    [InlineData("Basics.mp4", true)]
    [InlineData("LT-ODM_Sketch.to.Offer.mp4", true)]
    [InlineData("../Basics.mp4", false)]
    [InlineData("..\\Basics.mp4", false)]
    [InlineData("sub/Basics.mp4", false)]
    [InlineData("C:Basics.mp4", false)]
    [InlineData(".hidden.mp4", false)]
    [InlineData("videos.json", false)]
    [InlineData("Basics.MP4.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_plain_mp4_names_are_accepted(string? name, bool ok) => Assert.Equal(ok, TrainingVideoCatalog.IsSafeFileName(name));

    [Fact]
    public void Resolve_finds_only_existing_videos_in_the_folder()
    {
        var folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "Basics.mp4"), [1]);

        Assert.Equal(Path.Combine(folder, "Basics.mp4"), TrainingVideoCatalog.Resolve(folder, "Basics.mp4", Anyone));
        Assert.Null(TrainingVideoCatalog.Resolve(folder, "Other.mp4", Anyone));
        Assert.Null(TrainingVideoCatalog.Resolve(folder, "../Basics.mp4", Anyone));
    }

    [Theory]
    [InlineData("Admin", new[] { "Admin" }, true)]
    [InlineData("Admin", new[] { "Merchandiser" }, false)]
    [InlineData("Admin, Merchandiser,Designer", new[] { "Designer" }, true)]
    [InlineData("*", new[] { "Factory" }, true)]      // default: every signed-in user
    [InlineData("", new[] { "Viewer" }, true)]         // blank = every signed-in user
    [InlineData("Admin,*", new[] { "Viewer" }, true)]
    public void Watching_follows_the_roles_setting(string setting, string[] userRoles, bool expected)
        => Assert.Equal(expected, TrainingVideoCatalog.CanWatch(setting, r => userRoles.Contains(r)));

    // ----- Endpoints -----

    [Fact]
    public async Task Admin_gets_the_list_and_can_stream_with_ranges()
    {
        WriteFactoryVideos();
        var client = await SignInAsync("Admin");

        var list = await client.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos");
        Assert.Equal(["Basics.mp4", "Costing.mp4"], list!.Videos.Select(v => v.File));
        var video = list.Videos[0];
        Assert.Equal("Getting started", video.Title);
        Assert.Equal(331, video.DurationSeconds);

        var full = await client.GetAsync("/api/v1/training-videos/Basics.mp4");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal("video/mp4", full.Content.Headers.ContentType?.MediaType);
        Assert.Equal(100, (await full.Content.ReadAsByteArrayAsync()).Length);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/training-videos/Basics.mp4");
        request.Headers.Range = new RangeHeaderValue(10, 19);
        var part = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(Enumerable.Range(10, 10).Select(i => (byte)i), await part.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Other_files_and_unknown_names_are_not_served()
    {
        WriteFactoryVideos();
        var client = await SignInAsync("Admin");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/training-videos/secret.txt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/training-videos/videos.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/training-videos/Nope.mp4")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/training-videos/..%2Fsecret.txt")).StatusCode);
    }

    [Fact]
    public async Task By_default_every_signed_in_user_can_watch_but_anonymous_cannot()
    {
        WriteFactoryVideos();
        var factoryUser = await SignInAsync("Factory");

        Assert.Equal(HttpStatusCode.OK, (await factoryUser.GetAsync("/api/v1/training-videos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await factoryUser.GetAsync("/api/v1/training-videos/Basics.mp4")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().GetAsync("/api/v1/training-videos")).StatusCode);
    }

    [Fact]
    public async Task A_video_limited_to_Admin_and_Costing_is_hidden_and_not_served_to_others()
    {
        WriteFactoryVideos();
        var viewer = await SignInAsync("Viewer");
        var costing = await SignInAsync("Costing");

        var viewerList = await viewer.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos");
        Assert.Equal(["Basics.mp4"], viewerList!.Videos.Select(v => v.File));
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/training-videos/Costing.mp4")).StatusCode);

        var costingList = await costing.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos");
        Assert.Contains(costingList!.Videos, v => v.File == "Costing.mp4");
        Assert.Equal(HttpStatusCode.OK, (await costing.GetAsync("/api/v1/training-videos/Costing.mp4")).StatusCode);
    }

    [Fact]
    public async Task A_roles_setting_refuses_other_roles()
    {
        WriteFactoryVideos();
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("TrainingVideos:Roles", "Admin"));
        var name = "vid" + Guid.NewGuid().ToString("N")[..8];
        var hasher = limited.Services.GetRequiredService<IPasswordHasher>();
        await factory.Users.CreateUserAsync(name, $"{name}@company.test", name, hasher.Hash(Password), false, ["Merchandiser"]);
        var client = limited.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login", new { login = name, password = Password, rememberMe = false }))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/training-videos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/training-videos/Basics.mp4")).StatusCode);
    }

    // ----- Hidden and deleted videos -----

    private static TrainingVideoState Mark(string file, string state) => new(file, state, "boss", DateTime.UtcNow);

    [Fact]
    public void Hidden_and_deleted_videos_are_left_out_for_users_and_marked_for_managers()
    {
        var folder = NewFolder();
        foreach (var f in new[] { "A.mp4", "B.mp4", "C.mp4" }) File.WriteAllBytes(Path.Combine(folder, f), [1]);
        TrainingVideoState[] states = [Mark("b.mp4", "Hidden"), Mark("C.mp4", "Deleted"), Mark("A.mp4", "Odd")];

        var user = TrainingVideoCatalog.List(folder, Anyone, states);
        Assert.Equal(["A.mp4"], user.Videos.Select(v => v.File));   // an unknown state hides nothing
        Assert.False(user.CanManage);
        Assert.Null(user.Videos[0].State);

        var manager = TrainingVideoCatalog.List(folder, Anyone, states, canManage: true);
        Assert.True(manager.CanManage);
        Assert.Equal([null, "Hidden", "Deleted"], manager.Videos.Select(v => v.State));
        Assert.Equal("boss", manager.Videos[1].StateBy);

        Assert.Null(TrainingVideoCatalog.Resolve(folder, "B.mp4", Anyone, states));
        Assert.Null(TrainingVideoCatalog.Resolve(folder, "C.mp4", Anyone, states));
        Assert.NotNull(TrainingVideoCatalog.Resolve(folder, "C.mp4", Anyone, states, canManage: true));
        Assert.NotNull(TrainingVideoCatalog.Resolve(folder, "A.mp4", Anyone, states));
    }

    [Fact]
    public void All_videos_hidden_means_none_available_for_users()
    {
        var folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "A.mp4"), [1]);

        Assert.False(TrainingVideoCatalog.List(folder, Anyone, [Mark("A.mp4", "Hidden")]).Available);
        Assert.True(TrainingVideoCatalog.List(folder, Anyone, [Mark("A.mp4", "Hidden")], canManage: true).Available);
    }

    [Fact]
    public async Task An_admin_hides_deletes_and_restores_a_video()
    {
        WriteFactoryVideos();
        var admin = await SignInAsync("Admin");
        var viewer = await SignInAsync("Viewer");
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/training-videos/Basics.mp4/state", new { state = "hidden" })).StatusCode);

            var viewerList = await viewer.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos");
            Assert.False(viewerList!.Available);   // Basics hidden, Costing not for Viewers
            Assert.False(viewerList.CanManage);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/training-videos/Basics.mp4")).StatusCode);

            var adminList = await admin.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos");
            Assert.True(adminList!.CanManage);
            var basics = adminList.Videos.Single(v => v.File == "Basics.mp4");
            Assert.Equal(("Hidden", true), (basics.State, basics.StateBy is { Length: > 0 }));
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/training-videos/Basics.mp4")).StatusCode);   // Admin can still play it

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/training-videos/Basics.mp4/state", new { state = "Deleted" })).StatusCode);
            Assert.Equal("Deleted", (await admin.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos"))!.Videos.Single(v => v.File == "Basics.mp4").State);

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/training-videos/Basics.mp4/state", new { state = (string?)null })).StatusCode);
            Assert.Equal(["Basics.mp4"], (await viewer.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos"))!.Videos.Select(v => v.File));
        }
        finally
        {
            factory.Videos.Reset();
        }
    }

    [Fact]
    public async Task Only_admins_change_states_and_only_for_videos_in_the_folder()
    {
        WriteFactoryVideos();
        var admin = await SignInAsync("Admin");
        var merch = await SignInAsync("Merchandiser");
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await merch.PutAsJsonAsync("/api/v1/training-videos/Basics.mp4/state", new { state = "Hidden" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/v1/training-videos/Nope.mp4/state", new { state = "Hidden" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/v1/training-videos/secret.txt/state", new { state = "Hidden" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/training-videos/Basics.mp4/state", new { state = "Gone" })).StatusCode);
            Assert.Empty(factory.Videos.States);
        }
        finally
        {
            factory.Videos.Reset();
        }
    }

    // ----- Added, edited and replaced videos -----

    /// <summary>A few bytes that look like an MP4 (an ftyp box), then a marker so tests can tell files apart.</summary>
    private static byte[] Mp4(byte marker, int length = 64)
    {
        var b = new byte[length];
        "\0\0\0\u0018ftypmp42"u8.ToArray().CopyTo(b, 0);
        for (var i = 12; i < length; i++) b[i] = marker;
        return b;
    }

    private static TrainingVideoDetails Upload(string file, string stored, string title, string? roles = "", DateTime? created = null)
        => new(file, title, "", null, roles, stored, "boss", created ?? DateTime.UtcNow);

    private string UploadsFolder => Path.Combine(factory.FilesRoot, "training-video-uploads");

    private static MultipartFormDataContent Form(byte[]? file, string fileName = "clip.mp4", params (string Key, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        if (file is not null) form.Add(new ByteArrayContent(file), "file", fileName);
        foreach (var (k, v) in fields) form.Add(new StringContent(v), k);
        return form;
    }

    private void ResetVideos()
    {
        factory.Videos.Reset();
        if (Directory.Exists(UploadsFolder)) Directory.Delete(UploadsFolder, recursive: true);
    }

    [Fact]
    public void Uploaded_videos_follow_the_release_ones_and_replace_files_of_the_same_name()
    {
        var folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "A.mp4"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "B.mp4"), [1, 2]);
        File.WriteAllText(Path.Combine(folder, "videos.json"), """[{ "file": "B.mp4", "title": "Bee", "roles": ["Costing"] }, { "file": "A.mp4", "title": "Ay" }]""");
        var store = new FolderUploadStore(NewFolder());
        TrainingVideoDetails[] details =
        [
            Upload("new-two.mp4", store.Put([9, 9, 9]), "Second upload", created: DateTime.UtcNow),
            Upload("new-one.mp4", store.Put([9]), "First upload", roles: "Admin", created: DateTime.UtcNow.AddMinutes(-5)),
            Upload("A.mp4", store.Put([7, 7, 7, 7]), "Ay") with { Title = null, Description = null, Roles = null },   // replacement only
            new("B.mp4", "Bee edited", null, 42, "", null, "boss", DateTime.UtcNow),                                  // details only
        ];

        var manager = TrainingVideoCatalog.List(folder, Anyone, [], canManage: true, details, store);
        Assert.Equal(["B.mp4", "A.mp4", "new-one.mp4", "new-two.mp4"], manager.Videos.Select(v => v.File));
        Assert.Equal(["Release", "Replaced", "Uploaded", "Uploaded"], manager.Videos.Select(v => v.Source));
        Assert.Equal([true, false, false, false], manager.Videos.Select(v => v.Edited));
        Assert.Equal(("Bee edited", 42), (manager.Videos[0].Title, manager.Videos[0].DurationSeconds));
        Assert.Empty(manager.Videos[0].Roles!);                       // roles opened to everyone on the page
        Assert.Equal(4, manager.Videos[1].SizeBytes);                 // the replacement's file
        Assert.Equal(["Admin"], manager.Videos[2].Roles);

        var user = TrainingVideoCatalog.List(folder, Anyone, [], false, details, store);
        Assert.Equal(["B.mp4", "A.mp4", "new-two.mp4"], user.Videos.Select(v => v.File));   // new-one is for Admins only
        Assert.Null(user.Videos[0].Source);                                                 // management details for Admins only
        Assert.EndsWith(details[2].UploadFile!, TrainingVideoCatalog.Resolve(folder, "A.mp4", Anyone, [], false, details, store));
        Assert.Null(TrainingVideoCatalog.Resolve(folder, "new-one.mp4", Anyone, [], false, details, store));
    }

    [Fact]
    public void A_broken_manifest_still_shows_videos_added_on_the_page_but_no_release_video()
    {
        var folder = NewFolder();
        File.WriteAllBytes(Path.Combine(folder, "Costing.mp4"), [1]);
        File.WriteAllText(Path.Combine(folder, "videos.json"), "{ not json");
        var store = new FolderUploadStore(NewFolder());
        TrainingVideoDetails[] details = [Upload("added.mp4", store.Put([1]), "Added"), Upload("Costing.mp4", store.Put([2]), "Costing") with { Roles = null }];

        Assert.Equal(["added.mp4"], TrainingVideoCatalog.List(folder, Anyone, [], false, details, store).Videos.Select(v => v.File));
    }

    [Theory]
    [InlineData("Sewing room tour", "sewing-room-tour-")]
    [InlineData("  Café: costing & FOB!! ", "cafe-costing-fob-")]
    [InlineData("???", "video-")]
    public void New_file_names_come_from_the_title(string title, string start)
    {
        var name = TrainingVideoCatalog.NewFileName(title, _ => false);
        Assert.StartsWith(start, name);
        Assert.True(TrainingVideoCatalog.IsSafeFileName(name));
    }

    [Fact]
    public void Only_files_starting_like_an_mp4_are_taken()
    {
        Assert.True(TrainingVideoCatalog.LooksLikeMp4(Mp4(1)));
        Assert.False(TrainingVideoCatalog.LooksLikeMp4("PK\u0003\u0004 zip file"u8));
        Assert.False(TrainingVideoCatalog.LooksLikeMp4([0, 0]));
    }

    [Fact]
    public async Task An_admin_adds_a_video_that_users_can_watch_and_deletes_it_for_good()
    {
        WriteFactoryVideos();
        ResetVideos();
        var admin = await SignInAsync("Admin");
        var viewer = await SignInAsync("Viewer");
        try
        {
            var created = await admin.PostAsync("/api/v1/training-videos",
                Form(Mp4(5), "tour.MP4", ("title", "Sewing room tour"), ("description", "A walk through"), ("roles", ""), ("durationSeconds", "95")));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var file = (await created.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["file"];
            Assert.StartsWith("sewing-room-tour-", file);

            var list = await viewer.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos");
            var added = list!.Videos.Last();
            Assert.Equal((file, "Sewing room tour", "A walk through", 95), (added.File, added.Title, added.Description, added.DurationSeconds));
            Assert.Equal(Mp4(5), await viewer.GetByteArrayAsync($"/api/v1/training-videos/{file}"));
            Assert.Equal("Uploaded", (await admin.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos"))!.Videos.Last().Source);

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/v1/training-videos/Basics.mp4")).StatusCode);   // a release video
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/training-videos/{file}")).StatusCode);
            Assert.Empty(factory.Videos.Details);
            Assert.Empty(Directory.GetFiles(UploadsFolder));
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/v1/training-videos/{file}")).StatusCode);
        }
        finally
        {
            ResetVideos();
        }
    }

    [Fact]
    public async Task Adding_checks_the_title_the_file_and_the_role()
    {
        WriteFactoryVideos();
        ResetVideos();
        var admin = await SignInAsync("Admin");
        var merch = await SignInAsync("Merchandiser");
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await merch.PostAsync("/api/v1/training-videos", Form(Mp4(1), "a.mp4", ("title", "X")))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/training-videos", Form(Mp4(1), "a.mp4", ("title", " ")))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/training-videos", Form(null, "a.mp4", ("title", "X")))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/training-videos", Form(Mp4(1), "a.mov", ("title", "X")))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/v1/training-videos", Form("not a video at all"u8.ToArray(), "a.mp4", ("title", "X")))).StatusCode);
            Assert.Empty(factory.Videos.Details);
        }
        finally
        {
            ResetVideos();
        }
    }

    [Fact]
    public async Task An_admin_edits_a_release_video_and_goes_back_to_the_release_details()
    {
        WriteFactoryVideos();
        ResetVideos();
        var admin = await SignInAsync("Admin");
        var viewer = await SignInAsync("Viewer");
        try
        {
            // Unchanged details keep nothing.
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/training-videos/Basics.mp4",
                new { title = "Getting started", description = "The basics", roles = Array.Empty<string>() })).StatusCode);
            Assert.Empty(factory.Videos.Details);

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/training-videos/Basics.mp4",
                new { title = "Start here", description = "", roles = new[] { "Admin", "Merchandiser" } })).StatusCode);
            Assert.Empty((await viewer.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos"))!.Videos);   // now Admin and Merchandiser only
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/v1/training-videos/Basics.mp4")).StatusCode);
            var edited = (await admin.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos"))!.Videos.Single(v => v.File == "Basics.mp4");
            Assert.Equal(("Start here", null, true), (edited.Title, edited.Description, edited.Edited));
            Assert.Equal(["Admin", "Merchandiser"], edited.Roles);

            // Opening the Costing video (Admin, Costing in videos.json) to everyone.
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync("/api/v1/training-videos/Costing.mp4",
                new { title = "Costing", roles = Array.Empty<string>() })).StatusCode);
            Assert.Contains((await viewer.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos"))!.Videos, v => v.File == "Costing.mp4");

            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/training-videos/Basics.mp4/details")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/training-videos/Costing.mp4/details")).StatusCode);
            Assert.Equal(["Basics.mp4"], (await viewer.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos"))!.Videos.Select(v => v.File));
            Assert.Empty(factory.Videos.Details);

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/v1/training-videos/Basics.mp4", new { title = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/v1/training-videos/Nope.mp4", new { title = "X" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/v1/training-videos/Basics.mp4", new { title = "X" })).StatusCode);
        }
        finally
        {
            ResetVideos();
        }
    }

    [Fact]
    public async Task An_admin_replaces_a_release_video_file_and_goes_back_to_the_original()
    {
        WriteFactoryVideos();
        ResetVideos();
        var admin = await SignInAsync("Admin");
        var viewer = await SignInAsync("Viewer");
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/v1/training-videos/Basics.mp4/file", Form(Mp4(3), "new.mp4", ("durationSeconds", "300")))).StatusCode);
            Assert.Equal(Mp4(3), await viewer.GetByteArrayAsync("/api/v1/training-videos/Basics.mp4"));
            var replaced = (await admin.GetFromJsonAsync<TrainingVideoListDto>("/api/v1/training-videos"))!.Videos.Single(v => v.File == "Basics.mp4");
            Assert.Equal(("Replaced", "Getting started", 300, false), (replaced.Source, replaced.Title, replaced.DurationSeconds, replaced.Edited));

            // Replacing again keeps one uploaded file.
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/v1/training-videos/Basics.mp4/file", Form(Mp4(4), "newer.mp4"))).StatusCode);
            Assert.Equal(Mp4(4), await viewer.GetByteArrayAsync("/api/v1/training-videos/Basics.mp4"));
            Assert.Single(Directory.GetFiles(UploadsFolder));

            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/v1/training-videos/Basics.mp4/file")).StatusCode);
            Assert.Equal(100, (await viewer.GetByteArrayAsync("/api/v1/training-videos/Basics.mp4")).Length);   // the release file again
            Assert.Empty(Directory.GetFiles(UploadsFolder));
            Assert.Empty(factory.Videos.Details);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/v1/training-videos/Basics.mp4/file")).StatusCode);   // nothing replaced
        }
        finally
        {
            ResetVideos();
        }
    }
}
