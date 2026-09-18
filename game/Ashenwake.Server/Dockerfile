FROM mcr.microsoft.com/dotnet/sdk:8.0.425 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY game/Ashenwake.Core/ game/Ashenwake.Core/
COPY game/Ashenwake.Server/ game/Ashenwake.Server/
RUN dotnet restore game/Ashenwake.Server --locked-mode -p:NuGetAudit=false && dotnet publish game/Ashenwake.Server --no-restore -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0.31
WORKDIR /app
COPY --from=build /app/ ./
COPY content/combat.json /app/content/combat.json
RUN mkdir -p /app/artifacts/online-server && chown -R 1654:1654 /app/artifacts
USER 1654:1654
ENV ASHENWAKE_LISTEN=http://0.0.0.0:5180 ASHENWAKE_COMBAT_CONTENT=/app/content/combat.json
EXPOSE 5180
ENTRYPOINT ["dotnet", "Ashenwake.Server.dll"]
