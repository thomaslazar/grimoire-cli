# Grimoire API coverage

Map of every Grimoire HTTP API operation and the `grimoire-cli` command (if any) that implements it.

- **Reference:** spec fetched live from the pinned stack's `/api/openapi.json` (v1.5.6, 166 paths, 220 operations) and the upstream source at `temp/grimoire/backend/routers/`. Tested range: `1.5.6` only (`GrimoireApiClient.cs`).
- **Perm** column uses Grimoire's roles (`admin` / `gm or admin` / `not guest`); blank = any authenticated user. `?` = a dependency this script could not resolve.
- ✅ = covered by a CLI command · — = not implemented · 🔒 = internal-only (no user-facing verb); 🔒 rows never count as covered.
- **Regenerate with `tools/generate-api-coverage.py`; update `IMPLEMENTED` there in the same PR as any change to which endpoints the CLI calls.**

## Coverage summary

| Tag | Covered / Total |
|-----|-----------------|
| (untagged) | 0 / 1 |
| addons | 7 / 7 |
| audio | 0 / 10 |
| auth | 2 / 10 |
| bookmarks | 0 / 4 |
| books | 10 / 16 |
| campaigns | 0 / 81 |
| downloads | 0 / 1 |
| export | 0 / 1 |
| favorites | 0 / 3 |
| library | 3 / 6 |
| logs | 0 / 1 |
| lookups | 0 / 15 |
| maintenance | 1 / 2 |
| maps | 0 / 11 |
| saved-filters | 0 / 4 |
| search | 0 / 1 |
| settings | 0 / 5 |
| systems | 8 / 13 |
| tags | 0 / 6 |
| tokens | 0 / 10 |
| users | 0 / 12 |
| **Total** | **31 / 220** |

1 operation(s) are internal-only (🔒) and excluded from covered counts.

## (untagged)

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/{full_path}` | Serve Frontend |  | — |

## addons

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/addons` | List add-ons | admin | `addons list` ✅ |
| POST | `/api/addons/refresh` | Refresh the add-on index | admin | `addons refresh` ✅ |
| PATCH | `/api/addons/settings` | Update add-on settings | admin | `addons settings` ✅ |
| POST | `/api/addons/update-all` | Update all add-ons | admin | `addons upgrade-all` ✅ |
| PATCH | `/api/addons/{addon_id}` | Enable, disable, or approve an add-on | admin | `addons update` ✅ |
| DELETE | `/api/addons/{addon_id}` | Uninstall an add-on | admin | `addons uninstall` ✅ |
| POST | `/api/addons/{addon_id}/install` | Install or update an add-on | admin | `addons install` ✅ |

## audio

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/audio` | List audio | not guest | — |
| GET | `/api/audio-folders` | List audio folders | not guest | — |
| PATCH | `/api/audio-folders` | Set tags on an audio folder | gm or admin | — |
| POST | `/api/audio-folders/bulk` | Bulk set audio folder tags | gm or admin | — |
| POST | `/api/audio/bulk` | Bulk update audio tracks | gm or admin | — |
| POST | `/api/audio/bulk/tags` | Bulk add tags to audio tracks | gm or admin | — |
| GET | `/api/audio/{audio_id}` | Get an audio track |  | — |
| PATCH | `/api/audio/{audio_id}` | Update audio metadata | gm or admin | — |
| GET | `/api/audio/{audio_id}/artwork` | Audio artwork |  | — |
| GET | `/api/audio/{audio_id}/file` | Stream/download audio file |  | — |

## auth

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/auth/config` | Public auth configuration |  | — |
| POST | `/api/auth/guest-login` | Log in as a guest |  | — |
| POST | `/api/auth/login` | Log in |  | `login` ✅ |
| POST | `/api/auth/logout` | Log out |  | — |
| GET | `/api/auth/me` | Get current user |  | `me` ✅ |
| GET | `/api/auth/openid/callback` | OIDC callback |  | — |
| POST | `/api/auth/openid/discover` | Fetch OIDC discovery document | admin | — |
| GET | `/api/auth/openid/login` | Start an OIDC login |  | — |
| POST | `/api/auth/setup` | First-run admin setup |  | — |
| GET | `/api/auth/status` | Check initialization status |  | — |

