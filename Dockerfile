FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props ./
COPY src/Application/Application.csproj src/Application/
RUN dotnet restore src/Application/Application.csproj

COPY src/Application/ src/Application/

# Wolverine's Production mode loads handlers/endpoints from pre-generated code
# (TypeLoadMode.Static, AssertAllPreGeneratedTypesExist) instead of compiling them
# at startup, so that code has to be baked in at build time.
RUN dotnet run --project src/Application/Application.csproj \
    --configuration Release \
    --no-launch-profile \
    -- codegen write

RUN dotnet publish src/Application/Application.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql links against libgssapi_krb5 for GSSAPI/Kerberos negotiation even when
# it isn't used to authenticate; the slim runtime image doesn't ship it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Set by the CI build (git release tag, or dev-<short-sha> for non-release builds) so
# GET /api/v1/version can report which build is actually running.
ARG APP_VERSION=dev

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    APP_VERSION=$APP_VERSION

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Application.dll"]
