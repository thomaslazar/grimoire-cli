# Grimoire API coverage

Map of every Grimoire HTTP API operation and the `grimoire-cli` command (if any) that implements it.

- **Reference:** spec fetched live from the pinned stack's `/api/openapi.json` (v1.7.3, 237 paths, 313 operations) and the upstream router source read from the same container. Tested range: `1.7.2`-`1.7.3` (`GrimoireApiClient.cs`).
- **Perm** column uses Grimoire's roles (`admin` / `gm or admin` / `not guest`); blank = any authenticated user. `?` = a dependency this script could not resolve.
- **Key** column is the API key permission a route needs (`—`: no key can call it), derived from `backend/api_keys.py` in the same container.
- ✅ = covered by a CLI command · — = not implemented · 🔒 = internal-only (no user-facing verb); 🔒 rows never count as covered.
- **Regenerate with `tools/generate-api-coverage.py`; update `IMPLEMENTED` there in the same PR as any change to which endpoints the CLI calls.**

## Coverage summary

| Tag | Covered / Total |
|-----|-----------------|
| (untagged) | 0 / 1 |
| addons | 8 / 8 |
| api-keys | 0 / 6 |
| audio | 14 / 14 |
| audio-sets | 0 / 5 |
| auth | 2 / 14 |
| backups | 6 / 6 |
| bookmarks | 0 / 4 |
| books | 16 / 16 |
| campaigns | 17 / 93 |
| downloads | 1 / 1 |
| duplicates | 13 / 13 |
| favorites | 0 / 3 |
| files | 9 / 10 |
| library | 4 / 7 |
| logs | 1 / 1 |
| lookups | 15 / 15 |
| maintenance | 3 / 4 |
| maps | 14 / 16 |
| models | 10 / 10 |
| saved-filters | 0 / 4 |
| search | 2 / 2 |
| settings | 0 / 3 |
| stats | 1 / 1 |
| systems | 15 / 15 |
| tags | 6 / 6 |
| themes | 0 / 7 |
| token-frames | 0 / 2 |
| tokens | 10 / 10 |
| users | 0 / 16 |
| **Total** | **167 / 313** |

2 operation(s) are internal-only (🔒) and excluded from covered counts.

## (untagged)

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/{full_path}` | Serve Frontend |  | — | — |

## addons

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/addons` | List add-ons | admin | addons: read | `addons list` ✅ |
| POST | `/api/addons/refresh` | Refresh the add-on index | admin | addons: write | `addons refresh` ✅ |
| PATCH | `/api/addons/settings` | Update add-on settings | admin | addons: write | `addons settings` ✅ |
| POST | `/api/addons/update-all` | Update all add-ons | admin | addons: write | `addons upgrade-all` ✅ |
| GET | `/api/addons/verify-index` | Verify an add-on index URL |  | addons: read | `addons verify-index` ✅ |
| PATCH | `/api/addons/{addon_id}` | Enable, disable, or approve an add-on | admin | addons: write | `addons update` ✅ |
| DELETE | `/api/addons/{addon_id}` | Uninstall an add-on | admin | addons: write | `addons uninstall` ✅ |
| POST | `/api/addons/{addon_id}/install` | Install or update an add-on | admin | addons: write | `addons install` ✅ |

## api-keys

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/api-keys` | List your API keys (never their secrets) |  | — | — |
| POST | `/api/api-keys` | Create an API key that acts as you |  | — | — |
| GET | `/api/api-keys/permissions` | List the permissions your keys can be granted |  | — | — |
| PATCH | `/api/api-keys/{key_id}` | Rename a key or change its permissions or expiry |  | — | — |
| DELETE | `/api/api-keys/{key_id}` | Revoke (delete) an API key |  | — | — |
| POST | `/api/api-keys/{key_id}/regenerate` | Issue a new secret for a key |  | — | — |

## audio

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/audio` | List audio | not guest | audio: read | `audio list` ✅ |
| GET | `/api/audio-folders` | List audio folders | not guest | audio: read | `audio folders list` ✅ |
| PATCH | `/api/audio-folders` | Set tags on an audio folder | gm or admin | audio: write | `audio folders set` ✅ |
| POST | `/api/audio-folders/bulk` | Bulk set audio folder tags | gm or admin | audio: write | `audio folders batch-set` ✅ |
| POST | `/api/audio/bulk` | Bulk update audio tracks | gm or admin | audio: write | `audio batch-update` ✅ |
| POST | `/api/audio/bulk/tags` | Bulk add tags to audio tracks | gm or admin | audio: write | `audio batch-tag` ✅ |
| GET | `/api/audio/{audio_id}` | Get an audio track |  | audio: read | `audio get` ✅ |
| PATCH | `/api/audio/{audio_id}` | Update audio metadata | gm or admin | audio: write | `audio update` ✅ |
| GET | `/api/audio/{audio_id}/artwork` | Audio artwork |  | audio: read | `audio artwork` ✅ |
| GET | `/api/audio/{audio_id}/cover` | Audio cover image | gm or admin | audio: read | `audio cover get` ✅ |
| POST | `/api/audio/{audio_id}/cover` | Upload an audio cover | gm or admin | audio: write | `audio cover upload` ✅ |
| DELETE | `/api/audio/{audio_id}/cover` | Remove an audio cover | gm or admin | audio: write | `audio cover delete` ✅ |
| POST | `/api/audio/{audio_id}/cover/from-source` | Set an audio cover from an existing image | gm or admin | audio: write | `audio cover from-source` ✅ |
| GET | `/api/audio/{audio_id}/file` | Stream/download audio file |  | audio: read | `audio file` ✅ |

