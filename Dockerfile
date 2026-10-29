FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/ChessSlackBetter.Bot -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Skia needs fontconfig and a font for the board coordinates.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ChessBot__DatabasePath=/data/chessbot.db
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "ChessSlackBetter.Bot.dll"]
