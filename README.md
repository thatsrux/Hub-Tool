# Hub Tool

Un centro di controllo nativo per Windows: dispositivi, audio, profili e shortcut, con un overlay discreto.

**0.5.1 è una prerelease.** L'elenco riguarda le periferiche d'interfaccia: audio, monitor, tastiere, mouse, videocamere, illuminazione compatibile, stampanti, scanner e hub USB esterni. Non include CPU, bus, controller host o altri nodi interni. La copertura dei controlli è descritta nella [matrice delle capacità](docs/CAPABILITIES.md).

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
- Conservare i dispositivi scollegati, dimenticare quelli indesiderati e ripristinarli in seguito dalla pagina Overlay.
- Regolare volume, mute, canali e livello in dB di cuffie, altoparlanti e microfoni esposti da Core Audio.
- Attivare, disattivare e regolare l'eco microfono/sidetone nelle cuffie quando il driver lo espone nella topologia audio Windows. Se il nodo mute del driver non è scrivibile, Hub usa il volume minimo e ricorda il livello da ripristinare.
- Regolare velocità puntatore, doppio clic, rotella, pulsante principale e ripetizione tastiera. Queste preferenze Windows sono globali, non per singolo mouse o tastiera.
- Regolare luminosità e contrasto dei monitor che rispondono alle API DDC/CI.
- Vedere l’anteprima live nativa della webcam mentre si regolano i controlli standard esposti da IAMCameraControl/IAMVideoProcAmp.
- Ottimizzare in un clic l’immagine con **Enhance with AI**: il fotogramma viene analizzato localmente e Hub regola luce, contrasto, colore, nitidezza, gain e modalità automatiche effettivamente supportate.
- Salvare più profili immagine per ogni videocamera, applicarli o eliminarli; ripristinare inoltre i valori predefiniti dichiarati dal driver.
- Controllare direttamente le luci monitor DX Light/QuikLight USB `1A86:FE07`: accensione, colore RGB/HEX, preset e luminosità.
- Sincronizzare 54 zone LED con i bordi dello schermo, regolando fluidità, intensità dei colori e morbidezza delle transizioni.
- Prendere automaticamente il controllo quando DX Light viene chiuso e riapplicare lo stato salvato, evitando che le luci restino spente. Hub e DX Light non accedono mai contemporaneamente al controller.
- Salvare profili di audio, canali, input e monitor. I valori dei dispositivi assenti vengono conservati per la riconnessione.
- Registrare shortcut globali per profili, singoli controlli, mute, volume o apertura di file/programmi con argomenti.
- Importare/esportare profili `.hubprofile`; l'importazione non applica automaticamente impostazioni o comandi.
- Personalizzare l’overlay da una pagina dedicata: barra di sole icone orizzontale o verticale, dimensione, opacità, primo piano e compressione automatica. Ogni icona apre soltanto il proprio pannello; `Ctrl+Alt+H` mostra o nasconde la barra.
- Aprire i pannelli Windows della categoria per le impostazioni non ancora integrate.

## Uso

1. Avvia `HubTool.exe`. La scansione iniziale non cambia le impostazioni del PC.
2. Seleziona un dispositivo. Gli slider sono continui durante il trascinamento, applicano al massimo ogni 110 ms e arrotondano al passo dichiarato dal driver; puoi anche cliccare direttamente un punto della barra.
3. In **Overlay**, scegli dispositivi e orientamento, poi premi **Mostra / nascondi**. Trascina lo spazio intorno alle icone per spostare la barra e clicca un’icona per aprire quel dispositivo.
4. Per ripristinare i valori all'avvio/riconnessione, attiva la relativa preferenza. Applicare un profilo abilita il ripristino dei dispositivi inclusi.
5. In **Profili**, salva lo stato corrente con un nome nuovo. In **Shortcut**, scegli l'azione e la combinazione; un conflitto con un'altra app viene segnalato.
6. Chiudere la finestra lascia Hub nell'area notifiche. Usa **Esci** nel menu dell'icona per terminarlo.

Per le luci monitor, avvia Hub prima di uscire da DX Light. Finché DX Light è aperto, Hub conserva le modifiche senza contendere la periferica; entro circa un secondo dalla sua chiusura applica colore o sync salvati. Dopo il primo passaggio puoi lasciare DX Light chiuso e usare soltanto Hub.