## bookmarks

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/bookmarks` | List bookmarks for a book |  | — |
| POST | `/api/bookmarks` | Create a bookmark |  | — |
| PATCH | `/api/bookmarks/{bookmark_id}` | Update bookmark label |  | — |
| DELETE | `/api/bookmarks/{bookmark_id}` | Delete a bookmark |  | — |

## books

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/books` | List books | not guest | `books list` ✅ |
| POST | `/api/books/bulk` | Bulk update books | gm or admin | `books batch-update` ✅ |
| POST | `/api/books/bulk/tags` | Bulk add tags to books | gm or admin | `books batch-tag` ✅ |
| GET | `/api/books/{book_id}` | Get a book |  | `books get` ✅ |
| PATCH | `/api/books/{book_id}` | Update book metadata | gm or admin | `books update` ✅ |
| GET | `/api/books/{book_id}/file` | Download book file |  | — |
| POST | `/api/books/{book_id}/metadata-fetch` | Fetch metadata for review | gm or admin | `books metadata-fetch` ✅ |
| POST | `/api/books/{book_id}/metadata-search` | Search a metadata source | gm or admin | `books metadata-search` ✅ |
| GET | `/api/books/{book_id}/metadata-sources` | List metadata sources | gm or admin | `books metadata-sources` ✅ |
| GET | `/api/books/{book_id}/page/{page_num}` | Render a PDF page as WebP |  | — |
| GET | `/api/books/{book_id}/page/{page_num}/text` | Get page text |  | — |
| GET | `/api/books/{book_id}/page/{page_num}/words` | Get page word bounding boxes |  | — |
| POST | `/api/books/{book_id}/reindex` | Re-run OCR on a book (optional DPI override) | gm or admin | `books reindex` ✅ |
| POST | `/api/books/{book_id}/rescan` | Re-read a book from disk and rebuild its search index | gm or admin | `books rescan` ✅ |
| GET | `/api/books/{book_id}/thumbnail` | Book cover thumbnail |  | — |
| GET | `/api/books/{book_id}/toc` | PDF table of contents |  | — |