## audio-sets

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/audio-sets` | List the user's saved playlists and soundboards |  | personal: read | — |
| POST | `/api/audio-sets` | Save a playlist or soundboard |  | personal: write | — |
| GET | `/api/audio-sets/{set_id}` | Load one saved set, resolved against the library |  | personal: read | — |
| PATCH | `/api/audio-sets/{set_id}` | Rename a saved set or replace its contents |  | personal: write | — |
| DELETE | `/api/audio-sets/{set_id}` | Delete a saved set |  | personal: write | — |

## auth

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/auth/config` | Public auth configuration |  | — | — |
| POST | `/api/auth/guest-login` | Log in as a guest |  | — | — |
| POST | `/api/auth/login` | Log in |  | — | `login` ✅ |
| POST | `/api/auth/logout` | Log out |  | — | — |
| GET | `/api/auth/me` | Get current user |  | — | `me` ✅ |
| GET | `/api/auth/openid/callback` | OIDC callback |  | — | — |
| POST | `/api/auth/openid/discover` | Fetch OIDC discovery document | admin | — | — |
| GET | `/api/auth/openid/login` | Start an OIDC login |  | — | — |
| POST | `/api/auth/refresh` | Refresh the access token |  | — | 🔒 automatic session renewal (all commands) |
| GET | `/api/auth/sessions` | List your active sessions |  | — | — |
| DELETE | `/api/auth/sessions/others` | Log out everywhere else |  | — | — |
| DELETE | `/api/auth/sessions/{session_id}` | Revoke one of your sessions |  | — | — |
| POST | `/api/auth/setup` | First-run admin setup |  | — | — |
| GET | `/api/auth/status` | Check initialization status |  | — | — |

## backups

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/backups` | List backups, newest first | admin | backups: read | `backups list` ✅ |
| POST | `/api/backups` | Create a backup now | admin | backups: write | `backups create` ✅ |
| GET | `/api/backups/settings` | Read backup schedule and retention settings | admin | backups: read | `backups settings get` ✅ |
| PUT | `/api/backups/settings` | Configure backup schedule and retention | admin | backups: write | `backups settings set` ✅ |
| DELETE | `/api/backups/{backup_id}` | Delete a backup archive | admin | backups: write | `backups delete` ✅ |
| GET | `/api/backups/{backup_id}/download` | Download a backup archive | admin | backups: read | `backups download` ✅ |

## bookmarks

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/bookmarks` | List bookmarks for a book |  | personal: read | — |
| POST | `/api/bookmarks` | Create a bookmark |  | personal: write | — |
| PATCH | `/api/bookmarks/{bookmark_id}` | Update bookmark label |  | personal: write | — |
| DELETE | `/api/bookmarks/{bookmark_id}` | Delete a bookmark |  | personal: write | — |

