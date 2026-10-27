<p align="center"><img src="docs/branding/banner.png" alt="LiteLauncher" width="900"></p>

# LiteLauncher

Um launcher de jogos **leve** para Windows, com visual de console (inspirado no painel do Xbox) e feito para ser usado com **controle**, teclado ou mouse. Reúne seus jogos da **Steam** e da **Epic Games** em uma única tela.

Foi pensado para ser pequeno: não substitui o shell do Windows, não instala serviço em segundo plano e lê os arquivos que a própria Steam e a Epic já guardam no disco. Os jogos são abertos pelo cliente de cada loja, então overlay, nuvem de saves e DRM continuam funcionando.

Inspirado em [GamingConsoleMode](https://github.com/toonymak1993/GamingConsoleMode) e [Playnite](https://github.com/JosefNemec/Playnite).

## Capturas de tela

<p align="center"><img src="docs/screenshots/inicio.png" alt="Tela inicial" width="900"><br><sub>Tela inicial: fundo com a arte do jogo, jogos recentes, atalhos e barra superior</sub></p>

<p align="center"><img src="docs/screenshots/launchers.png" alt="Launchers" width="900"><br><sub>Launchers: Steam e Epic Games com a quantidade de jogos instalados</sub></p>

## Instalação

1. Abra a página de [**Releases**](../../releases) do projeto.
2. Baixe o **`LiteLauncher-Setup.exe`**.
3. Execute o arquivo e siga o instalador. O Windows pode mostrar o aviso "O Windows protegeu seu computador", pois o programa não tem assinatura digital paga. Clique em **Mais informações → Executar assim mesmo**.

Não precisa ter o .NET instalado.

## Modo Xbox do Windows (tela cheia)

O LiteLauncher é um programa comum e **não aparece sozinho** na lista de *Configurações → Jogos → Modo Xbox*: o Windows só lista ali apps empacotados com uma permissão especial de "aplicativo de jogos", que a Microsoft não libera para projetos independentes.

Para usar o LiteLauncher nesse modo é necessário o [**AnyFSE**](https://github.com/ashpynov/AnyFSE), um projeto de terceiros (sem vínculo com o LiteLauncher). Ele aparece na lista do Windows como aplicativo inicial, abre o launcher que você escolher e some. Resumo:

1. O Windows precisa estar com o Modo Xbox (Full Screen Experience) habilitado. O AnyFSE não o liga sozinho.
2. Instale o AnyFSE seguindo as instruções do projeto (precisa de administrador).
3. Abra o AnyFSE, clique com o botão direito e escolha **Configure**. Indique o executável do LiteLauncher (`LiteLauncher.exe`, normalmente em `%LOCALAPPDATA%\Programs\LiteLauncher`) como launcher personalizado. Use o argumento `--fullscreen` se o campo de argumentos existir.
4. Em *Configurações → Jogos → Modo Xbox*, escolha o **AnyFSE** como aplicativo inicial.

Isso ainda não foi testado por nós com o LiteLauncher. Detalhes, limitações e a observação do autor sobre antivírus que podem marcar o AnyFSE como falso positivo estão na página dele.

## Recursos

**Tela inicial estilo Xbox**
- Fundo que muda para a arte do jogo selecionado, ocupando a tela inteira como na tela inicial do Xbox, com sombreamento suave nos cantos. Em Configurações → Tema você pode escolher o estilo com a imagem só na parte de cima, esmaecendo para o fundo (inteira ou preenchendo)
- Cartão grande do último jogo jogado e fila dos jogos recentes
- Atalhos: *My games & apps*, *Launchers*, *Settings* e *Friends*; menu de texto (Store, Community, Exit)
- Menus de rede, volume e energia seguem o mesmo visual plano estilo Xbox One das demais telas
- Perfil, relógio e bateria (notebooks e portáteis) na barra superior; dicas de botões do controle no rodapé (A, B, X, Y, Start = detalhes, Select = energia)
- O jogo em destaque se atualiza sozinho quando você joga, instala ou desinstala algo

**Biblioteca ("My games & apps")**
- Lê os jogos instalados de **todas** as pastas de biblioteca da Steam e os jogos instalados da Epic
- Abas **Installed / Recent / Steam / Epic Games / Favourites**: `LB` e `RB` no controle (`PageUp` e `PageDown` no teclado) trocam de aba. *Recent* mostra só o que você já jogou
- **Ordenação**: `RT` no controle (ou `F2`, ou um clique no botão *Sort*) alterna entre Recentes, A–Z e Tamanho em disco; a escolha fica salva
- **Favoritos**: `X` no controle (ou `F`) marca ou desmarca o jogo
- **Capas automáticas**: se a Steam ainda não guardou a capa ou o fundo de um jogo, o LiteLauncher baixa a arte da loja da Steam e guarda em `%APPDATA%\LiteLauncher\covers` (a Epic não oferece arte pública; esses jogos continuam com a letra)
- Busca: comece a digitar, `Ctrl+F` ou `Y` no controle. No controle, `Y` abre o teclado do Windows, `Start` confirma a busca e `B` fecha o teclado (um segundo `B` fecha a busca)

**Tela de abertura do jogo (overlay)**
- Cartão com a capa e contagem de 3 segundos; `B` ou `Esc` cancela
- Durante o jogo ele vira a tela **Jogando agora**: tempo da sessão, botão **Back to game** (traz a janela do jogo para a frente) e o fechamento forçado. Quando você fecha o jogo, o LiteLauncher volta à frente na tela inicial. O botão **Home** (ou `B`) esconde o overlay e deixa um atalho com o nome do jogo na barra superior: `Seta para cima` até ele e `A` trazem o overlay de volta. Assim você usa o launcher (e, no futuro, outros apps) com o jogo aberto
- **Fechamento forçado**: botão *Force close game* (aperte duas vezes para confirmar). Encerra os processos da pasta do jogo e volta ao início, útil quando o jogo trava ou não abre. Com o jogo em tela cheia, segure **Select + Start** por 1,5 s para trazer o launcher de volta. Jogos com anti-cheat podem recusar o encerramento; nesse caso use o Gerenciador de Tarefas

**Menu do jogo** (`Start` no controle, tecla Menu ou botão direito)
- Informações da loja (desenvolvedor, lançamento, jogadores online, gêneros, descrição), tempo jogado, última vez e tamanho em disco
- **Conquistas** (Steam): progresso com barra e lista completa (ícone, nome, descrição, data). Precisa estar logado na Steam, com "Detalhes do jogo" público. A Epic não oferece conquistas a outros programas
- **Manage**: renomear, escolher capa/fundo próprios, ou restaurar os originais (só muda no LiteLauncher)
- **SteamGridDB**: busca capas, fundos e logos da comunidade (precisa de uma chave gratuita)

**Amigos (Steam)**
- Amigos online, com os que estão jogando primeiro e o nome do jogo; `A` abre a conversa no cliente da Steam
- Precisa de login na Steam e de uma chave da Steam Web API (gratuita)

**Barra superior: rede, volume e energia**
- `Seta para cima` no controle leva à barra superior
- **Rede**: lista as redes Wi-Fi com sinal, conecta em redes salvas ou pede a senha com o teclado virtual. Usa o `netsh wlan` do Windows; o LiteLauncher não guarda senhas
- **Volume**: volume geral do Windows e mudo
- **Energia** (ou `Select` / `F9`): Suspender, Reiniciar e Desligar, com confirmação nos dois últimos

**Launchers**
- Mostra Steam e Epic com a quantidade de jogos instalados. Ao escolher, o cliente abre (Steam na biblioteca) e o LiteLauncher minimiza para ele aparecer.
- Ao jogar, a Steam e a Epic são abertas em segundo plano e minimizadas (se ainda não estiverem abertas), para que o LiteLauncher volte à frente quando o jogo fechar

**Configurações**
- **Som**: volume do sistema e sons de navegação
- **Tema**: cor de destaque, cor de fundo, arte do jogo no fundo (mostrar imagem inteira ou preencher), escurecimento
- **Perfil**: nome, avatar pronto ou foto do seu PC (a Steam não é alterada)
- **Conta Steam**: login oficial pela Steam (OpenID) ou reutilizar a conta do cliente
- **Geral**: **idioma** (English, Português do Brasil e Español; na primeira vez segue o idioma do Windows), iniciar em tela cheia, **iniciar junto com o Windows**, verificar atualizações e reler a biblioteca

**Atualizações**
- Ao abrir (no máximo uma vez por dia) o LiteLauncher consulta a última versão publicada em Releases e avisa se houver uma nova; em Configurações → Geral o botão abre a página de download. Nada é baixado nem instalado sozinho. Funciona nas versões geradas pelo fluxo de Release do GitHub (o endereço do repositório e o número da versão entram no programa nessa hora); builds locais de desenvolvimento não consultam

**Entradas:** teclado, mouse e controles. Controle Xbox (e qualquer um que a Steam Input mostre como Xbox) funciona direto. **PlayStation (DualShock 4 / DualSense), Switch Pro e controles DirectInput genéricos** também são lidos pelo Windows, sem programa extra: `X`/Cruz seleciona, Círculo volta, Triângulo busca, Quadrado favorita, `Options` abre o menu do jogo, `Share`/`Create` abre a energia e `L1`/`R1`/`R2` trocam abas e ordenação (a numeração dos botões desses controles foi montada pela documentação e ainda não foi testada em todos os modelos; se algum botão estiver trocado, abra uma issue com o modelo). Direcional ou analógico esquerdo movem; `Start` abre o menu do jogo ou atualiza. `F11` alterna tela cheia; `--fullscreen` inicia assim.

Configurações ficam em `%APPDATA%\LiteLauncher\settings.json`. Se algo der errado, o launcher continua aberto e registra os detalhes em `%APPDATA%\LiteLauncher\crash.log`; envie esse arquivo ao relatar um problema. Suas chaves de API ficam nessa pasta, nunca no projeto.

## Sobre o "Entrar com a Steam"

Usa o login oficial da Steam (OpenID 2.0): o navegador abre `steamcommunity.com`, você entra normalmente (com Steam Guard), a Steam devolve a resposta a um ouvinte local temporário em `127.0.0.1`, o LiteLauncher pede à Steam para validá-la e só então guarda seu **SteamID64 público**. Senha e tokens nunca passam pelo programa. Perfis **privados** não expõem nome nem foto.

## Roadmap

- [ ] Provedores GOG e Xbox / Game Pass
- [ ] Jogos da Steam não instalados
- [ ] Arte para jogos da Epic (via SteamGridDB)
- [ ] Perfis de controle e remapeamento de botões
- [ ] Instalar atualizações automaticamente (hoje o programa só avisa)
- [ ] Assinatura de código do instalador
- [x] Iniciar junto com o Windows
- [x] Aviso de atualização
- [x] Download de capas ausentes (Steam)
- [x] Ordenação e aba Recentes
- [x] Controles PlayStation, Switch e DirectInput
- [x] Idiomas: Português (Brasil) e Espanhol (traduções feitas por máquina; correções são bem-vindas em `src/LiteLauncher.Core/Translations.cs`)

## Avisos

- Sem vínculo com Valve, Microsoft, Xbox ou Epic Games. Steam é marca da Valve Corporation. O visual é inspirado em painéis de console e não inclui arte ou recursos do Xbox.
- Jogos sem imagem no cache da Steam mostram uma letra no lugar da capa.

## Licença

Veja o arquivo [LICENSE](LICENSE).