## campaigns

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/campaigns` | List campaigns for the current user |  | — |
| POST | `/api/campaigns` | Create a campaign |  | — |
| GET | `/api/campaigns/admin/by-user/{user_id}` | Admin: list campaigns owned by a user (read-only, minimal fields) | admin | — |
| GET | `/api/campaigns/invites` | List the current user's pending campaign invitations |  | — |
| GET | `/api/campaigns/resources/search` | Search books, maps, and tokens by name |  | — |
| GET | `/api/campaigns/resources/suggested/{system_id}` | Suggested resources (system books) for the create wizard |  | — |
| GET | `/api/campaigns/{campaign_id}` | Get a campaign |  | — |
| PATCH | `/api/campaigns/{campaign_id}` | Update a campaign |  | — |
| DELETE | `/api/campaigns/{campaign_id}` | Delete a campaign |  | — |
| PUT | `/api/campaigns/{campaign_id}/archive` | Archive or unarchive a campaign |  | — |
| GET | `/api/campaigns/{campaign_id}/availability` | Get availability chart for upcoming sessions |  | — |
| PUT | `/api/campaigns/{campaign_id}/availability/{session_date}` | Set availability for a session date |  | — |
| PUT | `/api/campaigns/{campaign_id}/availability/{session_date}/cancel` | GM: cancel or uncancel a session date |  | — |
| POST | `/api/campaigns/{campaign_id}/banner` | Upload campaign banner |  | — |
| GET | `/api/campaigns/{campaign_id}/banner` | Get campaign banner image |  | — |
| DELETE | `/api/campaigns/{campaign_id}/banner` | Remove campaign banner |  | — |
| GET | `/api/campaigns/{campaign_id}/categories` | List categories (optionally filtered by kind) |  | — |
| POST | `/api/campaigns/{campaign_id}/categories` | Create a category |  | — |
| PUT | `/api/campaigns/{campaign_id}/categories/reorder` | Reorder categories |  | — |
| PATCH | `/api/campaigns/{campaign_id}/categories/{category_id}` | Rename a category |  | — |
| DELETE | `/api/campaigns/{campaign_id}/categories/{category_id}` | Delete a category (mode: uncategorize \| delete_items) |  | — |
| POST | `/api/campaigns/{campaign_id}/convert-to-group` | Convert a personal campaign into a GM-run group campaign |  | — |
| GET | `/api/campaigns/{campaign_id}/eligible-members` | List users that can be invited |  | — |
| POST | `/api/campaigns/{campaign_id}/files` | Upload a campaign file (GM); links it as a resource |  | — |
| GET | `/api/campaigns/{campaign_id}/files/{file_id}` | Download a campaign file (honours resource visibility) |  | — |
| POST | `/api/campaigns/{campaign_id}/guests` | Create a guest invite code for a GM campaign |  | — |
| GET | `/api/campaigns/{campaign_id}/guests` | List a campaign's guests and their invite codes |  | — |
| DELETE | `/api/campaigns/{campaign_id}/guests/{member_id}` | Remove a guest (deletes the guest account) |  | — |
| POST | `/api/campaigns/{campaign_id}/guests/{member_id}/regenerate` | Regenerate a guest's invite code |  | — |
| GET | `/api/campaigns/{campaign_id}/guests/{member_id}/share-template` | Get share text and links for a guest invite code |  | — |
| POST | `/api/campaigns/{campaign_id}/images` | Upload an image (GM); links it as an image resource for note embedding |  | — |
| POST | `/api/campaigns/{campaign_id}/invite` | Invite a player to a GM campaign |  | — |
| POST | `/api/campaigns/{campaign_id}/members/{member_id}/art` | Upload a member's character art |  | — |
| GET | `/api/campaigns/{campaign_id}/members/{member_id}/art` | Get a member's character art |  | — |
| DELETE | `/api/campaigns/{campaign_id}/members/{member_id}/art` | Remove a member's character art |  | — |
| POST | `/api/campaigns/{campaign_id}/members/{member_id}/sheet` | Upload a member's character sheet |  | — |
| GET | `/api/campaigns/{campaign_id}/members/{member_id}/sheet` | Download a member's character sheet |  | — |
| DELETE | `/api/campaigns/{campaign_id}/members/{member_id}/sheet` | Remove a member's character sheet |  | — |
| POST | `/api/campaigns/{campaign_id}/members/{member_id}/sheet/duplicate` | Duplicate a blank sheet into a member's slot |  | — |
| PATCH | `/api/campaigns/{campaign_id}/members/{user_id}` | Accept or decline an invitation |  | — |
| DELETE | `/api/campaigns/{campaign_id}/members/{user_id}` | Remove a member |  | — |
| PUT | `/api/campaigns/{campaign_id}/resource-group-order` | Set the resource panel's group display order (categories + type groups) |  | — |
| GET | `/api/campaigns/{campaign_id}/resources` | List linked resources |  | — |
| POST | `/api/campaigns/{campaign_id}/resources` | Link a resource to a campaign |  | — |
| POST | `/api/campaigns/{campaign_id}/resources/bulk` | Link many resources at once |  | — |
| PUT | `/api/campaigns/{campaign_id}/resources/reorder` | Reorder resources (drag-and-drop) |  | — |
| PATCH | `/api/campaigns/{campaign_id}/resources/{resource_id}` | Update resource visibility/category |  | — |
| DELETE | `/api/campaigns/{campaign_id}/resources/{resource_id}` | Unlink a resource |  | — |
| GET | `/api/campaigns/{campaign_id}/schedule` | Get campaign schedule and next sessions |  | — |
| PUT | `/api/campaigns/{campaign_id}/schedule` | Create or update campaign schedule |  | — |
| DELETE | `/api/campaigns/{campaign_id}/schedule` | Remove campaign schedule |  | — |
| GET | `/api/campaigns/{campaign_id}/sessions` | List session notes |  | — |
| POST | `/api/campaigns/{campaign_id}/sessions` | Create a session note |  | — |
| GET | `/api/campaigns/{campaign_id}/sessions/search` | Search session notes |  | — |
| GET | `/api/campaigns/{campaign_id}/sessions/{session_id}` | Get a session note with all notes |  | — |
| PATCH | `/api/campaigns/{campaign_id}/sessions/{session_id}` | Update session title |  | — |
| DELETE | `/api/campaigns/{campaign_id}/sessions/{session_id}` | Delete a session note |  | — |
| PUT | `/api/campaigns/{campaign_id}/sessions/{session_id}/notes/gm` | Save GM notes (owner only) |  | — |
| PUT | `/api/campaigns/{campaign_id}/sessions/{session_id}/notes/player` | Save own player note |  | — |
| GET | `/api/campaigns/{campaign_id}/sheet-sources` | List blank sheets a member can duplicate |  | — |
| GET | `/api/campaigns/{campaign_id}/wiki` | List visible wiki pages |  | — |
| POST | `/api/campaigns/{campaign_id}/wiki` | Create a wiki page |  | — |
| GET | `/api/campaigns/{campaign_id}/wiki/export` | Export campaign wiki (md zip or json bundle) |  | — |
| POST | `/api/campaigns/{campaign_id}/wiki/import` | Import wiki pages (markdown / json / LegendKeeper) |  | — |
| PUT | `/api/campaigns/{campaign_id}/wiki/reorder` | Reorder wiki pages (drag-and-drop) |  | — |
| GET | `/api/campaigns/{campaign_id}/wiki/search` | Search wiki pages |  | — |
| GET | `/api/campaigns/{campaign_id}/wiki/templates` | List the campaign's note templates |  | — |
| POST | `/api/campaigns/{campaign_id}/wiki/templates` | Write a new note template |  | — |
| GET | `/api/campaigns/{campaign_id}/wiki/templates/browse` | Browse the community note-template catalogue |  | — |
| POST | `/api/campaigns/{campaign_id}/wiki/templates/download/{template_id}` | Download a community note template into the campaign |  | — |
| PUT | `/api/campaigns/{campaign_id}/wiki/templates/source` | Set the note-template catalogue URL |  | — |
| POST | `/api/campaigns/{campaign_id}/wiki/templates/upload` | Add a note template from an uploaded .md file |  | — |
| GET | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}` | Get a note template incl. its body |  | — |
| PATCH | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}` | Edit a note template |  | — |
| DELETE | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}` | Delete a note template |  | — |
| GET | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}/export` | Export a note template as a .zip folder |  | — |
| POST | `/api/campaigns/{campaign_id}/wiki/templates/{template_id}/use` | Create a wiki page from a note template |  | — |
| GET | `/api/campaigns/{campaign_id}/wiki/titles` | Wiki page titles for [[link]] autocomplete |  | — |
| GET | `/api/campaigns/{campaign_id}/wiki/{page_id}` | Get a wiki page |  | — |
| PATCH | `/api/campaigns/{campaign_id}/wiki/{page_id}` | Update a wiki page |  | — |
| DELETE | `/api/campaigns/{campaign_id}/wiki/{page_id}` | Delete a wiki page |  | — |

## downloads

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/downloads/archive` | Download an archive of files |  | — |