## books

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/books` | List books | not guest | books: read | `books list` ✅ |
| POST | `/api/books/bulk` | Bulk update books | gm or admin | books: write | `books batch-update` ✅ |
| POST | `/api/books/bulk/tags` | Bulk add tags to books | gm or admin | books: write | `books batch-tag` ✅ |
| GET | `/api/books/{book_id}` | Get a book |  | books: read | `books get` ✅ |
| PATCH | `/api/books/{book_id}` | Update book metadata | gm or admin | books: write | `books update` ✅ |
| GET | `/api/books/{book_id}/file` | Download book file |  | books: read | `books file` ✅ |
| POST | `/api/books/{book_id}/metadata-fetch` | Fetch metadata for review | gm or admin | books: read | `books metadata-fetch` ✅ |
| POST | `/api/books/{book_id}/metadata-search` | Search a metadata source | gm or admin | books: read | `books metadata-search` ✅ |
| GET | `/api/books/{book_id}/metadata-sources` | List metadata sources | gm or admin | books: read | `books metadata-sources` ✅ |
| GET | `/api/books/{book_id}/page/{page_num}` | Render a PDF page as WebP |  | books: read | `books page` ✅ |
| GET | `/api/books/{book_id}/page/{page_num}/text` | Get page text |  | books: read | `books page-text` ✅ |
| GET | `/api/books/{book_id}/page/{page_num}/words` | Get page word bounding boxes |  | books: read | `books page-words` ✅ |
| POST | `/api/books/{book_id}/reindex` | Re-run OCR on a book (optional DPI override) | gm or admin | books: write | `books reindex` ✅ |
| POST | `/api/books/{book_id}/rescan` | Re-read a book from disk and rebuild its search index | gm or admin | books: write | `books rescan` ✅ |
| GET | `/api/books/{book_id}/thumbnail` | Book cover thumbnail |  | books: read | `books thumbnail` ✅ |
| GET | `/api/books/{book_id}/toc` | PDF table of contents |  | books: read | `books toc` ✅ |

## campaigns

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/campaigns` | List campaigns for the current user |  | campaigns: read | `campaigns list` ✅ |
| POST | `/api/campaigns` | Create a campaign |  | campaigns: write | `campaigns create` ✅ |
| GET | `/api/campaigns/admin/by-user/{user_id}` | Admin: list campaigns owned by a user (read-only, minimal fields) | admin | campaigns: read | — |
| GET | `/api/campaigns/calendar/subscription` | Get the caller's calendar subscription URLs |  | — | — |
| POST | `/api/campaigns/calendar/subscription` | Mint or rotate the caller's calendar feed token |  | — | — |
| DELETE | `/api/campaigns/calendar/subscription` | Revoke the caller's calendar feed token |  | — | — |
| GET | `/api/campaigns/calendar/{token}/all.ics` | ICS feed of every campaign the token's user belongs to |  | campaigns: read | — |
| GET | `/api/campaigns/calendar/{token}/{campaign_id}.ics` | ICS feed for a single campaign |  | campaigns: read | — |
| GET | `/api/campaigns/invites` | List the current user's pending campaign invitations |  | campaigns: read | — |
| GET | `/api/campaigns/resources/search` | Search books, maps, and tokens by name |  | campaigns: read | — |
| GET | `/api/campaigns/resources/suggested/{system_id}` | Suggested resources (system books) for the create wizard |  | campaigns: read | — |
| GET | `/api/campaigns/{campaign_id}` | Get a campaign |  | campaigns: read | `campaigns get` ✅ |
| PATCH | `/api/campaigns/{campaign_id}` | Update a campaign |  | campaigns: write | `campaigns update` ✅ |
| DELETE | `/api/campaigns/{campaign_id}` | Delete a campaign |  | campaigns: write | — |
| PUT | `/api/campaigns/{campaign_id}/archive` | Archive or unarchive a campaign |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/availability` | Get availability chart for upcoming sessions |  | campaigns: read | — |
| PUT | `/api/campaigns/{campaign_id}/availability/{session_date}` | Set availability for a session date |  | campaigns: write | — |
| PUT | `/api/campaigns/{campaign_id}/availability/{session_date}/cancel` | GM: cancel or uncancel a session date |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/banner` | Upload campaign banner |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/banner` | Get campaign banner image |  | campaigns: read | — |
| DELETE | `/api/campaigns/{campaign_id}/banner` | Remove campaign banner |  | campaigns: write | — |
| PUT | `/api/campaigns/{campaign_id}/banner/focus` | Set the banner focal point |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/banner/from-source` | Set the banner from an existing image |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/calendar.ics` | Download a campaign's schedule as an .ics file |  | campaigns: read | — |
| GET | `/api/campaigns/{campaign_id}/categories` | List categories (optionally filtered by kind) |  | campaigns: read | `campaigns categories list` ✅ |
| POST | `/api/campaigns/{campaign_id}/categories` | Create a category |  | campaigns: write | `campaigns categories create` ✅ |
| PUT | `/api/campaigns/{campaign_id}/categories/reorder` | Reorder categories |  | campaigns: write | `campaigns categories reorder` ✅ |
| PATCH | `/api/campaigns/{campaign_id}/categories/{category_id}` | Rename a category |  | campaigns: write | `campaigns categories update` ✅ |
| DELETE | `/api/campaigns/{campaign_id}/categories/{category_id}` | Delete a category (mode: uncategorize \| delete_items) |  | campaigns: write | `campaigns categories delete` ✅ |
| POST | `/api/campaigns/{campaign_id}/convert-to-group` | Convert a personal campaign into a GM-run group campaign |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/eligible-members` | List users that can be invited |  | campaigns: read | — |
| POST | `/api/campaigns/{campaign_id}/files` | Upload a campaign file (GM); links it as a resource |  | campaigns: write | `campaigns files upload` ✅ |
| GET | `/api/campaigns/{campaign_id}/files/{file_id}` | Download a campaign file (honours resource visibility) |  | campaigns: read | — |
| POST | `/api/campaigns/{campaign_id}/guests` | Create a guest invite code for a GM campaign |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/guests` | List a campaign's guests and their invite codes |  | campaigns: read | — |
| DELETE | `/api/campaigns/{campaign_id}/guests/{member_id}` | Remove a guest (deletes the guest account) |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/guests/{member_id}/regenerate` | Regenerate a guest's invite code |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/guests/{member_id}/share-template` | Get share text and links for a guest invite code |  | campaigns: read | — |
| POST | `/api/campaigns/{campaign_id}/images` | Upload an image (GM); links it as an image resource for note embedding |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/invite` | Invite a player to a GM campaign |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/members/{member_id}/art` | Upload a member's character art |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/members/{member_id}/art` | Get a member's character art |  | campaigns: read | — |
| DELETE | `/api/campaigns/{campaign_id}/members/{member_id}/art` | Remove a member's character art |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/members/{member_id}/sheet` | Upload a member's character sheet |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/members/{member_id}/sheet` | Download a member's character sheet |  | campaigns: read | — |
| DELETE | `/api/campaigns/{campaign_id}/members/{member_id}/sheet` | Remove a member's character sheet |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/members/{member_id}/sheet/duplicate` | Duplicate a blank sheet into a member's slot |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/members/{member_id}/token` | Upload a member's character token |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/members/{member_id}/token` | Get a member's character token |  | campaigns: read | — |
| DELETE | `/api/campaigns/{campaign_id}/members/{member_id}/token` | Remove a member's character token |  | campaigns: write | — |
| PATCH | `/api/campaigns/{campaign_id}/members/{user_id}` | Accept or decline an invitation |  | campaigns: write | — |
| DELETE | `/api/campaigns/{campaign_id}/members/{user_id}` | Remove a member |  | campaigns: write | — |
| PUT | `/api/campaigns/{campaign_id}/resource-group-order` | Set the resource panel's group display order (categories + type groups) |  | campaigns: write | `campaigns categories group-order` ✅ |
| GET | `/api/campaigns/{campaign_id}/resources` | List linked resources |  | campaigns: read | `campaigns resources list` ✅ |
| POST | `/api/campaigns/{campaign_id}/resources` | Link a resource to a campaign |  | campaigns: write | `campaigns resources add` ✅ |
| POST | `/api/campaigns/{campaign_id}/resources/bulk` | Link many resources at once |  | campaigns: write | `campaigns resources bulk` ✅ |
| PUT | `/api/campaigns/{campaign_id}/resources/reorder` | Reorder resources (drag-and-drop) |  | campaigns: write | `campaigns resources reorder` ✅ |
| PATCH | `/api/campaigns/{campaign_id}/resources/{resource_id}` | Update resource visibility/category |  | campaigns: write | `campaigns resources update` ✅ |
| DELETE | `/api/campaigns/{campaign_id}/resources/{resource_id}` | Unlink a resource |  | campaigns: write | `campaigns resources remove` ✅ |
| GET | `/api/campaigns/{campaign_id}/schedule` | Get campaign schedule and next sessions |  | campaigns: read | — |
| PUT | `/api/campaigns/{campaign_id}/schedule` | Create or update campaign schedule |  | campaigns: write | — |
| DELETE | `/api/campaigns/{campaign_id}/schedule` | Remove campaign schedule |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/sessions` | List session notes |  | campaigns: read | — |
| POST | `/api/campaigns/{campaign_id}/sessions` | Create a session note |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/sessions/search` | Search session notes |  | campaigns: read | — |
| GET | `/api/campaigns/{campaign_id}/sessions/{session_id}` | Get a session note with all notes |  | campaigns: read | — |
| PATCH | `/api/campaigns/{campaign_id}/sessions/{session_id}` | Update session title |  | campaigns: write | — |
| DELETE | `/api/campaigns/{campaign_id}/sessions/{session_id}` | Delete a session note |  | campaigns: write | — |
| PUT | `/api/campaigns/{campaign_id}/sessions/{session_id}/notes/gm` | Save GM notes (owner only) |  | campaigns: write | — |
| PUT | `/api/campaigns/{campaign_id}/sessions/{session_id}/notes/player` | Save own player note |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/sheet-sources` | List blank sheets a member can duplicate |  | campaigns: read | — |
| GET | `/api/campaigns/{campaign_id}/wiki` | List visible wiki pages |  | campaigns: read | — |
| POST | `/api/campaigns/{campaign_id}/wiki` | Create a wiki page |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/wiki/export` | Export campaign wiki (md zip or json bundle) |  | campaigns: read | — |
| POST | `/api/campaigns/{campaign_id}/wiki/import` | Import wiki pages from a file or a picked folder |  | campaigns: write | — |
| PUT | `/api/campaigns/{campaign_id}/wiki/reorder` | Reorder wiki pages (drag-and-drop) |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/wiki/search` | Search wiki pages |  | campaigns: read | — |
| GET | `/api/campaigns/{campaign_id}/wiki/templates` | List the campaign's note templates |  | campaigns: read | — |
| POST | `/api/campaigns/{campaign_id}/wiki/templates` | Write a new note template |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/wiki/templates/browse` | Browse the community note-template catalogue |  | campaigns: read | — |
| POST | `/api/campaigns/{campaign_id}/wiki/templates/download/{template_id}` | Download a community note template into the campaign |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/wiki/templates/upload` | Add a note template from an uploaded .md file |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}` | Get a note template incl. its body |  | campaigns: read | — |
| PATCH | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}` | Edit a note template |  | campaigns: write | — |
| DELETE | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}` | Delete a note template |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}/export` | Export a note template as a .zip folder |  | campaigns: read | — |
| POST | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}/use` | Create a wiki page from a note template |  | campaigns: write | — |
| GET | `/api/campaigns/{campaign_id}/wiki/titles` | Wiki page titles for [[link]] autocomplete |  | campaigns: read | — |
| GET | `/api/campaigns/{campaign_id}/wiki/{page_id}` | Get a wiki page |  | campaigns: read | — |
| PATCH | `/api/campaigns/{campaign_id}/wiki/{page_id}` | Update a wiki page |  | campaigns: write | — |
| DELETE | `/api/campaigns/{campaign_id}/wiki/{page_id}` | Delete a wiki page |  | campaigns: write | — |
| POST | `/api/campaigns/{campaign_id}/wiki/{page_id}/hide` | Hide a wiki page from your own view |  | campaigns: write | — |
| DELETE | `/api/campaigns/{campaign_id}/wiki/{page_id}/hide` | Un-hide a wiki page you had hidden |  | campaigns: write | — |

## downloads

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/downloads/archive` | Download an archive of files |  | downloads: read | `downloads archive` ✅ |

