# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy solution and project files first for caching
COPY recruitai-backend/RecruitAI.sln recruitai-backend/
COPY recruitai-backend/src/RecruitAI.API/RecruitAI.API.csproj recruitai-backend/src/RecruitAI.API/
COPY recruitai-backend/src/RecruitAI.Application/RecruitAI.Application.csproj recruitai-backend/src/RecruitAI.Application/
COPY recruitai-backend/src/RecruitAI.Domain/RecruitAI.Domain.csproj recruitai-backend/src/RecruitAI.Domain/
COPY recruitai-backend/src/RecruitAI.Infrastructure/RecruitAI.Infrastructure.csproj recruitai-backend/src/RecruitAI.Infrastructure/
COPY recruitai-backend/src/RecruitAI.Shared/RecruitAI.Shared.csproj recruitai-backend/src/RecruitAI.Shared/
COPY recruitai-backend/tests/RecruitAI.IntegrationTests/RecruitAI.IntegrationTests.csproj recruitai-backend/tests/RecruitAI.IntegrationTests/

# Restore packages
RUN dotnet restore recruitai-backend/RecruitAI.sln

# Copy the rest of the source files
COPY recruitai-backend/ recruitai-backend/

# Publish the API project
RUN dotnet publish recruitai-backend/src/RecruitAI.API/RecruitAI.API.csproj -c Release -o /publish

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /publish .

# Expose port (Render automatically maps internal port to public URL)
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

# Set entry point
ENTRYPOINT ["dotnet", "RecruitAI.API.dll"]
