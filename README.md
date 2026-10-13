# AlpaFlow Scout

Análise de atletas do futebol brasileiro com IA: o usuário descreve o perfil que procura
("zagueiro pela esquerda para o 3-4-3, bom no jogo aéreo e na saída de bola, até 25 anos")
e um agente consulta a base, ordena os atletas por encaixe e explica cada escolha.

> **Uso interno.** Os dados vêm da API-Football, que não licencia publicação. A plataforma mostra
> as nossas métricas (notas e percentis) e fatos públicos (jogos, minutos, gols), nunca as
> estatísticas brutas do provedor.

## Arquitetura

.NET 10 com Clean Architecture e CQRS (handlers próprios, sem biblioteca de mediador) + React.

| Projeto | O que faz |
|---|---|
| `Scout.Domain` | Atleta, clube, competição, estatística, partida, escalação; regra dos 18 anos; função tática pela grade do esquema |
| `Scout.Application` | Motor de análise (indicadores por 90 min, percentis por setor, notas), consultas (busca por perfil, detalhe, parecidos, oportunidades) e o agente de chat |
| `Scout.Infrastructure` | EF Core + PostgreSQL, cliente da API-Football com cache no banco e controle de cota, importador |
| `Scout.Coleta` | Linha de comando para importar competições |
| `Scout.Api` | Minimal API + chat via `Microsoft.Extensions.AI` (OpenAI) com chamada de ferramentas |
| `web/` | React + Vite: chat e cartões de atleta |
| `prototipo-node/` | Primeiro protótipo (validação), substituído pela versão .NET |

### Regras de proteção

- Atleta com menos de 18 anos, ou sem data de nascimento, não entra em nenhuma análise individual.
- Só entra quem jogou pelo menos 450 minutos na competição.
- A idade é a do fim da temporada (ou a de hoje, se ela está em andamento).

### Atributos que a busca entende

Jogo aéreo, saída de bola, marcação, intensidade, drible, criação, finalização, disciplina, regularidade e goleiro.
Cada um é a média dos percentis de alguns indicadores. Jogo aéreo, saída de bola e intensidade são
**aproximações**: o provedor atual não tem duelo aéreo, passe progressivo nem dados físicos.

### Ajuste à posse

Desarmes, interceptações e bloqueios são ajustados à posse do time (×50 / posse do adversário):
quem joga num time que fica com a bola defende menos vezes, e sem o ajuste os defensores de times
dominantes pareceriam fracos.

## Análise de times

A partir das escalações e das estatísticas de cada jogo (`/fixtures/statistics`, uma consulta por partida):

- **Esquemas:** esquema inicial de cada jogo, em casa e fora, com o aproveitamento de pontos em cada um.
- **Estilo e pontos fortes:** posse e construção, força ofensiva, solidez defensiva e disciplina, em percentil
  entre os times da competição. As médias brutas do provedor não aparecem.
- **Elenco:** idade média e minutos para atletas até 23 anos (pesados pelos minutos), atletas usados e rodízio.
- **Carências:** em cada função do plano de jogo, a nota dos titulares comparada com a dos outros times que usam
  a mesma função. Abaixo do percentil 35 é carência.
- **Mercado:** times com carência numa função, e em que times um atleta encaixaria (titulares com nota menor que a dele).

## Como rodar

1. Copie `.env.example` para `.env` e preencha `APIFOOTBALL_KEY`, `SCOUT_DB` e `OPENAI_API_KEY`.
2. Importe as competições (o banco é criado na primeira vez):

```bash
dotnet run --project src/Scout.Coleta -- importar 72 2026
dotnet run --project src/Scout.Coleta -- importar 75 2026
```

Ligas: 71 Série A · 72 Série B · 75 Série C · 76 Série D. `--sem-escalacoes` pula as escalações
(mais rápido, mas sem a função tática). `cota` mostra quantas consultas foram usadas hoje.

3. Suba a API e o front:

```bash
dotnet run --project src/Scout.Api
```
```bash
cd web && npm install && npm run dev
```

Abra http://localhost:5173.

```bash
dotnet test
```

4. Crie o seu login (a senha é pedida no terminal):

```bash
dotnet run --project src/Scout.Coleta -- criar-usuario voce@email.com "Seu Nome"
```

## Publicação e app no celular

O site é um app instalável (PWA): no celular, "Instalar app" no Chrome ou "Adicionar à Tela de Início" no Safari.
Todo o acesso exige login. O passo a passo do servidor está em [docs/DEPLOY.md](docs/DEPLOY.md).
