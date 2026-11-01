FROM node:26-bookworm-slim AS web
WORKDIR /work
COPY design ./design
COPY src/Autobahn.Admin.Web ./src/Autobahn.Admin.Web
COPY src/Autobahn.Fleet.Web ./src/Autobahn.Fleet.Web
RUN cd src/Autobahn.Admin.Web && npm ci --ignore-scripts && npm run build
RUN cd src/Autobahn.Fleet.Web && npm ci --ignore-scripts && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /work
COPY . .
COPY --from=web /work/src/Autobahn.Host/wwwroot ./src/Autobahn.Host/wwwroot
RUN dotnet publish src/Autobahn.Host -c Release -p:SkipFrontendBuild=true -o /out/host
RUN dotnet publish src/Autobahn.Agent -c Release -o /out/agent

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS gateway
WORKDIR /app
USER root
RUN mkdir -p /app/data /app/config && chown -R $APP_UID:$APP_UID /app
COPY --from=build --chown=app:app /out/host .
COPY --chown=app:app config/models.json ./config/models.json
ENV ASPNETCORE_HTTP_PORTS=8080
ENV Gateway__UpstreamSecretsPath=data/upstream-secrets.enc
ENV Gateway__Security__AuditLogPath=data/audit-log.jsonl
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Autobahn.Host.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS agent
WORKDIR /app
USER root
RUN apt-get update && apt-get install -y --no-install-recommends docker.io ca-certificates && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/agent .
ENV ASPNETCORE_URLS=http://0.0.0.0:9455
ENV Agent__ModelDirectory=/models
ENV Agent__DataDirectory=/var/lib/autobahn-agent
EXPOSE 9455
ENTRYPOINT ["dotnet", "Autobahn.Agent.dll"]

FROM gateway AS final
