# Backlog do AlpaFlow Scout

Ideias guardadas para depois. Desde 03/11/2026 o canal "Futebol em Números" (YouTube e TikTok) é a vitrine do Scout: entra primeiro
o que faz o vídeo sair mais rápido, mais bonito ou com um gancho melhor. Cada item sai daqui quando for feito.

## Para o canal (conteúdo mais rápido e com gancho)

1. **Pós-jogo automático** — FEITO em 03/11/2026 (RoteiristaPosJogo + AcompanhamentoPosJogo; ligar com SCOUT_POSJOGO_TIMES=1062
   e SCOUT_WHATSAPP_* no servidor). O maior ganho: quem publica primeiro depois do apito pega a busca.
   - Quando um jogo do Galo termina e as estatísticas chegam, o Scout monta sozinho "onde ganhou/perdeu", as notas e um roteiro
     curto de 40 s, e avisa no WhatsApp do Leandro. Ele só grava a fala por cima.
   - Usa o que já existe: analisar_partida, notas do jogo e percentis.

2. **Cartão vertical da resposta** — FEITO em 03/11/2026 (web/src/ParaVideo.ts: botão em cada resposta e 3 cartões no painel da partida)
   - Gera a resposta em 1080x1920, com letra grande, os números em destaque e o escudo, pronta para o Short.
   - Hoje a resposta é gravada da tela do computador e ampliada na edição: o texto fica pequeno e às vezes cortado.

3. **Sequências e marcas** (gancho de título: "o Galo não perde há 7 jogos")
   - Ferramenta do chat: invencibilidade, jogos sem sofrer gol, sequência de vitórias, maior goleada da temporada, "desde X".
   - A base já tem os placares; é conta em cima de procurar_partidas.

4. **Palpite da IA antes do jogo, e "acertou ou errou?" depois**
   - Chance de vitória, empate e derrota do próximo jogo pela força tipo Elo do modelo de chances da liga.
   - Gera dois vídeos por jogo (véspera e pós-jogo) e um motivo para voltar ao canal. Guardar o palpite para mostrar o histórico de acertos.

5. **Página pública de cada análise** (link da bio)
   - scout.alpaflow.tec.br/a/<id> com a resposta e os cartões, sem login, e um botão "pergunte você também" (cadastro).
   - Transforma quem vem do vídeo em usuário da plataforma.

## Mapa de calor e finalizações no campo (levantamento de 03/11/2026)

FEITO em 03/11/2026 com a PitchAPI (Série A 2023-2026 confirmada): 4º cartão do "Para vídeo" e contagens de finalização no roteiro.

A API-Football (a nossa) não tem coordenadas de lance. Opções vistas:
- **PitchAPI** (pitchapi.dev): grátis, chave por cadastro, só pós-jogo. Finalizações com x/y em metros, xG e xGOT, e mapa de calor
  em grade 16x12 por time e atleta. Cita a Série B do Brasil (desde 2024); a Série A não aparece nomeada (conferir em /v1/leagues).
  Sem termos de uso publicados nem SLA: serviço pequeno, pode sumir. **Primeiro teste.**
- **TheStatsAPI**: a partir de US$ 50/mês (7 dias grátis), todas as ligas no mesmo plano. Anuncia mapa de calor e shot map, mas o
  exemplo do Brasileirão vem com "xg_available": false. Só pagar depois de ver no teste grátis que a Série A tem os dois.
- **Sportmonks**: €29 a €249/mês por nº de ligas; xG da Série A do Brasil (liga 648) como add-on. Não vende mapa de calor.
- **Hudl StatsBomb**: o dado de verdade (eventos com posição e 360), preço sob consulta, para clubes (Galo e Cruzeiro são clientes).
- **SofaScore / FotMob**: têm mapa de calor, mas sem API oficial; raspar viola os termos. Fora, ainda mais num canal público.

## Para a ferramenta

6. **"Mais de dois gols" ambíguo**: quando a pergunta puder ser soma ou diferença, responder as duas leituras em uma linha
   (hoje vale a diferença, regra de 03/11).
7. **Data relativa nas respostas**: além de 12/08/2026, dizer "há 12 semanas, 21 jogos", que é o que a gente fala no vídeo.
8. **Banco local desatualizado**: script para trazer os placares mais recentes de produção (ou rodar a coleta) antes de testar.

## Custos

9. **Caber no plano grátis da API-Football (100 requisições/dia)** — FAZER na segunda, 09/11/2026, quando o Pro vencer (15h33).
   Decisão do Leandro: economizar os US$ 19/mês até o canal faturar. Hoje o Pro (7.500/dia) gasta ~450/dia.
   - De onde vem o gasto: elenco de cada time de cada competição todo dia (/players por time, 2-3 páginas; só a Copa do Brasil
     tem 90+ times), mais Série B e Série C, mais 3-4 consultas por jogo e a revisão dos 3 dias seguintes.
   - Plano:
     1. SCOUT_ATUALIZAR só com a Série A (71) e as copas: Copa do Brasil (73), Libertadores (13) e Sul-Americana (11).
        Sai a Série B (72) e a Série C (75).
     2. Copas sem baixar elenco: o /players do time da Série A já traz as estatísticas dele nas copas (hoje o Importador descarta
        tudo que não é da liga importada). Guardar as copas a partir dessa mesma resposta.
     3. Nas copas, detalhes (escalação, estatísticas, lances) só dos jogos com time da Série A; os demais ficam só com o placar.
     4. Elencos da Série A em rodízio: só os times que jogaram desde a última atualização.
     5. Prioridade quando a cota apertar: Galo e pós-jogo primeiro (AcompanhamentoPosJogo), depois o resto.
   - Estimativa: 70-90 em dia de rodada, 10-20 sem jogo. Some do Scout: Série B e C (chat e chances de acesso) e análise de jogo
     de copa entre times de fora da Série A. Ao estourar a cota, o Scout para e continua no dia seguinte (já é assim).
   - Ao vencer: trocar no servidor APIFOOTBALL_LIMITE_DIA para 95 e SCOUT_ATUALIZAR para "71:2026,73:2026,13:2026,11:2026".
   - Voltar ao Pro quando as redes faturarem.
