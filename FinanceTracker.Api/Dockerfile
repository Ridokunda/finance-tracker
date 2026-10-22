FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY FinanceTracker.Api.csproj ./
RUN dotnet restore FinanceTracker.Api.csproj

COPY . ./
RUN dotnet publish FinanceTracker.Api.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app/publish ./

USER app

EXPOSE 8080

ENTRYPOINT ["dotnet", "FinanceTracker.Api.dll"]