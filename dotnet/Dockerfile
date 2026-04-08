FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .
COPY --from=build /app/data ./data
EXPOSE 8090
ENV ASPNETCORE_URLS=http://0.0.0.0:8090
ENTRYPOINT ["./MultiAgentSupportAI"]