## duplicates

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| POST | `/api/duplicates/cancel-scan` | Stop a running duplicate scan | admin | duplicates: write | `duplicates cancel-scan` ✅ |
| GET | `/api/duplicates/compare` | Side-by-side comparison of two to four items | admin | duplicates: read | `duplicates compare` ✅ |
| POST | `/api/duplicates/dismiss` | Mark a group as not duplicates | admin | duplicates: write | `duplicates dismiss` ✅ |
| GET | `/api/duplicates/dismissals` | List dismissed groups | admin | duplicates: read | `duplicates dismissals` ✅ |
| DELETE | `/api/duplicates/dismissals/{dismissal_id}` | Undo a dismissal | admin | duplicates: write | `duplicates undismiss` ✅ |
| GET | `/api/duplicates/groups` | Candidate duplicate groups from the last scan | admin | duplicates: read | `duplicates groups` ✅ |
| DELETE | `/api/duplicates/items/{resource_type}/{item_id}` | Delete one duplicate record, and optionally its file | admin | duplicates: write | `duplicates delete` ✅ |
| POST | `/api/duplicates/link` | File items under a parent as its variants | admin | duplicates: write | `duplicates link` ✅ |
| POST | `/api/duplicates/merge-metadata` | Copy metadata fields from one copy onto another | admin | duplicates: write | `duplicates merge-metadata` ✅ |
| POST | `/api/duplicates/promote` | Make a different copy the main version of an existing family | admin | duplicates: write | `duplicates promote` ✅ |
| POST | `/api/duplicates/scan` | Start a duplicate-detection scan | admin | duplicates: write | `duplicates scan` ✅ |
| GET | `/api/duplicates/scan-status` | Progress of the duplicate-detection scan | admin | duplicates: read | `duplicates scan-status` ✅ |
| POST | `/api/duplicates/unlink` | Promote variants back to standalone entries | admin | duplicates: write | `duplicates unlink` ✅ |

