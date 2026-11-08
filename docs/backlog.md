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

3. **Sequências e marcas** — FEITO em 08/11/2026 (ferramenta `sequencias` do chat, ObterSequenciasHandler) (gancho de título: "o Galo não perde há 7 jogos")
   - Ferramenta do chat: invencibilidade, jogos sem sofrer gol, sequência de vitórias, maior goleada da temporada, "desde X".
   - A base já tem os placares; é conta em cima de procurar_partidas.

4. **Palpite da IA antes do jogo, e "acertou ou errou?" depois** — FEITO em 08/11/2026 (PalpitesHandler: chances exatas pela força
   Elo + Poisson, guardado em palpites e congelado no apito; ferramentas `palpite` e `historico_palpites` no chat; bloco e cartão
   "Palpite para vídeo" no painel da partida; o AcompanhamentoPosJogo faz o palpite dos jogos do Galo nas 48 h antes)
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

9. **Sair do Pro da API-Football (US$ 19/mês)** — revisto em 08/11/2026.
   - **Descoberta:** o plano grátis só libera as temporadas 2022 a 2024 (temporada atual devolve "Free plans do not have access
     to this season"). Cortar para 100 requisições/dia NÃO resolve: no grátis o Scout não recebe nada de 2026.
   - **Decisão do Leandro (08/11):** renovar o Pro por mais 1 mês para fechar a temporada 2026 com o Scout completo e, nesse mês,
     construir a coleta pela PitchAPI (grátis) para 2027 não depender do Pro.
   - Plano da migração:
     1. Tabela nova de estatística por partida e atleta, vinda da PitchAPI (/v1/matches/{id}/players e /advanced/players),
        ligando o atleta da PitchAPI ao nosso por nome + time (como ligarJogador no ParaVideo.ts). Serve também ao item 10 v2.
        **FEITO em 08/11/2026:** tabela desempenhos_avancados (ColetaPitch + LigarNomes), roda na AtualizacaoDiaria depois da
        API-Football (até 150 jogos/dia) e pelo comando `pitch <liga> <temporada> [max]` do Scout.Coleta. Teste local: 10 jogos,
        250 de 254 atletas ligados ao cadastro (os 4 restantes ficam sem ligação de propósito, na dúvida não liga).
     2. Placar, escalação, lances e estatísticas do time por jogo pela PitchAPI, no lugar das 3-4 consultas por jogo.
     3. Somar a temporada a partir dos jogos (hoje vem pronta do /players da API-Football).
     4. O que a PitchAPI não tem (conferido em 08/11): copas (Copa do Brasil, Libertadores, Sul-Americana; no Brasil só tem
        Série A, Série B e Paulistão), data de nascimento (regra dos 18) e elenco do time (/teams/{id}/squad dá 404).
        Testar a Bzzoiro (item 11) e o ESPN para isso.
   - Antes de vencer o mês renovado: decidir se 2027 começa só com a PitchAPI ou se renova de novo.

## Cartões de atleta

10. **Cartão do atleta para vídeo (por 90 min, percentil e ranking)** — ideia de 06/11/2026, a partir de um "playercard" de outro perfil.
    - **v1 feita em 08/11/2026** (só API-Football): GET /api/atletas/{id}/cartao e botão "Cartão para vídeo" no painel do
      atleta; ranking contra o mesmo setor na liga. Falta: comparação com todos os de linha e as métricas da PitchAPI.
    - No "Para vídeo" do painel do atleta, no nosso visual (verde, Futebol em Números), 1080x1920.
    - Blocos com o que a API-Football tem: Passe (passes certos, precisão, passes decisivos, assistências), Defesa (desarmes,
      interceptações, bloqueios), Duelos (ganhos e %, faltas sofridas), Ataque (finalizações, no alvo, gols, dribles certos e %),
      Disciplina (faltas, cartões). Cada número por 90 min, com percentil (cor) e "3º de 413" contra atletas com 450+ min
      (MotorDeAnalise.MinutosMinimos), comparando com o mesmo setor e com todos os de linha.
    - PitchAPI tem quase tudo do cartão, POR JOGO (conferido em 06/11 com Botafogo x Galo, m_2KAgmB):
      /v1/matches/{id}/players (passes certos, passes_into_final_third, long_balls_accurate, touches, recoveries, duelos,
      xG, xA) e /v1/matches/{id}/advanced/players (progressive_passes, passes_into_box, switches, through_balls, crosses,
      progressive_carries, carries_into_final_third, take_ons, sca/gca, xag, xg_chain, xt, vaep, tackles, interceptions).
      Não há total da temporada: somar jogo a jogo numa tabela nova (estatística avançada por partida e atleta), ligando o
      atleta da PitchAPI ao nosso pelo nome + time (como ligarJogador no ParaVideo.ts).
    - Carga inicial ~700 consultas (34 rodadas x 10 jogos x 2), depois ~20 por rodada; sem cota publicada, só freio de
      excesso (429 com Retry-After): ir devagar.
    - Primeira versão pode sair só com a API-Football (o que já está no banco); a avançada entra depois.

## Fontes de dados para testar

11. **Bzzoiro Sports Data (sports.bzzoiro.com)** — TESTADA em 08/11/2026 com a chave (BZZOIRO_KEY no .env local; header
    `Authorization: Token <chave>`). Cobre o que falta na PitchAPI:
    - Ligas e copas de 2026: Série A (9), Série B (34), **Copa do Brasil (35), Libertadores (32), Sul-Americana (33)**.
    - **Elenco por time** (/api/players/?team=155 = Galo, 46 atletas) com **data de nascimento** (Gutte 2008-04-05, igual à
      ficha do Sub-17), posição, altura, peso, pé, contrato, valor de mercado e transferências.
    - Jogo (/api/events/{id}/): placar, xG real, treinadores, escalações com nota, desfalques, árbitro, público, confronto.
      Galo x Grêmio da Copa do Brasil (602319) veio completo.
    - **Estatística por atleta e jogo** (/api/player-stats/?event={id}): minutos, nota, gols, xA, finalizações, passes, passes
      decisivos, cruzamentos, bolas longas, toques, dribles, duelos, aéreos, desarmes, cortes, interceptações, recuperações,
      faltas, cartões, defesas, pênaltis.
    - Cuidados: há duplicata no cadastro (Gutte aparece duas vezes, com 04 e 05/04/2008); ids próprios (ligar por nome + time,
      como na PitchAPI); serviço novo e sem garantia, igual à PitchAPI.
    - **Conclusão:** PitchAPI (ligas: mapas e métricas avançadas) + Bzzoiro (copas, elencos, nascimento, estatística por jogo)
      cobrem o Scout sem o Pro da API-Football. Próximo passo: ColetaBzzoiro (elencos e nascimento primeiro, depois os jogos de
      copa) e, em dezembro, um teste de uma semana com o Scout lendo só das duas antes de decidir não renovar.
    - **Nascimentos FEITO em 08/11/2026** (ColetaBzzoiro.PreencherNascimentos; roda na AtualizacaoDiaria e pelo comando
      `nascimentos-bzzoiro <temporada>`): só preenche quem está sem data, ignora 1º de janeiro (data de quem só sabe o ano) e
      duplicatas com datas diferentes. Teste local: 141 de 516 atletas sem data (41 de 110 clubes ligados; os demais são de
      competições que a Bzzoiro não tem).
    - **Jogos de copa FEITO em 08/11/2026** (ColetaBzzoiro.ColetarCopas; até 40 jogos/dia e comando `copas-bzzoiro <temporada>
      [max]`): desempenho por atleta nos jogos de Copa do Brasil, Libertadores e Sul-Americana com time da Série A, na mesma
      tabela da PitchAPI (desempenhos_avancados, coluna fonte). Teste local: 15 jogos, 390 de 398 atletas ligados.
    - Falta para 2027 sem o Pro: criar a partida a partir da Bzzoiro/PitchAPI quando a API-Football não tiver o jogo (hoje a
      coleta só complementa jogos que já estão na base) e montar escalação, placar e estatística do time a partir delas.

## Imagem e direitos (escudos, fotos, cores)

12. **Modo gravação com escudos e cores dos clubes, sem fotos** — conversa de 06/11/2026.
    - **Feito em 08/11/2026:** escudos aparecem no modo gravação (fotos seguem escondidas); cores por clube em web/src/Cores.ts
      nos cartões do jogo (placar, barras, destaques, anel das notas, mapas) e nas bolinhas da prancheta. Times de cores
      parecidas: o segundo fica cinza.
    - Hoje o "Modo gravação" esconde escudos e fotos. Mudar para: escudos aparecem (pequenos, em placar e tabela: uso
      informativo, sem parecer oficial), fotos de atletas continuam escondidas.
    - Cartões do "Para vídeo" e da prancheta com as cores de cada clube (ex.: Grêmio azul e preto) no lugar do verde único.
      Cor sozinha não é protegida.
    - Fotos nos vídeos: só de divulgação do clube para a imprensa, sempre com crédito ("Foto: Atlético/divulgação"), ou com
      licença livre (Wikimedia Commons). Evitar foto de agência (Getty, AGIF, Reuters): é de onde vêm as denúncias de direito
      autoral (3 derrubam o canal). Menor de 18: só foto de divulgação, nunca de rede social pessoal.
    - Nunca imagem de transmissão (direito de arena): o maior risco de bloqueio.
    - Quando o canal monetizar ou o Scout for vendido a clubes: consultar advogado de direito desportivo.
