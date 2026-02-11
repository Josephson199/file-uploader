FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props ./

COPY src/server/FileUploader.VirusScanner/FileUploader.VirusScanner.csproj ./src/server/FileUploader.VirusScanner/
COPY src/server/FileUploader.Data/FileUploader.Data.csproj ./src/server/FileUploader.Data/

RUN dotnet restore ./src/server/FileUploader.VirusScanner/FileUploader.VirusScanner.csproj

COPY src/server/FileUploader.VirusScanner ./src/server/FileUploader.VirusScanner
COPY src/server/FileUploader.Data ./src/server/FileUploader.Data

RUN dotnet publish ./src/server/FileUploader.VirusScanner/FileUploader.VirusScanner.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled AS runtime

# Must run as root because Aspire volumes are root-owned
USER root

WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "FileUploader.VirusScanner.dll"]
# f2f