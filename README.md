# Hub Tool

Un centro di controllo nativo per Windows: dispositivi, audio, profili e shortcut, con un overlay discreto.

**0.2.0 è una prerelease in sviluppo.** Non offre ancora tutte le funzioni di ogni dispositivo. La copertura effettiva è descritta nella [matrice delle capacità](docs/CAPABILITIES.md).

## Download

[Release GitHub](https://github.com/thatsrux/Hub-Tool/releases) · [Codice sorgente](https://github.com/thatsrux/Hub-Tool)

- `HubTool-…-win-x64.exe`: eseguibile autonomo, senza installazione di .NET.
- `…-portable.zip`: lo stesso eseguibile con documentazione e licenze. È il pacchetto consigliato per conservare tutto insieme.
- `…-compact.zip`: pacchetto molto più piccolo; richiede il [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
- `SHA256SUMS-win-x64.txt`: impronte dei file distribuiti, verificabili con `Get-FileHash` in PowerShell.

I binari non sono ancora firmati con un certificato Authenticode. Gli hash permettono di controllare l'integrità, ma non sostituiscono la firma dell'editore. Non occorre disabilitare le protezioni del PC. È possibile compilare dal sorgente. L'app non richiede privilegi amministrativi.

## Cosa puoi fare

- Cercare tutti gli elementi Plug and Play rilevati da Windows, inclusi componenti interni e virtuali; vedere produttore, classe e ID del driver.
- Conservare i dispositivi scollegati, le preferenze dell'overlay e le impostazioni supportate.
- Regolare volume, mute, canali e livello in dB di cuffie, altoparlanti e microfoni esposti da Core Audio.
- Regolare velocità puntatore, doppio clic, rotella, pulsante principale e ripetizione tastiera. Queste preferenze Windows sono globali, non per singolo mouse o tastiera.
- Regolare luminosità e contrasto dei monitor che rispondono alle API DDC/CI.
- Salvare profili di audio, canali, input e monitor. I valori dei dispositivi assenti vengono conservati per la riconnessione.
- Registrare shortcut globali per profili, singoli controlli, mute, volume o apertura di file/programmi con argomenti.
- Scegliere i dispositivi dell'overlay, espanderlo/comprimerlo e focalizzarlo con `Ctrl+Alt+H` (modificabile). `Esc` lo nasconde.
- Aprire i pannelli Windows della categoria per le impostazioni non ancora integrate.

## Uso

1. Avvia `HubTool.exe`. La scansione iniziale non cambia le impostazioni del PC.
2. Seleziona un dispositivo. Gli slider applicano il valore al rilascio del mouse o del tasto; questo evita una raffica di scritture ai driver.
3. Attiva **Mostra nell'overlay** sui dispositivi desiderati.
4. Per ripristinare i valori all'avvio/riconnessione, attiva la relativa preferenza. Applicare un profilo abilita il ripristino dei dispositivi inclusi.
5. In **Profili**, salva lo stato corrente con un nome nuovo. In **Shortcut**, scegli l'azione e la combinazione; un conflitto con un'altra app viene segnalato.
6. Chiudere la finestra lascia Hub nell'area notifiche. Usa **Esci** nel menu dell'icona per terminarlo.

I dati sono in `%LOCALAPPDATA%\HubTool\settings.json`. Nessun account, server, telemetria o aggiornamento automatico in background. Copiare questa cartella permette un backup manuale; gli ID hardware possono cambiare reinstallando driver o spostando alcune periferiche di porta.

## Stack ed efficienza

C# / .NET 8, WPF, NAudio.Wasapi + NAudio.Core 2.2.1, SetupAPI, Core Audio, SystemParametersInfo, API monitor e RegisterHotKey via P/Invoke. Nessun motore browser incorporato. Le licenze delle dipendenze sono in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) e `licenses/`.

L'inventario reagisce agli eventi Windows con debounce di 700 ms. Le notifiche volume sono aggregate per 180 ms solo quando arrivano cambiamenti. SetupAPI e DDC vengono interrogati fuori dal thread grafico. La lista usa virtualizzazione e riciclo. Non c'è polling periodico dei dispositivi.

L'ottimizzazione è ancora aperta: il requisito di consumo minimo non è ancora dimostrato. Una prima misura dopo il rendering delle quattro pagine ha mostrato circa 187 MiB di working set, 119 MiB privati e 0,625 secondi CPU su un campione di 15 secondi. Questo campione include gli effetti del rendering diagnostico, non rappresenta un benchmark universale né un limite garantito.

## Compilazione e verifica

Su Windows con SDK .NET 8:

```powershell
dotnet build HubTool/HubTool.csproj -c Release
dotnet run --project HubTool.Tests/HubTool.Tests.csproj -c Release
dotnet run --project HubTool.Tests/HubTool.Tests.csproj -c Release -- --hardware-read
./scripts/package.ps1 -Version 0.2.0
```

I test ordinari verificano persistenza, merge dell'inventario, riconnessione e profili offline senza cambiare hardware. `--hardware-read` aggiunge letture reali di input, audio e monitor. [Verifica e limiti](docs/VERIFICATION.md).

Per controllare il layout, `HubTool.exe --preview PERCORSO` renderizza le quattro pagine, raccoglie un campione di risorse e termina. Usa preferenze isolate e non registra shortcut. Le immagini possono mostrare i nomi reali dei dispositivi: non vengono caricate automaticamente.

La CI esegue i test su Windows. I tag `v*` attivano il packaging e la pubblicazione di una prerelease GitHub.

## Stato dell'obiettivo

Restano integrazioni per funzioni proprietarie, controlli avanzati dei dispositivi, maggiore copertura di test hardware, verifica interattiva dell'overlay e ulteriori ottimizzazioni. L'overlay topmost non garantisce la visibilità su fullscreen esclusivo, desktop protetto o schermate UAC. La presenza nell'inventario non implica che ogni funzione sia controllabile.

Aggiornare questo file e [CHANGELOG.md](CHANGELOG.md) a ogni modifica sostanziale, indicando stack, funzionalità, verifica e limiti reali.
