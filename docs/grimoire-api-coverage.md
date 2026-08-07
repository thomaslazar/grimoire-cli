# Grimoire API coverage

Generated from the v1.5.4 spec (130 paths, 178 operations across `GET` /
`POST` / `PATCH` / `PUT` / `DELETE`), grouped by the spec's own OpenAPI tags.
**Update this file in the same PR as any change to which endpoints
`grimoire-cli` calls.**

Four operations are implemented; everything else is `—`.

### (frontend)

| Endpoint | Command |
|---|---|
| `GET /{full_path}` | — |

### audio

| Endpoint | Command |
|---|---|
| `GET /api/audio` | — |
| `GET /api/audio-folders` | — |
| `PATCH /api/audio-folders` | — |
| `GET /api/audio/{audio_id}` | — |
| `PATCH /api/audio/{audio_id}` | — |
| `GET /api/audio/{audio_id}/artwork` | — |
| `GET /api/audio/{audio_id}/file` | — |

### auth

| Endpoint | Command |
|---|---|
| `GET /api/auth/config` | — |
| `POST /api/auth/guest-login` | — |
| `POST /api/auth/login` | `login` |
| `POST /api/auth/logout` | — |
| `GET /api/auth/me` | — |
| `GET /api/auth/openid/callback` | — |
| `POST /api/auth/openid/discover` | — |
| `GET /api/auth/openid/login` | — |
| `POST /api/auth/setup` | — |
| `GET /api/auth/status` | — |

### bookmarks

| Endpoint | Command |
|---|---|
| `GET /api/bookmarks` | — |
| `POST /api/bookmarks` | — |
| `PATCH /api/bookmarks/{bookmark_id}` | — |
| `DELETE /api/bookmarks/{bookmark_id}` | — |

### books

| Endpoint | Command |
|---|---|
| `GET /api/books` | — |
| `GET /api/books/{book_id}` | — |
| `PATCH /api/books/{book_id}` | — |
| `GET /api/books/{book_id}/file` | — |
| `GET /api/books/{book_id}/page/{page_num}` | — |
| `GET /api/books/{book_id}/page/{page_num}/text` | — |
| `GET /api/books/{book_id}/page/{page_num}/words` | — |
| `POST /api/books/{book_id}/reindex` | — |
| `POST /api/books/{book_id}/rescan` | — |
| `GET /api/books/{book_id}/thumbnail` | — |
| `GET /api/books/{book_id}/toc` | — |

### campaigns

