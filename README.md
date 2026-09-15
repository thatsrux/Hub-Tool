# Hub Tool

Un centro di controllo nativo per Windows: dispositivi, audio, profili e shortcut, con un overlay discreto.

**0.3.5 è una prerelease.** L'elenco riguarda le periferiche d'interfaccia: audio, monitor, tastiere, mouse, videocamere, stampanti, scanner e hub USB esterni. Non include CPU, bus, controller host o altri nodi interni. La copertura dei controlli è descritta nella [matrice delle capacità](docs/CAPABILITIES.md).

## Download

[Release GitHub](https://github.com/thatsrux/Hub-Tool/releases) · [Codice sorgente](https://github.com/thatsrux/Hub-Tool)

Nel repository, `HubTool.exe` si trova direttamente nella cartella principale ed è la build Windows x64 autonoma: basta fare doppio clic e non serve installare .NET.

- `HubTool-…-win-x64-compact.exe`: scelta consigliata su PC con .NET 8 Desktop Runtime; circa 670 KB, senza asset esterni.
- `HubTool-…-win-x64.exe`: eseguibile autonomo compresso, senza installazione di .NET.
- `…-portable.zip`: eseguibile autonomo con documentazione e licenze.
- `…-compact.zip`: pacchetto molto più piccolo; richiede il [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
- `SHA256SUMS-win-x64.txt`: impronte dei file distribuiti, verificabili con `Get-FileHash` in PowerShell.

I binari non sono ancora firmati con un certificato Authenticode. Gli hash permettono di controllare l'integrità, ma non sostituiscono la firma dell'editore. Non occorre disabilitare le protezioni del PC. È possibile compilare dal sorgente. L'app non richiede privilegi amministrativi.

## Cosa puoi fare

- Cercare e filtrare le periferiche d'interfaccia. Ogni tipo ha un'icona vettoriale; interfacce HID generiche, webcam virtuali, stampanti software e duplicati dello stesso scanner vengono esclusi.
- Conservare i dispositivi scollegati, le preferenze dell'overlay e le impostazioni supportate.
- Regolare volume, mute, canali e livello in dB di cuffie, altoparlanti e microfoni esposti da Core Audio.
- Attivare, disattivare e regolare l'eco microfono/sidetone nelle cuffie quando il driver lo espone nella topologia audio Windows. Se il nodo mute del driver non è scrivibile, Hub usa il volume minimo e ricorda il livello da ripristinare.
- Regolare velocità puntatore, doppio clic, rotella, pulsante principale e ripetizione tastiera. Queste preferenze Windows sono globali, non per singolo mouse o tastiera.
- Regolare luminosità e contrasto dei monitor che rispondono alle API DDC/CI.
- Regolare i controlli standard delle webcam esposti da IAMCameraControl/IAMVideoProcAmp, senza acquisire video. I valori disponibili dipendono dal driver.
- Ripristinare in un clic tutti i controlli supportati della videocamera ai valori predefiniti dichiarati dal driver.
- Salvare profili di audio, canali, input e monitor. I valori dei dispositivi assenti vengono conservati per la riconnessione.
- Registrare shortcut globali per profili, singoli controlli, mute, volume o apertura di file/programmi con argomenti.
- Importare/esportare profili `.hubprofile`; l'importazione non applica automaticamente impostazioni o comandi.
- Scegliere i dispositivi dell'overlay: badge circolare da 52 × 52 DIP, trascinabile e senza barra del titolo. Un clic apre moduli separati; `Ctrl+Alt+H` li focalizza. `Esc` comprime e tenta di restituire il focus all'app precedente.
- Aprire i pannelli Windows della categoria per le impostazioni non ancora integrate.

## Uso

1. Avvia `HubTool.exe`. La scansione iniziale non cambia le impostazioni del PC.
2. Seleziona un dispositivo. Gli slider applicano il valore al rilascio del mouse o del tasto; questo evita una raffica di scritture ai driver.
3. Attiva **Mostra nell'overlay** sui dispositivi desiderati, poi premi **Overlay**. Trascina l'icona H per spostarla; cliccala per aprire i moduli. **Disattiva overlay** o il menu destro del badge lo rimuovono.
4. Per ripristinare i valori all'avvio/riconnessione, attiva la relativa preferenza. Applicare un profilo abilita il ripristino dei dispositivi inclusi.
5. In **Profili**, salva lo stato corrente con un nome nuovo. In **Shortcut**, scegli l'azione e la combinazione; un conflitto con un'altra app viene segnalato.
6. Chiudere la finestra lascia Hub nell'area notifiche. Usa **Esci** nel menu dell'icona per terminarlo.

I dati sono in `%LOCALAPPDATA%\HubTool\settings.json`. Nessun account, server, telemetria o aggiornamento automatico in background. Copiare questo file permette un backup manuale. I valori falliti restano separati dallo stato osservato e vengono ritentati al successivo rilevamento. Gli ID hardware possono cambiare reinstallando driver o spostando alcune periferiche di porta.

## Stack ed efficienza

C# / .NET 8, WPF, NAudio.Wasapi + NAudio.Core 2.2.1, SetupAPI, Core Audio, SystemParametersInfo, API monitor/camera, Shell_NotifyIcon e RegisterHotKey via P/Invoke. Nessun motore browser o dipendenza da Windows Forms. Le licenze delle dipendenze sono in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) e `licenses/`.

L'inventario reagisce agli eventi Windows con debounce di 700 ms. Le notifiche volume sono aggregate per 180 ms e aggiornano i binding senza ricreare i pannelli. SetupAPI e DDC vengono interrogati fuori dal thread grafico. La lista usa virtualizzazione e riciclo. Non c'è polling periodico dei dispositivi. L'overlay collassato non mantiene i moduli grafici. Nessuna immagine o font aggiuntivo: solo geometrie vettoriali e un'icona EXE da 3,7 KB.

Campione locale 0.3 con runtime condiviso, a riposo e senza rendering diagnostico: 20 s dopo 8 s di assestamento, nessun tempo CPU aggiuntivo misurato, circa 156 MiB di working set e 97 MiB privati. L'EXE autonomo compresso ha mostrato circa 235 MiB e 148 MiB privati: risparmia download/spazio rispetto al precedente EXE autonomo, ma deve decomprimere il runtime. Per contenere sia download sia memoria preferire la versione compatta con runtime condiviso. Non vengono forzati GC o tagli del working set. I numeri dipendono da runtime, driver e PC.

## Compilazione e verifica

Su Windows con SDK .NET 8:

```powershell
dotnet build src/HubTool/HubTool.csproj -c Release
dotnet run --project tests/HubTool.Tests/HubTool.Tests.csproj -c Release
dotnet run --project tests/HubTool.Tests/HubTool.Tests.csproj -c Release -- --hardware-read
./scripts/package.ps1 -Version 0.3.5
```

I test ordinari verificano persistenza, merge dell'inventario, riconnessione e profili offline senza cambiare hardware. `--hardware-read` aggiunge letture reali di input, audio e monitor. [Verifica e limiti](docs/VERIFICATION.md).

`HubTool.exe --preview PERCORSO` renderizza le pagine, il layout minimo e l'overlay, verifica l'assenza della barra del titolo, clic/apertura, Escape e posizione del badge. `--benchmark PERCORSO` misura l'idle senza generare immagini. Entrambi usano preferenze isolate, non registrano shortcut e terminano automaticamente. Le immagini possono mostrare nomi reali dei dispositivi e non vengono caricate automaticamente.

La CI esegue i test su Windows. I tag `v*` attivano il packaging e la pubblicazione di una prerelease GitHub.

## Limiti del prodotto

Funzioni proprietarie e controlli avanzati dipendono dalle API del produttore. L'overlay non garantisce la visibilità su fullscreen esclusivo, desktop protetto o schermate UAC. La presenza nell'elenco non implica che ogni funzione sia controllabile. Il perimetro attuale richiesto esclude i componenti interni del PC.

Aggiornare questo file e [CHANGELOG.md](CHANGELOG.md) a ogni modifica sostanziale, indicando stack, funzionalità, verifica e limiti reali.
