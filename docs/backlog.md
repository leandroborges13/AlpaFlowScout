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
