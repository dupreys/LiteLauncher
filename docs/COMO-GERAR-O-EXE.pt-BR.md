# Como gerar o instalador (.exe) do LiteLauncher

Existem dois caminhos. O **A** não exige instalar nada no seu PC.

## A) Pelo GitHub (recomendado)

1. Crie uma conta e um repositório vazio em github.com (botão **New repository**, nome `LiteLauncher`).
2. Envie o projeto (veja a seção "Enviar para o GitHub" abaixo).
3. Crie uma versão. No terminal, dentro da pasta do projeto:
   ```
   git tag v1.0.0
   git push origin v1.0.0
   ```
4. No GitHub, abra a aba **Actions**: o fluxo "Release" compila, testa e monta o instalador (leva alguns minutos).
5. Quando terminar, na página **Releases** do repositório estarão:
   - `LiteLauncher-Setup.exe`: o instalador (é o que você instala no Windows);
   - `LiteLauncher-portable.zip`: só o programa, sem instalar.

Se o fluxo ficar vermelho, abra a execução, copie a mensagem de erro e envie para quem está ajudando.

## B) No seu PC

1. Instale o [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) e o [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. Abra o PowerShell na pasta do projeto e rode:
   ```
   powershell -ExecutionPolicy Bypass -File build\publish.ps1
   ```
3. O instalador fica em `dist\LiteLauncher-Setup.exe` e o programa solto em `publish\LiteLauncher.exe`.

## Enviar para o GitHub

1. Instale o [Git para Windows](https://git-scm.com/download/win).
2. Na pasta do projeto (a que contém `LiteLauncher.sln`):
   ```
   git init
   git add .
   git commit -m "Primeira versão do LiteLauncher"
   git branch -M main
   git remote add origin https://github.com/SEU_USUARIO/LiteLauncher.git
   git push -u origin main
   ```
   (troque `SEU_USUARIO`; o Git pede o login do GitHub na primeira vez).

Suas chaves de API e configurações ficam em `%APPDATA%\LiteLauncher`, fora do projeto, então não vão para o GitHub.

## Aviso do Windows ao instalar

O instalador não tem assinatura digital paga, então o Windows SmartScreen pode mostrar "O Windows protegeu seu computador".
Clique em **Mais informações → Executar assim mesmo**. Isso é esperado em programas novos e sem certificado comprado.

## Já enviei os arquivos, mas não aparece nenhum .exe

Enviar os arquivos (`git push`) só roda o teste automático. O instalador só é gerado quando você publica uma **tag de versão**:

1. No PowerShell, dentro da pasta do projeto:
   ```
   git tag v1.0.0
   git push origin v1.0.0
   ```
   (para uma nova versão, use outro número, como `v1.0.1`).
2. No GitHub, aba **Actions**, aguarde o fluxo **Release** ficar verde (alguns minutos).
3. Aba **Releases** (coluna da direita da página inicial do repositório): baixe `LiteLauncher-Setup.exe`.

Sem usar o terminal: aba **Actions → Release → Run workflow**. Esse caminho gera os arquivos, mas só como download dentro da própria execução (seção *Artifacts*, no fim da página), sem criar a Release.

Se o fluxo ficar vermelho, abra a execução, clique na etapa com o X e copie a mensagem de erro. Se o projeto estiver numa subpasta do repositório, a pasta `.github` precisa ficar na **raiz** do repositório, senão o GitHub não enxerga os fluxos.
