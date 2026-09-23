FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/CvPlatform.Domain/CvPlatform.Domain.csproj src/CvPlatform.Domain/
COPY src/CvPlatform.Web/CvPlatform.Web.csproj src/CvPlatform.Web/
RUN dotnet restore src/CvPlatform.Web/CvPlatform.Web.csproj
COPY src/ src/
RUN dotnet publish src/CvPlatform.Web/CvPlatform.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CvPlatform.Web.dll"]
