FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/CvPlatform.Domain/CvPlatform.Domain.csproj src/CvPlatform.Domain/
COPY src/CvPlatform.Web/CvPlatform.Web.csproj src/CvPlatform.Web/
RUN dotnet restore src/CvPlatform.Web/CvPlatform.Web.csproj
COPY src/ src/
# Restore again with the full source: .NET 10 only adds the package that ships blazor.web.js
# once it sees the Razor components, which the csproj-only restore above can't.
RUN dotnet publish src/CvPlatform.Web/CvPlatform.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CvPlatform.Web.dll"]