## export

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/export/tags` | Export all tag data as JSON | admin | — |

## favorites

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/favorites` | List current user's favorites |  | — |
| POST | `/api/favorites` | Add a favorite |  | — |
| DELETE | `/api/favorites/{item_type}/{item_id}` | Remove a favorite |  | — |

## library

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/about` | Build information |  | 🔒 24-hour version check (all commands), forced at login |
| POST | `/api/cancel-scan` | Cancel running scan | admin | `library cancel-scan` ✅ |
| GET | `/api/latest-release` | Latest published release |  | — |
| POST | `/api/rescan` | Rescan and reindex library | admin | `library rescan` ✅ |
| GET | `/api/scan-status` | Scan status | admin | `library scan-status` ✅ |
| GET | `/api/stats` | Library statistics |  | — |

## logs

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/logs` | Application logs | admin | — |

## lookups

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/dice-materials` | List all dice/materials |  | — |
| POST | `/api/dice-materials` | Create a custom dice/material (admin) | admin | — |
| DELETE | `/api/dice-materials/{material_id}` | Delete a dice/material (admin; blocked if in use unless force=true) | admin | — |
| GET | `/api/genres` | List all genres (tiered) |  | — |
| POST | `/api/genres` | Create a custom genre (admin) | admin | — |
| DELETE | `/api/genres/{genre_id}` | Delete a genre (admin; blocked if in use unless force=true) | admin | — |
| GET | `/api/licenses` | List all licenses |  | — |
| POST | `/api/licenses` | Create a custom license (admin) | admin | — |
| DELETE | `/api/licenses/{license_id}` | Delete a license (admin; blocked if in use unless force=true) | admin | — |
| GET | `/api/parent-systems` | List all parent systems |  | — |
| POST | `/api/parent-systems` | Create a custom parent system (admin) | admin | — |
| DELETE | `/api/parent-systems/{parent_id}` | Delete a parent system (admin; blocked if in use unless force=true) | admin | — |
| GET | `/api/system-families` | List all system families |  | — |
| POST | `/api/system-families` | Create a custom system family (admin) | admin | — |
| DELETE | `/api/system-families/{family_id}` | Delete a system family (admin; blocked if in use unless force=true) | admin | — |

## maintenance

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/health` | Liveness/readiness probe |  | — |
| POST | `/api/maintenance/cleanup-missing` | Remove DB entries for missing files | admin | `library cleanup-missing` ✅ |

