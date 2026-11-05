FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY src/Lab6.Crypto/Lab6.Crypto.csproj src/Lab6.Crypto/
COPY src/Lab6.Server/Lab6.Server.csproj src/Lab6.Server/
RUN dotnet restore src/Lab6.Server/Lab6.Server.csproj
COPY src/Lab6.Crypto/ src/Lab6.Crypto/
COPY src/Lab6.Server/ src/Lab6.Server/
RUN dotnet publish src/Lab6.Server/Lab6.Server.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lab6.Server.dll"]
