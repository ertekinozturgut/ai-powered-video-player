FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
WORKDIR /src
COPY . .
RUN --mount=type=secret,id=proxy_ca,target=/usr/local/share/ca-certificates/environment-proxy-ca.crt \
    if [ -s /usr/local/share/ca-certificates/environment-proxy-ca.crt ]; then update-ca-certificates; fi && \
    dotnet restore src/VideoAtlas.Web/VideoAtlas.Web.csproj --locked-mode
RUN dotnet publish src/VideoAtlas.Web/VideoAtlas.Web.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg curl ca-certificates sqlite3 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /publish .
RUN mkdir -p /data && chown -R app:app /data
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production Storage__Root=/data DOTNET_CLI_TELEMETRY_OPTOUT=1
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s CMD curl -fsS http://127.0.0.1:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "VideoAtlas.Web.dll"]
