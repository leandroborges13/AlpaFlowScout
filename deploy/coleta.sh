#!/bin/sh
# Atalho para os comandos da Coleta dentro do container (rode em /opt/alpaflowscout).
#   ./coleta.sh importar 72 2026
#   ./coleta.sh criar-usuario voce@email.com "Seu Nome"
#   ./coleta.sh cota
exec docker compose run --rm -it --entrypoint dotnet alpaflowscout /app/coleta/Scout.Coleta.dll "$@"
