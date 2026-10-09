# 1) Site (React)
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2) API e Coleta (.NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Scout.Api -c Release -o /out/api \
 && dotnet publish src/Scout.Coleta -c Release -o /out/coleta

# 3) Imagem final: a API serve o site; a Coleta fica junto para importar e criar usuários
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/api ./api
COPY --from=build /out/coleta ./coleta
COPY --from=web /web/dist ./api/wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    TZ=America/Sao_Paulo
EXPOSE 8080
RUN mkdir -p /app/chaves && chown app /app/chaves
USER app
WORKDIR /app/api
ENTRYPOINT ["dotnet", "Scout.Api.dll"]
