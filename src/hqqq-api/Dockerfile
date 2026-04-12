# Stage 1: Base runtime image (used as the final runtime layer)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Stage 2: Build with full SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["hqqq-api.csproj", "./"]
RUN dotnet restore "hqqq-api.csproj"
COPY . .

# MSBuild Version = semver base (e.g. 1.0.3); InformationalVersion = full label (e.g. 1.0.3 or 0.0.0+abc1234)
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0-local

RUN dotnet build "hqqq-api.csproj" -c Release -o /app/build \
    /p:Version="${VERSION}" \
    /p:InformationalVersion="${INFORMATIONAL_VERSION}"

# Stage 3: Publish optimized output
FROM build AS publish
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0-local
RUN dotnet publish "hqqq-api.csproj" -c Release -o /app/publish /p:UseAppHost=false \
    /p:Version="${VERSION}" \
    /p:InformationalVersion="${INFORMATIONAL_VERSION}"

# Stage 4: Final production image (lean, no SDK)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "hqqq-api.dll"]
