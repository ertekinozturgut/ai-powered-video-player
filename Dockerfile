FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
WORKDIR /src
COPY . .
RUN --mount=type=secret,id=proxy_ca,target=/usr/local/share/ca-certificates/environment-proxy-ca.crt \
    apt-get update && apt-get install -y --no-install-recommends ca-certificates && \
    if [ -s /usr/local/share/ca-certificates/environment-proxy-ca.crt ]; then update-ca-certificates; fi && \
    dotnet restore src/VideoAtlas.Web/VideoAtlas.Web.csproj --locked-mode && \
    rm -rf /var/lib/apt/lists/*
RUN dotnet publish src/VideoAtlas.Web/VideoAtlas.Web.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble AS runtime-base
USER root
RUN --mount=type=secret,id=proxy_ca,target=/usr/local/share/ca-certificates/environment-proxy-ca.crt \
    apt-get update && apt-get install -y --no-install-recommends ffmpeg curl ca-certificates sqlite3 && \
    if [ -s /usr/local/share/ca-certificates/environment-proxy-ca.crt ]; then update-ca-certificates; fi && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /publish .
RUN mkdir -p /data/home /data/cache && chown -R app:app /data
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__Root=/data \
    HOME=/data/home \
    XDG_CACHE_HOME=/data/cache \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s CMD curl -fsS http://127.0.0.1:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "VideoAtlas.Web.dll"]

# Small runtime for ElevenLabs/OpenAI/n8n/Azure-only deployments.
FROM runtime-base AS runtime-lite

# Build the Python environment separately so compilers are not kept in the final image.
FROM runtime-base AS local-whisper-deps
USER root
ARG TORCH_INDEX_URL=https://download.pytorch.org/whl/cpu
COPY requirements-local-whisper.txt /tmp/requirements-local-whisper.txt
RUN --mount=type=secret,id=proxy_ca,target=/usr/local/share/ca-certificates/environment-proxy-ca.crt \
    if [ -s /usr/local/share/ca-certificates/environment-proxy-ca.crt ]; then update-ca-certificates; fi && \
    apt-get update && apt-get install -y --no-install-recommends \
      build-essential python3 python3-dev python3-pip python3-venv libsndfile1-dev && \
    python3 -m venv /opt/videoatlas-whisper && \
    /opt/videoatlas-whisper/bin/pip install --no-cache-dir --upgrade pip setuptools wheel && \
    /opt/videoatlas-whisper/bin/pip install --no-cache-dir --index-url "$TORCH_INDEX_URL" "torch>=2.6" && \
    /opt/videoatlas-whisper/bin/pip install --no-cache-dir -r /tmp/requirements-local-whisper.txt && \
    /opt/videoatlas-whisper/bin/pip check && \
    rm -rf /root/.cache /var/lib/apt/lists/*

# Full runtime: local Whisper STT plus local speaker diarization.
FROM runtime-base AS runtime-local-whisper
USER root
RUN apt-get update && apt-get install -y --no-install-recommends python3 libsndfile1 libgomp1 && rm -rf /var/lib/apt/lists/*
COPY --from=local-whisper-deps /opt/videoatlas-whisper /opt/videoatlas-whisper
ENV PATH=/opt/videoatlas-whisper/bin:$PATH \
    WHISPER_EXECUTABLE=/opt/videoatlas-whisper/bin/whisper \
    PYTHON_EXECUTABLE=/opt/videoatlas-whisper/bin/python \
    TORCH_HOME=/data/cache/torch
USER app

# Docker builds the complete local-analysis image unless a compose target overrides it.
FROM runtime-local-whisper AS final
