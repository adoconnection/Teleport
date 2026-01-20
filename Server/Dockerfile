FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Shared/Shared.csproj Shared/
COPY Server/Server.csproj Server/
RUN dotnet restore Server/Server.csproj

COPY Shared/ Shared/
COPY Server/ Server/
RUN dotnet publish Server/Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV TELEPORT_StorePath=/data

EXPOSE 8080
VOLUME /data

ENTRYPOINT ["dotnet", "TeleportServer.dll"]