I dati sono in `%LOCALAPPDATA%\HubTool\settings.json`. Nessun account, server, telemetria o aggiornamento automatico in background. Copiare questo file permette un backup manuale. I valori falliti restano separati dallo stato osservato e vengono ritentati al successivo rilevamento. Gli ID hardware possono cambiare reinstallando driver o spostando alcune periferiche di porta.

## Stack ed efficienza

C# / .NET 8, WPF, NAudio.Wasapi + NAudio.Core 2.2.1, SetupAPI, HID, GDI, AVICap, DirectShow, Core Audio, SystemParametersInfo, API monitor/camera, Shell_NotifyIcon e RegisterHotKey via P/Invoke. Nessun motore browser, cloud per la webcam o dipendenza da Windows Forms. Le licenze delle dipendenze sono in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) e `licenses/`.

L'inventario reagisce agli eventi Windows con debounce di 700 ms. Le notifiche volume sono aggregate per 180 ms e aggiornano i binding senza ricreare i pannelli. SetupAPI e DDC vengono interrogati fuori dal thread grafico. La lista usa virtualizzazione e riciclo. Il solo provider luci controlla a intervalli ridotti la disponibilità di DX Light; la cattura GDI 160 × 90 parte esclusivamente con sync attivo. L'overlay collassato non mantiene i moduli grafici. Nessuna immagine o font aggiuntivo: solo geometrie vettoriali e un'icona EXE da 3,7 KB.

Campione locale dell’EXE autonomo 0.5 in esecuzione con le impostazioni reali: 20 s, 0,25 s CPU (circa 0,08% della capacità totale del Ryzen 7 7700 a 16 thread), 249 MiB di working set e 158 MiB privati. Cattura e invio frame si fermano quando sync è disattivato; l’anteprima webcam esiste solo mentre la relativa scheda è visibile. Per contenere download e memoria preferire la versione compatta con runtime condiviso. Non vengono forzati GC o tagli del working set. I numeri dipendono da runtime, driver e PC.

## Compilazione e verifica

Su Windows con SDK .NET 8:

```powershell
dotnet build src/HubTool/HubTool.csproj -c Release
dotnet run --project tests/HubTool.Tests/HubTool.Tests.csproj -c Release
dotnet run --project tests/HubTool.Tests/HubTool.Tests.csproj -c Release -- --hardware-read
./scripts/package.ps1 -Version 0.5.1
```

I test ordinari verificano persistenza, protocollo luci, merge dell'inventario, riconnessione e profili offline senza cambiare hardware. `--hardware-read` aggiunge letture reali di input, audio, monitor e luci. `--light-write` esegue una scrittura invariata sul controller e va usato con DX Light e Hub chiusi. [Verifica e limiti](docs/VERIFICATION.md).

`HubTool.exe --preview PERCORSO` renderizza le cinque pagine, il layout minimo, la scheda webcam e l’overlay nelle due direzioni; verifica barra del titolo, apertura, Escape, ancoraggio e acquisizione del fotogramma. `--benchmark PERCORSO` misura l'idle senza generare immagini. Entrambi usano preferenze isolate, non registrano shortcut e terminano automaticamente. Le immagini possono mostrare nomi reali dei dispositivi e non vengono caricate automaticamente.

La CI esegue i test su Windows. I tag `v*` attivano il packaging e la pubblicazione di una prerelease GitHub.

## Limiti del prodotto

Il provider luci è limitato al controller DX Light/QuikLight USB verificato (`1A86:FE07`, firmware 1.9.4, 54 LED) e usa al momento lo schermo primario. Enhance usa analisi statistica locale del fotogramma e i controlli standard del driver: il risultato dipende da inquadratura, luce e webcam e non sostituisce i DSP proprietari. L'overlay non garantisce la visibilità su fullscreen esclusivo, desktop protetto o schermate UAC. La presenza nell'elenco non implica che ogni funzione sia controllabile. Il perimetro attuale richiesto esclude i componenti interni del PC.

Aggiornare questo file e [CHANGELOG.md](CHANGELOG.md) a ogni modifica sostanziale, indicando stack, funzionalità, verifica e limiti reali.