## maps

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/map-folders` | List map folders | not guest | — |
| PATCH | `/api/map-folders` | Set tags on a map folder | gm or admin | — |
| POST | `/api/map-folders/bulk` | Bulk set map folder tags | gm or admin | — |
| GET | `/api/maps` | List maps | not guest | — |
| POST | `/api/maps/bulk` | Bulk update maps | gm or admin | — |
| POST | `/api/maps/bulk/tags` | Bulk add tags to maps | gm or admin | — |
| GET | `/api/maps/{map_id}` | Get a map |  | — |
| PATCH | `/api/maps/{map_id}` | Update map metadata | gm or admin | — |
| GET | `/api/maps/{map_id}/file` | Download map file |  | — |
| GET | `/api/maps/{map_id}/page/{page_num}` | Render a map page |  | — |
| GET | `/api/maps/{map_id}/thumbnail` | Map thumbnail |  | — |

## saved-filters

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/saved-filters` | List the user's saved filters |  | — |
| POST | `/api/saved-filters` | Create/overwrite a saved filter |  | — |
| PATCH | `/api/saved-filters/{filter_id}` | Rename, re-save state, or set default |  | — |
| DELETE | `/api/saved-filters/{filter_id}` | Delete a saved filter |  | — |

## search

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/search` | Full-text search | not guest | — |

## settings

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/settings` | Get app settings | admin | — |
| PATCH | `/api/settings` | Update app settings | admin | — |
| DELETE | `/api/settings/api-key` | Revoke the stats API key | admin | — |
| POST | `/api/settings/api-key/generate` | Generate a new stats API key | admin | — |
| GET | `/api/settings/ui` | UI settings (any authenticated user) |  | — |

