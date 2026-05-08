FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY BlindSpots.slnx .
COPY BlindSpots/*.csproj ./BlindSpots/

RUN dotnet restore

COPY . .

WORKDIR /source/BlindSpots
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BlindSpots.dll"]