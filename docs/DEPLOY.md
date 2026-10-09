# Deploy do AlpaFlow Scout (servidor Hetzner)

A cada push na `main`, o GitHub Actions roda os testes e publica a imagem
`ghcr.io/leandroborges13/alpaflowscout:latest` (o site React vai dentro da API).

## Primeira vez

1. **DNS (Cloudflare):** registro `A` `scout` → IP do servidor, com proxy ligado (como os outros subdomínios).

2. **Arquivos no servidor:**

```bash
mkdir -p /opt/alpaflowscout && cd /opt/alpaflowscout
curl -fsSLO https://raw.githubusercontent.com/leandroborges13/AlpaFlowScout/main/deploy/docker-compose.yml
curl -fsSLO https://raw.githubusercontent.com/leandroborges13/AlpaFlowScout/main/deploy/coleta.sh
curl -fsSL -o .env https://raw.githubusercontent.com/leandroborges13/AlpaFlowScout/main/deploy/.env.exemplo
chmod +x coleta.sh
nano .env
```

No `.env`, preencha `DB_PASSWORD` (crie uma senha forte nova), `APIFOOTBALL_KEY` e `OPENAI_API_KEY`.

3. **Subir:**

```bash
docker compose pull && docker compose up -d
```

Se o `pull` pedir login, rode `docker login ghcr.io` com o seu usuário do GitHub e um token com `read:packages`.

4. **Importar os dados** (cerca de 1.300 consultas da API-Football):

```bash
./coleta.sh importar 71 2026
./coleta.sh importar 72 2026
./coleta.sh importar 75 2025
```

5. **Criar o seu usuário** (a senha é pedida no terminal, mínimo de 10 caracteres):

```bash
./coleta.sh criar-usuario voce@email.com "Seu Nome"
```

6. **Nginx Proxy Manager** (`ssh -L 8181:localhost:81` e abra http://localhost:8181):
   *Proxy Hosts → Add*: domínio `scout.alpaflow.tec.br`, esquema `http`, host `alpaflowscout`, porta `8080`.
   Na aba SSL: certificado Let's Encrypt, **Force SSL** ligado.

7. **Instalar no celular:** abra https://scout.alpaflow.tec.br e entre.
   - Android (Chrome): menu ⋮ → **Instalar app**.
   - iPhone (Safari): botão compartilhar → **Adicionar à Tela de Início**.

## Atualizações

- **Código novo:** depois do push e do build no GitHub (~3 min): `docker compose pull && docker compose up -d`.
- **Dados:** a própria API importa as rodadas novas todo dia às 6h (`SCOUT_ATUALIZAR` no `.env`).
- **Usuários:** `./coleta.sh criar-usuario ...` (também troca a senha de quem já existe) e `./coleta.sh remover-usuario email`.
