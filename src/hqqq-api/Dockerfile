# Stage 1: Base runtime image (used as the final runtime layer)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Stage 2: Build with full SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["hqqq-api.csproj", "./"]
RUN dotnet restore "hqqq-api.csproj"
COPY . .
RUN dotnet build "hqqq-api.csproj" -c Release -o /app/build

# Stage 3: Publish optimized output
FROM build AS publish
RUN dotnet publish "hqqq-api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 4: Final production image (lean, no SDK)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "hqqq-api.dll"]
