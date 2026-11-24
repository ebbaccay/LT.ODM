# Video tutorials (Help)

**Where:** Help > Video tutorials (`/help/videos`). **Who:** every signed-in user (the `TrainingVideos:Roles` setting, default `*`; opened to all on 2026-11-12); a single video can be limited further (the costing video: Admin and Costing). **Admins** can add videos, edit or replace any video, and hide, delete and restore videos on the page. **AI:** none; the videos are plain MP4 files.

Short narrated videos that show how to use LT ODM, with subtitles. The page lists the videos (title, description, length, size) and plays the one picked. Nothing is downloaded until **Play** is clicked (the files are 10-20 MB); the player then shows the download progress and supports pause, seeking and full screen.

| Video | Length | Who | Contents |
|---|---|---|---|
| Getting started: the basics | 5:35 | Everyone | Sign in, the menu and top bar, the dashboard, finding styles (search, filters), the style page (tabs, tech pack), Materials, Fill the gaps, Smart search, the Library assistant, style packages, Costing, theme, dark mode and the account menu |
| From sketch to client offer | 3:26 | Everyone | New style with a sketch, Suggest from picture (description draft, weave, usual fabric), Suggest BOM (reference styles, lines, the same-customer rule for labels), Add to package with a new package, the package page |
| From sample photo to client offer | 3:47 | Everyone | Recorded on the dev server's data: a new style (FW27HS100 double layer tee) from a sample photo, Suggest from picture (description draft, look-alike styles, knit/woven hint), adding what the photo cannot show (wash), Suggest BOM, the Similar tab, a new client package |
| Sharing a package with the client | 4:26 | Admin, Merchandiser, Designer, Costing (it shows FOB quotes) | Making a share link with prices, the client's page without sign-in (style cards, Like / Pass, comments, Request sample with details, two people on one link, Download PDF), the bell notice, the package going Viewed and Sampling by itself, each person's answers on the package page, setting outcomes (Dropped, Ordered) and the package status, Hit rate and What to offer learning from the answers |
| Costing: prices across LT ODM | 4:32 | Admin, Costing (it shows FOB, net balance and % to FOB) | The Costing page (columns, who sees what, filters, small orders, cost per minute, loading the ERP export), Prices by IO and Where the FOB goes on the style page, prices on Similar styles, the dashboard price panels (Price guide by garment, My concepts vs target, Price data health, Net balance watch, Quotes needing attention), quotes in packages and what the client sees, client price bands, What to offer, a concept's Estimated cost |

## One video for some roles only

A video that shows something only some roles may see gets `"roles"` in `videos.json`, e.g. the costing video:

```json
{ "file": "LT-ODM-Costing.mp4", "title": "Costing: prices across LT ODM", "durationSeconds": 272, "roles": ["Admin", "Costing"] }
```

Other users do not see it in the list, and asking for the file by name gives 404. No `roles` (or `"*"`) = everyone who may open the page. If `videos.json` cannot be read, the page shows **no** videos (rather than all of them), so a broken file never opens a limited video to everyone; fix the file and reload. When a new video shows prices or other limited data, give it the roles that see that data in the app.

## Adding, editing and replacing videos (Admins)

- **Add video** (top right): an MP4 file (up to 100 MB), a title, a description and **Who may watch** (tick roles; none ticked = everyone who may open the page). The browser measures the length; an upload bar shows the progress. The video is added at the end of the list and marked *Added on this page*. It is stored under the uploads folder (`Files:Root\training-video-uploads`, see [File storage](../installation/08-file-storage.md)), so new releases do not remove it.
- **Edit** (under each video): change the title, description and who may watch of **any** video, release videos included (e.g. open the costing video to more roles, or rename one). A release video then shows *details edited*; **Use original details** goes back to the release's `videos.json` details. Saving without a change keeps nothing.
- **Replace the video file** (in Edit): pick a new MP4 to play instead of the current one; the title, description and roles stay. A release video is then marked *File replaced on this page* and plays the new file until **Use the original file**; a re-recorded video in a later release is not shown while a replacement is in place. Replacing a video added on the page simply swaps its file.
- Under each video Admins also see where it comes from (*From the release*, *Added on this page*, *File replaced on this page*) and who may watch it (*Everyone* or *Only: ...*).
- Changes are kept in the database by file name (`nav.TrainingVideo`), so a release does not undo them.

## Hiding and deleting videos (Admins)

Under each video in the list, Admins see **Hide** and **Delete**:

- **Hide:** the video disappears for everyone else (from the list, and its file gives 404). Admins still see it, dimmed and marked *Hidden: only Admins see it*, and can play it. **Show again** puts it back.
- **Delete:** after a confirmation, the video disappears from the list for everyone, Admins included. It is kept under **Deleted videos (N)** at the bottom of the list (Admins only), with who deleted it and when, and **Restore** puts it back. A video added on the page can also be **deleted for good** there (its file is removed; cannot be undone). Release videos cannot be deleted for good, because every release brings them back.

