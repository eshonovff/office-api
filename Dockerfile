# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Faqat Office.Api-ро restore мекунем — Office.Api.Tests лозим нест дар image-и прод.
COPY Office.Api/Office.Api.csproj Office.Api/
RUN dotnet restore Office.Api/Office.Api.csproj

COPY Office.Api/ Office.Api/
RUN dotnet publish Office.Api/Office.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# curl барои HEALTHCHECK лозим аст — image-и пойгоҳӣ онро надорад.
RUN apk add --no-cache curl krb5-libs \
    && addgroup -S -g 1000 officeapi \
    && adduser -S -u 1000 -G officeapi officeapi \
    && mkdir -p /var/office/uploads /var/office/keys \
    && chown -R officeapi:officeapi /var/office /app

COPY --from=build --chown=officeapi:officeapi /app/publish .

USER officeapi
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
    CMD ["curl", "-f", "http://localhost:8080/health"]

ENTRYPOINT ["dotnet", "Office.Api.dll"]
