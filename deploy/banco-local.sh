#!/usr/bin/env bash
# Traz os dados de produção para o banco local, para testar com os jogos e placares de hoje (backlog item 8).
#   deploy/banco-local.sh usuario@servidor        (ou SCOUT_SERVIDOR=usuario@servidor no .env)
# - O dump sai do container alpaflowscout-db no servidor (pelo ssh) e é restaurado no banco do SCOUT_DB do .env local.
# - Não traz os usuários de produção (os logins locais continuam os mesmos) nem o cache de respostas da API-Football (grande).
# - Depois aplica as migrations do código local (que pode estar à frente de produção).
set -euo pipefail

RAIZ=$(cd "$(dirname "$0")/.." && pwd)
ENV="$RAIZ/.env"
[ -f "$ENV" ] || { echo "Não achei o .env em $RAIZ"; exit 1; }
SERVIDOR="${1:-$(grep -m1 '^SCOUT_SERVIDOR=' "$ENV" | cut -d= -f2- | tr -d '"' || true)}"
[ -n "$SERVIDOR" ] || { echo "Uso: deploy/banco-local.sh usuario@servidor (ou SCOUT_SERVIDOR no .env)"; exit 1; }

CONEXAO=$(grep -m1 '^SCOUT_DB=' "$ENV" | cut -d= -f2- | tr -d '"')
campo() { echo "$CONEXAO" | tr ';' '\n' | grep -i "^$1=" | cut -d= -f2- || true; }
HOST=$(campo Host); PORTA=$(campo Port); BANCO=$(campo Database); USUARIO=$(campo Username)
export PGPASSWORD="$(campo Password)"   # só para os comandos abaixo; nunca é exibida
case "$HOST" in localhost|127.0.0.1) ;; *) echo "SCOUT_DB não aponta para o banco local ($HOST): parei por segurança."; exit 1 ;; esac

# pg_dump/pg_restore: os do PATH ou os da instalação do PostgreSQL no Windows (a versão mais nova)
PG=""
command -v pg_restore >/dev/null 2>&1 || PG=$(ls -d "/c/Program Files/PostgreSQL/"*/bin 2>/dev/null | sort -V | tail -1)
pg() { if [ -n "$PG" ]; then "$PG/$1.exe" "${@:2}"; else "$@"; fi; }
LOCAL=(-h "$HOST" -p "${PORTA:-5432}" -U "$USUARIO" -d "$BANCO")

PASTA=$(mktemp -d)
trap 'rm -rf "$PASTA"' EXIT

echo "1/5 Gerando o dump de produção no servidor ($SERVIDOR)..."
ssh "$SERVIDOR" "docker exec alpaflowscout-db pg_dump -U alpaflowscout -Fc --exclude-table-data=usuarios --exclude-table-data=respostas_provedor alpaflow_scout" > "$PASTA/producao.dump"
echo "    $(du -h "$PASTA/producao.dump" | cut -f1) baixados"

echo "2/5 Guardando os usuários do banco local..."
pg pg_dump "${LOCAL[@]}" --data-only -t usuarios -Fc > "$PASTA/usuarios.dump"

echo "3/5 Restaurando os dados de produção no banco local ($BANCO)..."
pg pg_restore "${LOCAL[@]}" --clean --if-exists --no-owner --no-privileges "$PASTA/producao.dump"

echo "4/5 Devolvendo os usuários locais..."
pg pg_restore "${LOCAL[@]}" --data-only --no-owner --no-privileges "$PASTA/usuarios.dump"

echo "5/5 Aplicando as migrations do código local..."
(cd "$RAIZ" && dotnet run --project src/Scout.Coleta -- migrar)

echo "Pronto: o banco local está com os dados de produção de $(date +%d/%m/%Y\ %H:%M)."