## favorites

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/favorites` | List current user's favorites |  | personal: read | — |
| POST | `/api/favorites` | Add a favorite |  | personal: write | — |
| DELETE | `/api/favorites/{item_type}/{item_id}` | Remove a favorite |  | personal: write | — |

## files

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/files/browse` | List a library folder with indexing state | admin | files: read | `files browse` ✅ |
| POST | `/api/files/delete` | Remove a file or folder from the index, or from disk | admin | files: write | `files delete` ✅ |
| POST | `/api/files/folder` | Create a folder, optionally as a container or NSFW | admin | files: write | `files folder create` ✅ |
| DELETE | `/api/files/folder` | Delete a folder, recursively when confirmed by name | admin | files: write | — |
| GET | `/api/files/folder/contents` | Report whether a folder holds content | admin | files: read | `files folder contents` ✅ |
| PUT | `/api/files/folder/markers` | Set a folder's container/NSFW markers | admin | files: write | `files folder markers` ✅ |
| POST | `/api/files/folder/scaffold` | Create the standard category folders in a system folder | admin | files: write | `files folder scaffold` ✅ |
| POST | `/api/files/move` | Move files or folders, preserving their metadata | admin | files: write | `files move` ✅ |
| POST | `/api/files/rename` | Rename a file or folder on disk | admin | files: write | `files rename` ✅ |
| POST | `/api/files/upload` | Upload a single file into a library folder | admin | files: write | `files upload` ✅ |