## systems

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/systems` | List all game systems | not guest | `systems list` ✅ |
| POST | `/api/systems/bulk` | Bulk update game systems | gm or admin | `systems batch-update` ✅ |
| POST | `/api/systems/bulk/tags` | Bulk add tags to game systems | gm or admin | `systems batch-tag` ✅ |
| GET | `/api/systems/{system_id}` | Get a game system | not guest | `systems get` ✅ |
| PATCH | `/api/systems/{system_id}` | Update game system metadata | gm or admin | `systems update` ✅ |
| GET | `/api/systems/{system_id}/book-folders` | List book folders |  | — |
| PATCH | `/api/systems/{system_id}/book-folders` | Set tags on a book folder | gm or admin | — |
| GET | `/api/systems/{system_id}/cover` | System cover image |  | — |
| POST | `/api/systems/{system_id}/cover` | Upload a system cover | gm or admin | — |
| DELETE | `/api/systems/{system_id}/cover` | Remove an uploaded system cover | gm or admin | — |
| POST | `/api/systems/{system_id}/metadata-fetch` | Fetch metadata for review | gm or admin | `systems metadata-fetch` ✅ |
| POST | `/api/systems/{system_id}/metadata-search` | Search a metadata source | gm or admin | `systems metadata-search` ✅ |
| GET | `/api/systems/{system_id}/metadata-sources` | List metadata sources | gm or admin | `systems metadata-sources` ✅ |

## tags

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/tags` | List tags |  | — |
| POST | `/api/tags` | Create a tag | gm or admin | — |
| PATCH | `/api/tags/{internal}` | Rename a tag's display value | gm or admin | — |
| DELETE | `/api/tags/{internal}` | Delete a tag | gm or admin | — |
| GET | `/api/tags/{internal}/items` | Items carrying a tag |  | — |
| POST | `/api/tags/{internal}/merge` | Merge a tag into another | gm or admin | — |

## tokens

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/token-folders` | List token folders | not guest | — |
| PATCH | `/api/token-folders` | Set tags on a token folder | gm or admin | — |
| POST | `/api/token-folders/bulk` | Bulk set token folder tags | gm or admin | — |
| GET | `/api/tokens` | List tokens | not guest | — |
| POST | `/api/tokens/bulk` | Bulk update tokens | gm or admin | — |
| POST | `/api/tokens/bulk/tags` | Bulk add tags to tokens | gm or admin | — |
| GET | `/api/tokens/{token_id}` | Get a token |  | — |
| PATCH | `/api/tokens/{token_id}` | Update token metadata | gm or admin | — |
| GET | `/api/tokens/{token_id}/file` | Download token file |  | — |
| GET | `/api/tokens/{token_id}/thumbnail` | Token thumbnail |  | — |

## users

| Method | Path | Description | Perm | CLI |
|--------|------|-------------|------|-----|
| GET | `/api/users` | List all users | admin | — |
| POST | `/api/users` | Create a user | admin | — |
| GET | `/api/users/guests` | List guest accounts | admin | — |
| DELETE | `/api/users/me` | Delete own account |  | — |
| GET | `/api/users/me/opds` | Get OPDS feed status |  | — |
| DELETE | `/api/users/me/opds` | Revoke OPDS token |  | — |
| POST | `/api/users/me/opds/generate` | Generate/regenerate OPDS token |  | — |
| PATCH | `/api/users/me/password` | Change own password |  | — |
| PATCH | `/api/users/me/preferences` | Update own preferences |  | — |
| PATCH | `/api/users/{user_id}` | Update user role or password | admin | — |
| DELETE | `/api/users/{user_id}` | Delete a user | admin | — |
| POST | `/api/users/{user_id}/convert` | Convert a guest to a permanent user | admin | — |
