# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy solution and project files first for caching
COPY RecruitAI.sln ./
COPY src/RecruitAI.API/RecruitAI.API.csproj src/RecruitAI.API/
COPY src/RecruitAI.Application/RecruitAI.Application.csproj src/RecruitAI.Application/
COPY src/RecruitAI.Domain/RecruitAI.Domain.csproj src/RecruitAI.Domain/
COPY src/RecruitAI.Infrastructure/RecruitAI.Infrastructure.csproj src/RecruitAI.Infrastructure/
COPY src/RecruitAI.Shared/RecruitAI.Shared.csproj src/RecruitAI.Shared/
COPY tests/RecruitAI.IntegrationTests/RecruitAI.IntegrationTests.csproj tests/RecruitAI.IntegrationTests/

# Restore packages
RUN dotnet restore RecruitAI.sln

# Copy the rest of the source files
COPY src/ src/
COPY tests/ tests/

# Publish the API project
RUN dotnet publish src/RecruitAI.API/RecruitAI.API.csproj -c Release -o /publish

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /publish .

# Expose port (Render automatically maps internal port to public URL)
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Set entry point
ENTRYPOINT ["dotnet", "RecruitAI.API.dll"]