## library

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/about` | Build information |  | library: read | 🔒 24-hour version check (all commands), forced at login |
| POST | `/api/cancel-scan` | Cancel running scan | admin | library: write | `library cancel-scan` ✅ |
| GET | `/api/changelog` | Release changelog |  | library: read | — |
| GET | `/api/latest-release` | Latest published release |  | library: read | — |
| POST | `/api/maintenance/cleanup-missing` | Remove DB entries for missing files | admin | library: write | `library cleanup-missing` ✅ |
| POST | `/api/rescan` | Rescan and reindex library | admin | library: write | `library rescan` ✅ |
| GET | `/api/scan-status` | Scan status | admin | library: read | `library scan-status` ✅ |

## logs

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/logs` | Application logs | admin | logs: read | `logs` ✅ |

## lookups

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/dice-materials` | List all dice/materials |  | lookups: read | `dice-materials list` ✅ |
| POST | `/api/dice-materials` | Create a custom dice/material (admin) | admin | lookups: write | `dice-materials create` ✅ |
| DELETE | `/api/dice-materials/{material_id}` | Delete a dice/material (admin; blocked if in use unless force=true) | admin | lookups: write | `dice-materials delete` ✅ |
| GET | `/api/genres` | List all genres (tiered) |  | lookups: read | `genres list` ✅ |
| POST | `/api/genres` | Create a custom genre (admin) | admin | lookups: write | `genres create` ✅ |
| DELETE | `/api/genres/{genre_id}` | Delete a genre (admin; blocked if in use unless force=true) | admin | lookups: write | `genres delete` ✅ |
| GET | `/api/licenses` | List all licenses |  | lookups: read | `licenses list` ✅ |
| POST | `/api/licenses` | Create a custom license (admin) | admin | lookups: write | `licenses create` ✅ |
| DELETE | `/api/licenses/{license_id}` | Delete a license (admin; blocked if in use unless force=true) | admin | lookups: write | `licenses delete` ✅ |
| GET | `/api/parent-systems` | List all parent systems |  | lookups: read | `parent-systems list` ✅ |
| POST | `/api/parent-systems` | Create a custom parent system (admin) | admin | lookups: write | `parent-systems create` ✅ |
| DELETE | `/api/parent-systems/{parent_id}` | Delete a parent system (admin; blocked if in use unless force=true) | admin | lookups: write | `parent-systems delete` ✅ |
| GET | `/api/system-families` | List all system families |  | lookups: read | `system-families list` ✅ |
| POST | `/api/system-families` | Create a custom system family (admin) | admin | lookups: write | `system-families create` ✅ |
| DELETE | `/api/system-families/{family_id}` | Delete a system family (admin; blocked if in use unless force=true) | admin | lookups: write | `system-families delete` ✅ |

## maintenance

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/health` | Liveness/readiness probe |  | maintenance: read | — |
| POST | `/api/maintenance/sidecars/export` | Write metadata sidecars for the whole library | admin | maintenance: write | `sidecars export` ✅ |
| GET | `/api/maintenance/sidecars/settings` | Read metadata sidecar export settings | admin | maintenance: read | `sidecars settings get` ✅ |
| PUT | `/api/maintenance/sidecars/settings` | Configure metadata sidecar export | admin | maintenance: write | `sidecars settings set` ✅ |

