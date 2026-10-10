# VideoAtlas local audio MCP server

`VideoAtlas.Mcp` is a cross-platform stdio MCP server for processing local audio and video without sending the recording to an external provider. It supports Windows and Linux with .NET 10, FFmpeg, Python, and local Whisper.

## Tools

| Tool | Purpose |
| --- | --- |
| `check_local_audio_dependencies` | Reports FFmpeg, Whisper, Python, and allowed-root readiness. |
| `analyze_audio` | Runs the complete pipeline and creates `analysis.json`, `transcript.json`, `transcript.srt`, and `topics.json`. |
| `transcribe_audio` | Runs Whisper without speaker separation. It still creates the standard output set. |
| `diarize_transcript` | Adds `speaker_0`, `speaker_1`, … labels to an existing transcript using local voice and acoustic features. |
| `split_transcript_topics` | Creates chronological topic groups from an existing transcript using local contextual similarity. |
| `export_srt` | Creates UTF-8 SRT and includes speaker labels when available. |

The topic splitter is deterministic and fully local. It detects contextual vocabulary changes and enforces sensible minimum/maximum chapter lengths. It does not claim the deeper semantic quality of a configured LLM.

## Requirements

- .NET 10 SDK/runtime
- FFmpeg and FFprobe on `PATH`
- Python 3.11 or newer
- Python packages from `requirements-local-whisper.txt`
- Enough RAM/disk for the selected Whisper model; GPU is optional

### Windows

```powershell
.\scripts\setup-mcp.ps1
.\scripts\run-mcp.ps1 -AllowedRoots "D:\Media;D:\VideoAtlasOutput"
```

### Linux

```bash
chmod +x scripts/setup-mcp.sh scripts/run-mcp.sh
./scripts/setup-mcp.sh
VIDEOATLAS_MCP_ALLOWED_ROOTS="/srv/media:/srv/videoatlas-output" ./scripts/run-mcp.sh
```

The separator in `VIDEOATLAS_MCP_ALLOWED_ROOTS` is `;` on Windows and `:` on Linux. If the variable is omitted, only the MCP process working directory is accessible.

## MCP client configuration

Publish once with the setup script, then configure the MCP client to launch the server over stdio. Use absolute paths.

Windows example:

```json
{
  "mcpServers": {
    "videoatlas-audio": {
      "command": "dotnet",
      "args": ["C:\\VideoAtlas\\artifacts\\mcp\\VideoAtlas.Mcp.dll"],
      "env": {
        "VIDEOATLAS_MCP_ALLOWED_ROOTS": "D:\\Media;D:\\VideoAtlasOutput",
        "PYTHON_EXECUTABLE": "C:\\VideoAtlas\\.venv-mcp\\Scripts\\python.exe"
      }
    }
  }
}
```

Linux example:

```json
{
  "mcpServers": {
    "videoatlas-audio": {
      "command": "dotnet",
      "args": ["/opt/videoatlas/artifacts/mcp/VideoAtlas.Mcp.dll"],
      "env": {
        "VIDEOATLAS_MCP_ALLOWED_ROOTS": "/srv/media:/srv/videoatlas-output",
        "PYTHON_EXECUTABLE": "/opt/videoatlas/.venv-mcp/bin/python"
      }
    }
  }
}
```

## Complete analysis call

Call `analyze_audio` with an absolute file path, optional language, Whisper model, and optional expected speaker count:

```json
{
  "inputPath": "/srv/media/interview.mp3",
  "outputDirectory": "/srv/videoatlas-output/interview-001",
  "model": "large-v3-turbo",
  "language": "tr",
  "speakerCount": 2
}
```

The original recording is read-only. A requested output directory must be new or empty, so an MCP call cannot silently replace an earlier analysis.

## Configuration and safety

| Variable | Default | Meaning |
| --- | --- | --- |
| `VIDEOATLAS_MCP_ALLOWED_ROOTS` | Process working directory | File-system allowlist. All input and output paths must stay inside it. |
| `VIDEOATLAS_MCP_MAX_INPUT_BYTES` | 2 GiB | Maximum source size. |
| `WHISPER_EXECUTABLE` | `whisper` | Optional absolute executable path. |
| `PYTHON_EXECUTABLE` | `python` on Windows, `python3` on Linux | Optional absolute executable path. |
| `FFMPEG_EXECUTABLE` | `ffmpeg` | Optional absolute executable path. |
| `VIDEOATLAS_MCP_WHISPER_TIMEOUT_MINUTES` | 180 | Whisper process timeout. |
| `VIDEOATLAS_MCP_DIARIZATION_TIMEOUT_MINUTES` | 120 | Speaker-analysis timeout. |
| `VIDEOATLAS_MCP_FFMPEG_TIMEOUT_MINUTES` | 30 | Media-normalization timeout. |

Cancellation or timeout kills the complete spawned process tree. Tool results never include raw audio, and server logs are written to stderr so they do not corrupt the stdio MCP channel.
