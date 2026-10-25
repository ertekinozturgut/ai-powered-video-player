# VideoAtlas Analysis API v1

The external API lets a third-party system submit video or audio files to the same analysis queue used by the web application. Every credential belongs to one Identity user. API-created analyses therefore appear in that user's library and under **My account > API-created analyses**.

## Credentials and security

Create and revoke credentials in **My account > API credentials**. Administrators can also create, map, and revoke credentials for any registered user at `/admin/api`. The secret is shown once and is stored only as a SHA-256 digest. Send it only in the header below; never put it in a URL.

```http
Authorization: Bearer vat_<prefix>.<secret>
```

Scopes are `analysis:read`, `analysis:write`, and `analysis:delete`. A credential can access only API-created analyses owned by its mapped user. Browser cookies cannot satisfy API authorization, and API-key requests do not use CSRF tokens. Credentials can expire or be revoked immediately. The API is rate-limited to 60 requests per minute per credential.

Request auditing stores timestamp, credential prefix, mapped user, method, path, HTTP status, duration, byte counts, hashed remote address, trace ID, and related video ID for 90 days. It never stores the API secret, Authorization header, uploaded file, transcript, or request body. Users see their own latest records in their profile; administrators see the system view at `/admin/api`.

## Create an analysis

`POST /api/v1/analyses` uses `multipart/form-data` and returns `202 Accepted`.

```bash
curl -X POST https://videoatlas.example/api/v1/analyses \
  -H "Authorization: Bearer $VIDEOATLAS_API_KEY" \
  -F "file=@meeting.mp4" \
  -F "title=Weekly product meeting" \
  -F "consent=true" \
  -F "language=en" \
  -F "fullReview=true" \
  -F "speakerDiarization=true" \
  -F "speakerCount=3" \
  -F $'terms=VideoAtlas|Product name\nACME|Customer name' \
  -F "instructions=Preserve product and customer names"
```

`consent=true` is mandatory. `speakerCount` is optional (2-32). Omit it to let a compatible STT provider estimate the count. `terms` uses one `term|description` entry per line. Video and audio size/quota rules are identical to web uploads.

The response contains `id`, `status`, `stage`, `progress`, and absolute status/result/subtitle URLs. Poll the status URL with exponential backoff; terminal states are `Ready`, `AwaitingTranscript`, `Failed`, and `Canceled`.

## Endpoints

| Method | Endpoint | Scope | Purpose |
|---|---|---|---|
| `GET` | `/api/v1` | public | Version and endpoint discovery |
| `GET` | `/api/v1/openapi.json` | public | OpenAPI 3.0 discovery document |
| `POST` | `/api/v1/analyses` | write | Upload and queue video/audio |
| `GET` | `/api/v1/analyses?page=1&pageSize=25` | read | Paginated owner history |
| `GET` | `/api/v1/analyses/{id}` | read | Status and progress |
| `GET` | `/api/v1/analyses/{id}/result` | read | Transcript, topics, speakers |
| `GET` | `/api/v1/analyses/{id}/transcript` | read | Structured timed transcript |
| `GET` | `/api/v1/analyses/{id}/subtitles.srt` | read | Download SRT |
| `POST` | `/api/v1/analyses/{id}/cancel` | write | Request cancellation |
| `POST` | `/api/v1/analyses/{id}/retry` | write | Retry failed/idle work |
| `POST` | `/api/v1/analyses/{id}/reanalyze` | write | Full reanalysis; JSON `speakerDiarization`, `speakerCount` |
| `PUT` | `/api/v1/analyses/{id}/speakers` | write | Rename speaker labels; JSON `names` map |
| `POST` | `/api/v1/analyses/{id}/subtitles` | write | Import SRT when automatic STT is unavailable; JSON `content` |
| `DELETE` | `/api/v1/analyses/{id}` | delete | Delete an idle API analysis and media |

Errors use RFC 7807 problem details with stable `code` and `traceId` extensions. `401` means the credential is missing, invalid, expired, or revoked; `403` means the scope is missing; `404` hides foreign or non-API analyses; `409` represents an invalid state transition; `413` is an upload limit; and `429` is rate limiting.