## maps

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/map-folders` | List map folders | not guest | maps: read | `maps folders list` ✅ |
| PATCH | `/api/map-folders` | Set tags on a map folder | gm or admin | maps: write | `maps folders set` ✅ |
| POST | `/api/map-folders/bulk` | Bulk set map folder tags | gm or admin | maps: write | `maps folders batch-set` ✅ |
| GET | `/api/maps` | List maps | not guest | maps: read | `maps list` ✅ |
| POST | `/api/maps/bulk` | Bulk update maps | gm or admin | maps: write | `maps batch-update` ✅ |
| POST | `/api/maps/bulk/tags` | Bulk add tags to maps | gm or admin | maps: write | `maps batch-tag` ✅ |
| GET | `/api/maps/{map_id}` | Get a map |  | maps: read | `maps get` ✅ |
| PATCH | `/api/maps/{map_id}` | Update map metadata | gm or admin | maps: write | `maps update` ✅ |
| GET | `/api/maps/{map_id}/export.uvtt` | Export a map as Universal VTT |  | maps: read | `maps vtt export` ✅ |
| GET | `/api/maps/{map_id}/file` | Download map file |  | maps: read | `maps file` ✅ |
| GET | `/api/maps/{map_id}/page/{page_num}` | Render a map page |  | maps: read | `maps page` ✅ |
| GET | `/api/maps/{map_id}/thumbnail` | Map thumbnail |  | maps: read | `maps thumbnail` ✅ |
| GET | `/api/maps/{map_id}/vtt/authoring` | Get authored Universal VTT geometry |  | maps: read | — |
| PUT | `/api/maps/{map_id}/vtt/authoring` | Replace authored Universal VTT geometry | gm or admin | maps: write | — |
| GET | `/api/maps/{map_id}/vtt/data` | Universal VTT grid and feature data |  | maps: read | `maps vtt data` ✅ |
| GET | `/api/maps/{map_id}/vtt/image` | Universal VTT map image |  | maps: read | `maps vtt image` ✅ |

## models

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/model-folders` | List model folders | not guest | models: read | `models folders list` ✅ |
| PATCH | `/api/model-folders` | Set tags on a model folder | gm or admin | models: write | `models folders set` ✅ |
| POST | `/api/model-folders/bulk` | Bulk set model folder tags | gm or admin | models: write | `models folders batch-set` ✅ |
| GET | `/api/models` | List 3D models | not guest | models: read | `models list` ✅ |
| POST | `/api/models/bulk` | Bulk update models | gm or admin | models: write | `models batch-update` ✅ |
| POST | `/api/models/bulk/tags` | Bulk add tags to models | gm or admin | models: write | `models batch-tag` ✅ |
| GET | `/api/models/{model_id}` | Get a 3D model |  | models: read | `models get` ✅ |
| PATCH | `/api/models/{model_id}` | Update model metadata | gm or admin | models: write | `models update` ✅ |
| GET | `/api/models/{model_id}/file` | Download model file |  | models: read | `models file` ✅ |
| GET | `/api/models/{model_id}/thumbnail` | Model thumbnail |  | models: read | `models thumbnail` ✅ |

## saved-filters

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/saved-filters` | List the user's saved filters |  | personal: read | — |
| POST | `/api/saved-filters` | Create/overwrite a saved filter |  | personal: write | — |
| PATCH | `/api/saved-filters/{filter_id}` | Rename, re-save state, or set default |  | personal: write | — |
| DELETE | `/api/saved-filters/{filter_id}` | Delete a saved filter |  | personal: write | — |

## search

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/search` | Full-text search | not guest | search: read | `search` ✅ |
| GET | `/api/search/fields` | Searchable fields | not guest | search: read | `search fields` ✅ |

## settings

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/settings` | Get app settings | admin | settings: read | — |
| PATCH | `/api/settings` | Update app settings | admin | settings: write | — |
| GET | `/api/settings/ui` | UI settings (any authenticated user) |  | settings: read | — |

## stats

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/stats` | Library statistics |  | stats: read | `library stats` ✅ |

## systems

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/systems` | List all game systems | not guest | systems: read | `systems list` ✅ |
| POST | `/api/systems/bulk` | Bulk update game systems | gm or admin | systems: write | `systems batch-update` ✅ |
| POST | `/api/systems/bulk/tags` | Bulk add tags to game systems | gm or admin | systems: write | `systems batch-tag` ✅ |
| GET | `/api/systems/{system_id}` | Get a game system | not guest | systems: read | `systems get` ✅ |
| PATCH | `/api/systems/{system_id}` | Update game system metadata | gm or admin | systems: write | `systems update` ✅ |
| GET | `/api/systems/{system_id}/book-folders` | List book folders |  | systems: read | `systems book-folders list` ✅ |
| PATCH | `/api/systems/{system_id}/book-folders` | Set tags on a book folder | gm or admin | systems: write | `systems book-folders set` ✅ |
| DELETE | `/api/systems/{system_id}/book-folders` | Delete a book folder | gm or admin | systems: write | `systems book-folders delete` ✅ |
| GET | `/api/systems/{system_id}/cover` | System cover image |  | systems: read | `systems cover get` ✅ |
| POST | `/api/systems/{system_id}/cover` | Upload a system cover | gm or admin | systems: write | `systems cover upload` ✅ |
| DELETE | `/api/systems/{system_id}/cover` | Remove an uploaded system cover | gm or admin | systems: write | `systems cover delete` ✅ |
| POST | `/api/systems/{system_id}/cover/from-source` | Set a system cover from an existing image | gm or admin | systems: write | `systems cover from-source` ✅ |
| POST | `/api/systems/{system_id}/metadata-fetch` | Fetch metadata for review | gm or admin | systems: read | `systems metadata-fetch` ✅ |
| POST | `/api/systems/{system_id}/metadata-search` | Search a metadata source | gm or admin | systems: read | `systems metadata-search` ✅ |
| GET | `/api/systems/{system_id}/metadata-sources` | List metadata sources | gm or admin | systems: read | `systems metadata-sources` ✅ |

## tags

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/tags` | List tags |  | tags: read | `tags list` ✅ |
| POST | `/api/tags` | Create a tag | gm or admin | tags: write | `tags create` ✅ |
| PATCH | `/api/tags/{internal}` | Rename a tag's display value | gm or admin | tags: write | `tags rename` ✅ |
| DELETE | `/api/tags/{internal}` | Delete a tag | gm or admin | tags: write | `tags delete` ✅ |
| GET | `/api/tags/{internal}/items` | Items carrying a tag |  | tags: read | `tags items` ✅ |
| POST | `/api/tags/{internal}/merge` | Merge a tag into another | gm or admin | tags: write | `tags merge` ✅ |