| Endpoint | Command |
|---|---|
| `GET /api/campaigns` | — |
| `POST /api/campaigns` | — |
| `GET /api/campaigns/admin/by-user/{user_id}` | — |
| `GET /api/campaigns/invites` | — |
| `GET /api/campaigns/resources/search` | — |
| `GET /api/campaigns/resources/suggested/{system_id}` | — |
| `GET /api/campaigns/{campaign_id}` | — |
| `PATCH /api/campaigns/{campaign_id}` | — |
| `DELETE /api/campaigns/{campaign_id}` | — |
| `GET /api/campaigns/{campaign_id}/availability` | — |
| `PUT /api/campaigns/{campaign_id}/availability/{session_date}` | — |
| `PUT /api/campaigns/{campaign_id}/availability/{session_date}/cancel` | — |
| `POST /api/campaigns/{campaign_id}/banner` | — |
| `GET /api/campaigns/{campaign_id}/banner` | — |
| `DELETE /api/campaigns/{campaign_id}/banner` | — |
| `GET /api/campaigns/{campaign_id}/categories` | — |
| `POST /api/campaigns/{campaign_id}/categories` | — |
| `PUT /api/campaigns/{campaign_id}/categories/reorder` | — |
| `PATCH /api/campaigns/{campaign_id}/categories/{category_id}` | — |
| `DELETE /api/campaigns/{campaign_id}/categories/{category_id}` | — |
| `GET /api/campaigns/{campaign_id}/eligible-members` | — |
| `POST /api/campaigns/{campaign_id}/files` | — |
| `GET /api/campaigns/{campaign_id}/files/{file_id}` | — |
| `POST /api/campaigns/{campaign_id}/guests` | — |
| `GET /api/campaigns/{campaign_id}/guests` | — |
| `DELETE /api/campaigns/{campaign_id}/guests/{member_id}` | — |
| `POST /api/campaigns/{campaign_id}/guests/{member_id}/regenerate` | — |
| `GET /api/campaigns/{campaign_id}/guests/{member_id}/share-template` | — |
| `POST /api/campaigns/{campaign_id}/images` | — |
| `POST /api/campaigns/{campaign_id}/invite` | — |
| `POST /api/campaigns/{campaign_id}/members/{member_id}/art` | — |
| `GET /api/campaigns/{campaign_id}/members/{member_id}/art` | — |
| `DELETE /api/campaigns/{campaign_id}/members/{member_id}/art` | — |
| `POST /api/campaigns/{campaign_id}/members/{member_id}/sheet` | — |
| `GET /api/campaigns/{campaign_id}/members/{member_id}/sheet` | — |
| `DELETE /api/campaigns/{campaign_id}/members/{member_id}/sheet` | — |
| `POST /api/campaigns/{campaign_id}/members/{member_id}/sheet/duplicate` | — |
| `PATCH /api/campaigns/{campaign_id}/members/{user_id}` | — |
| `DELETE /api/campaigns/{campaign_id}/members/{user_id}` | — |
| `PUT /api/campaigns/{campaign_id}/resource-group-order` | — |
| `GET /api/campaigns/{campaign_id}/resources` | — |
| `POST /api/campaigns/{campaign_id}/resources` | — |
| `POST /api/campaigns/{campaign_id}/resources/bulk` | — |
| `PUT /api/campaigns/{campaign_id}/resources/reorder` | — |
| `PATCH /api/campaigns/{campaign_id}/resources/{resource_id}` | — |
| `DELETE /api/campaigns/{campaign_id}/resources/{resource_id}` | — |
| `GET /api/campaigns/{campaign_id}/schedule` | — |
| `PUT /api/campaigns/{campaign_id}/schedule` | — |
| `DELETE /api/campaigns/{campaign_id}/schedule` | — |
| `GET /api/campaigns/{campaign_id}/sessions` | — |
| `POST /api/campaigns/{campaign_id}/sessions` | — |
| `GET /api/campaigns/{campaign_id}/sessions/search` | — |
| `GET /api/campaigns/{campaign_id}/sessions/{session_id}` | — |
| `PATCH /api/campaigns/{campaign_id}/sessions/{session_id}` | — |
| `DELETE /api/campaigns/{campaign_id}/sessions/{session_id}` | — |
| `PUT /api/campaigns/{campaign_id}/sessions/{session_id}/notes/gm` | — |
| `PUT /api/campaigns/{campaign_id}/sessions/{session_id}/notes/player` | — |
| `GET /api/campaigns/{campaign_id}/sheet-sources` | — |
| `GET /api/campaigns/{campaign_id}/wiki` | — |
| `POST /api/campaigns/{campaign_id}/wiki` | — |
| `GET /api/campaigns/{campaign_id}/wiki/export` | — |
| `POST /api/campaigns/{campaign_id}/wiki/import` | — |
| `PUT /api/campaigns/{campaign_id}/wiki/reorder` | — |
| `GET /api/campaigns/{campaign_id}/wiki/search` | — |
| `GET /api/campaigns/{campaign_id}/wiki/titles` | — |
| `GET /api/campaigns/{campaign_id}/wiki/{page_id}` | — |
| `PATCH /api/campaigns/{campaign_id}/wiki/{page_id}` | — |
| `DELETE /api/campaigns/{campaign_id}/wiki/{page_id}` | — |

### downloads

| Endpoint | Command |
|---|---|
| `GET /api/downloads/archive` | — |

### export

| Endpoint | Command |
|---|---|
| `GET /api/export/tags` | — |

### favorites

| Endpoint | Command |
|---|---|
| `GET /api/favorites` | — |
| `POST /api/favorites` | — |
| `DELETE /api/favorites/{item_type}/{item_id}` | — |

### library

| Endpoint | Command |
|---|---|
| `GET /api/about` | *(version check inside `login`)* |
| `POST /api/cancel-scan` | — |
| `GET /api/latest-release` | — |
| `POST /api/rescan` | — |
| `GET /api/scan-status` | — |
| `GET /api/stats` | — |

### logs

| Endpoint | Command |
|---|---|
| `GET /api/logs` | — |

