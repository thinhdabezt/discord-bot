FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DiscordXBot.sln ./
COPY src/DiscordXBot/DiscordXBot.csproj src/DiscordXBot/
RUN dotnet restore src/DiscordXBot/DiscordXBot.csproj

COPY src/DiscordXBot/ src/DiscordXBot/
WORKDIR /src/src/DiscordXBot
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
ENTRYPOINT ["dotnet", "DiscordXBot.dll"]
