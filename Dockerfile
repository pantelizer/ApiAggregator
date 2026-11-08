# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, in its own layer, so it is cached until the project file changes.
COPY ApiAggregator/ApiAggregator.csproj ApiAggregator/
RUN dotnet restore ApiAggregator/ApiAggregator.csproj

COPY ApiAggregator/ ApiAggregator/
RUN dotnet publish ApiAggregator/ApiAggregator.csproj -c Release -o /app/publish --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# The aspnet image listens on 8080 by default and runs as the non-root "app" user.
EXPOSE 8080
ENTRYPOINT ["dotnet", "ApiAggregator.dll"]