### lookups

| Endpoint | Command |
|---|---|
| `GET /api/dice-materials` | — |
| `POST /api/dice-materials` | — |
| `DELETE /api/dice-materials/{material_id}` | — |
| `GET /api/genres` | — |
| `POST /api/genres` | — |
| `DELETE /api/genres/{genre_id}` | — |
| `GET /api/licenses` | — |
| `POST /api/licenses` | — |
| `DELETE /api/licenses/{license_id}` | — |
| `GET /api/parent-systems` | — |
| `POST /api/parent-systems` | — |
| `DELETE /api/parent-systems/{parent_id}` | — |
| `GET /api/system-families` | — |
| `POST /api/system-families` | — |
| `DELETE /api/system-families/{family_id}` | — |

### maintenance

| Endpoint | Command |
|---|---|
| `GET /api/health` | — |
| `POST /api/maintenance/cleanup-missing` | — |

### maps

| Endpoint | Command |
|---|---|
| `GET /api/map-folders` | — |
| `PATCH /api/map-folders` | — |
| `GET /api/maps` | — |
| `GET /api/maps/{map_id}` | — |
| `PATCH /api/maps/{map_id}` | — |
| `GET /api/maps/{map_id}/file` | — |
| `GET /api/maps/{map_id}/page/{page_num}` | — |
| `GET /api/maps/{map_id}/thumbnail` | — |

### saved-filters

| Endpoint | Command |
|---|---|
| `GET /api/saved-filters` | — |
| `POST /api/saved-filters` | — |
| `PATCH /api/saved-filters/{filter_id}` | — |
| `DELETE /api/saved-filters/{filter_id}` | — |

### search

| Endpoint | Command |
|---|---|
| `GET /api/search` | — |

### settings

| Endpoint | Command |
|---|---|
| `GET /api/settings` | — |
| `PATCH /api/settings` | — |
| `DELETE /api/settings/api-key` | — |
| `POST /api/settings/api-key/generate` | — |
| `GET /api/settings/ui` | — |

### systems

| Endpoint | Command |
|---|---|
| `GET /api/systems` | `systems list` |
| `GET /api/systems/{system_id}` | `systems get` |
| `PATCH /api/systems/{system_id}` | — |
| `GET /api/systems/{system_id}/book-folders` | — |
| `PATCH /api/systems/{system_id}/book-folders` | — |

### tags

| Endpoint | Command |
|---|---|
| `GET /api/tags` | — |
| `POST /api/tags` | — |
| `PATCH /api/tags/{internal}` | — |
| `DELETE /api/tags/{internal}` | — |
| `GET /api/tags/{internal}/items` | — |
| `POST /api/tags/{internal}/merge` | — |

### tokens

| Endpoint | Command |
|---|---|
| `GET /api/token-folders` | — |
| `PATCH /api/token-folders` | — |
| `GET /api/tokens` | — |
| `GET /api/tokens/{token_id}` | — |
| `PATCH /api/tokens/{token_id}` | — |
| `GET /api/tokens/{token_id}/file` | — |
| `GET /api/tokens/{token_id}/thumbnail` | — |

### users

| Endpoint | Command |
|---|---|
| `GET /api/users` | — |
| `POST /api/users` | — |
| `GET /api/users/guests` | — |
| `DELETE /api/users/me` | — |
| `GET /api/users/me/opds` | — |
| `DELETE /api/users/me/opds` | — |
| `POST /api/users/me/opds/generate` | — |
| `PATCH /api/users/me/password` | — |
| `PATCH /api/users/me/preferences` | — |
| `PATCH /api/users/{user_id}` | — |
| `DELETE /api/users/{user_id}` | — |
| `POST /api/users/{user_id}/convert` | — |

## How this was generated

```bash
python3 -c "
import json
s = json.load(open('temp/grimoire-openapi.json'))
for p, ops in sorted(s['paths'].items()):
    for m in ops:
        if m in ('get', 'post', 'patch', 'put', 'delete'):
            print(f'| \`{m.upper()} {p}\` | — |')
"
```

Rows were then grouped by the spec's `tags` field and the four implemented
rows hand-marked. `temp/grimoire-openapi.json` is gitignored and never
committed — pull a fresh copy from a running instance before regenerating
this table.