The release's MP4 files are never touched: they ship with every release, and the server folder is read-only for the app. The marks are kept in the database by file name (`nav.TrainingVideoState`), so a release that copies the file again does not bring a hidden or deleted video back. To really remove a video, take it out of `training\video-manual` (and `videos.json`) on the build machine before the next release. Role limits (above) still apply first: hiding never opens a video to more people. If every video is hidden or deleted, other users see *No videos are installed*.

## Limiting the whole page to some roles

Open to everyone by default: the menu item has no role rule and the setting is `*`. To limit it, change both:

1. **Settings > Menu**: tick the roles on Help > Video tutorials (who sees the menu item).
2. **`TrainingVideos:Roles`** on the server, e.g. `Admin,Merchandiser` ([Configuration](../installation/04-configuration.md#video-tutorials)), then recycle the app pool. The API refuses everyone else; a user who opens the address without the role is told the videos are not open to their role yet.

## Files and release

- The videos are **not in git** (large binaries). They live in `training\video-manual` next to the repository: the MP4 files and `videos.json`, which sets the order, titles, descriptions, lengths and per-video roles. MP4 files that `videos.json` does not list still show to everyone, after the listed ones, titled from the file name (so list every limited video); entries whose file is missing are skipped.
- `tools/build-release.ps1` copies them into the release as `api\training-videos` (parameter `-TrainingVideos` for another folder). Without videos the release still builds and the page says none are installed. See [IIS deployment](../installation/03-iis-deployment.md#1-build-a-release-build-machine).
- On the server the folder is `<API folder>\training-videos` unless `TrainingVideos:Folder` points elsewhere. The app-pool account needs **read** access only. Nothing to back up: each release brings the videos again.
- Development: `appsettings.Development.json` points `TrainingVideos:Folder` at `../../../../training/video-manual`.

## Making and updating the videos

The videos are recorded from the running app with the scripts in `training\video-manual\source` (see its `README.txt`): `record.js` drives the browser through the steps and captures each screen with a highlight and subtitles, `kokoro_tts.py` reads the narration with the Kokoro voice (Apache 2.0, runs on the PC, nothing is sent out) and `build.py` joins screens and voice into the MP4. Every user-facing change to a screen shown in a video is followed by re-recording the steps it affects; update `videos.json` (length) with it.

## API

| Call | Notes |
|---|---|
| `GET /api/v1/training-videos` | `{ available, canManage, videos: [{ file, title, description, durationSeconds, sizeBytes, state, stateBy, stateUtc, roles, source, edited }] }`, only the videos the user's roles may watch, without hidden and deleted ones; for Admins (`canManage`) every video, also hidden, deleted and role-limited ones, with `state` (Hidden / Deleted), `roles`, `source` (Release / Uploaded / Replaced) and `edited`. A `videos.json` that cannot be read hides the release videos; videos added on the page still show. 403 without one of the `TrainingVideos:Roles` (Admins always get the list) |
| `GET /api/v1/training-videos/{file}` | The MP4 (`video/mp4`, Range requests supported). Only plain `*.mp4` names that exist in the folder and that the user may watch are served (no paths); hidden and deleted videos only to Admins; anything else is 404 |
| `POST /api/v1/training-videos` | Admin only. Multipart: `file` (MP4, up to 100 MB, checked by its first bytes), `title`, `description`, `roles` (comma-separated, blank = everyone), `durationSeconds`. 201 with `{ file }`, the new name made from the title (`sewing-room-tour-3fa2c1.mp4`) |
| `PUT /api/v1/training-videos/{file}` | Admin only. `{ title, description, roles: [], durationSeconds }` for any video of the list; on a release video only what differs from `videos.json` is kept |
| `DELETE /api/v1/training-videos/{file}/details` | Admin only. Back to the release's details (release or replaced videos) |
| `POST /api/v1/training-videos/{file}/file` | Admin only. Multipart `file`, `durationSeconds`: a new file for the video; the details stay |
| `DELETE /api/v1/training-videos/{file}/file` | Admin only. A replaced release video plays its original file again |
| `DELETE /api/v1/training-videos/{file}` | Admin only. Deletes a video added on the page for good (file and database rows); 400 for a release video |
| `PUT /api/v1/training-videos/{file}/state` | Admin only. Body `{ "state": "Hidden" \| "Deleted" \| null }` (null = shown again). 404 for a name that is not an MP4 in the folder, 400 for another state. Procedures `nav.usp_TrainingVideoState_List` / `_Set` and `nav.usp_TrainingVideo_List` / `_Save` / `_Delete` (`db/procedures/admin.procedures.sql`), tables `nav.TrainingVideoState` and `nav.TrainingVideo` in `db/tables/nav.tables.sql` (both run by `deploy-auth.sql`). Uploads: `FileSystemTrainingVideoStore` |

The page fetches the video through the app's HTTP client, because a `<video>` element cannot send the sign-in token, and plays it from a blob URL.