## themes

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/themes` | List installed themes |  | personal: read | — |
| POST | `/api/themes` | Install a pasted or uploaded theme |  | personal: write | — |
| GET | `/api/themes/browse` | Browse the community catalogue |  | personal: read | — |
| POST | `/api/themes/install/{theme_id}` | Install a theme from the catalogue |  | personal: write | — |
| PUT | `/api/themes/selection` | Set the active mode and theme |  | personal: write | — |
| PUT | `/api/themes/source` | Set the catalogue URL (admin) | admin | personal: write | — |
| DELETE | `/api/themes/{theme_id}` | Uninstall a theme |  | personal: write | — |

## token-frames

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/token-frames` | List token frames | not guest | tokens: read | — |
| GET | `/api/token-frames/{frame_id}/file` | Serve a token frame image | not guest | tokens: read | — |

## tokens

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/token-folders` | List token folders | not guest | tokens: read | `tokens folders list` ✅ |
| PATCH | `/api/token-folders` | Set tags on a token folder | gm or admin | tokens: write | `tokens folders set` ✅ |
| POST | `/api/token-folders/bulk` | Bulk set token folder tags | gm or admin | tokens: write | `tokens folders batch-set` ✅ |
| GET | `/api/tokens` | List tokens | not guest | tokens: read | `tokens list` ✅ |
| POST | `/api/tokens/bulk` | Bulk update tokens | gm or admin | tokens: write | `tokens batch-update` ✅ |
| POST | `/api/tokens/bulk/tags` | Bulk add tags to tokens | gm or admin | tokens: write | `tokens batch-tag` ✅ |
| GET | `/api/tokens/{token_id}` | Get a token |  | tokens: read | `tokens get` ✅ |
| PATCH | `/api/tokens/{token_id}` | Update token metadata | gm or admin | tokens: write | `tokens update` ✅ |
| GET | `/api/tokens/{token_id}/file` | Download token file |  | tokens: read | `tokens file` ✅ |
| GET | `/api/tokens/{token_id}/thumbnail` | Token thumbnail |  | tokens: read | `tokens thumbnail` ✅ |

## users

| Method | Path | Description | Perm | Key | CLI |
|--------|------|-------------|------|-----|-----|
| GET | `/api/users` | List all users | admin | users: read | — |
| POST | `/api/users` | Create a user | admin | users: write | — |
| GET | `/api/users/guests` | List guest accounts | admin | users: read | — |
| DELETE | `/api/users/me` | Delete own account |  | — | — |
| GET | `/api/users/me/opds` | Get OPDS feed status |  | — | — |
| DELETE | `/api/users/me/opds` | Revoke OPDS token |  | — | — |
| POST | `/api/users/me/opds/generate` | Generate/regenerate OPDS token |  | — | — |
| PATCH | `/api/users/me/password` | Change own password |  | — | — |
| PATCH | `/api/users/me/preferences` | Update own preferences |  | users: write | — |
| PATCH | `/api/users/{user_id}` | Update user role or password | admin | users: write | — |
| DELETE | `/api/users/{user_id}` | Delete a user | admin | users: write | — |
| GET | `/api/users/{user_id}/access-grants` | List a user's access grants | admin | users: read | — |
| POST | `/api/users/{user_id}/access-grants` | Grant a user access to a restricted system or book | admin | users: write | — |
| DELETE | `/api/users/{user_id}/access-grants/{grant_id}` | Revoke an access grant | admin | users: write | — |
| POST | `/api/users/{user_id}/convert` | Convert a guest to a permanent user | admin | users: write | — |
| POST | `/api/users/{user_id}/merge` | Merge guest accounts into one account | admin | users: write | — |
