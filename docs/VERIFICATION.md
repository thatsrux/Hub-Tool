# Verifica — 2026-09-15

## Eseguito localmente

- Build Release con .NET SDK 8.0.425: zero errori e zero avvisi.
- 33 controlli con `--hardware-read`: persistenza, inventario, profili, protocollo QuikLight, input, audio, monitor e rilevamento delle luci; tutti passati.
- Scrittura reale invariata con DX Light e Hub chiusi: luminosità e colore statico accettati dal controller USB `1A86:FE07`, senza errori.
- Rendering delle quattro pagine WPF, layout compatto e overlay completato. La scheda luci mostra stato DX Light, interruttori, luminosità, preset, HEX/RGB e parametri sync.
- Passaggio operativo verificato avviando Hub dopo DX Light: importati stato acceso, sync attivo, colore `#E300FF` e 54 LED; nessun errore registrato.
- Campione dell’EXE autonomo con sync attivo a 15 fps per 20 s: 2,109 s CPU, pari a circa 0,659% della capacità totale sui 16 thread logici del Ryzen 7 7700; working set 272,6 MiB, memoria privata 161,4 MiB, nessuna crescita nel campione.

## Limiti della verifica

- La prova riguarda il controller Robobloq/DX Light rilevato, firmware 1.9.4 e 54 LED. Altri VID/PID o layout richiedono una verifica dedicata.
- Il sync usa lo schermo primario e una disposizione a tre lati. Se la striscia è installata in ordine diverso, i colori possono richiedere una futura calibrazione della mappa.
- La resa visiva fisica dei singoli LED non può essere valutata automaticamente; protocollo, apertura HID e scrittura sono stati verificati.
- Non è stata verificata la resa dell'overlay con tutti i giochi, DPI e configurazioni multimonitor.
- Firma Authenticode, certificazione, installer MSIX e supporto ARM64 restano aperti.

## Riproduzione

```powershell
dotnet build src/HubTool/HubTool.csproj -c Release
dotnet run --project tests/HubTool.Tests/HubTool.Tests.csproj -c Release
dotnet run --project tests/HubTool.Tests/HubTool.Tests.csproj -c Release -- --hardware-read
# Solo con DX Light e Hub chiusi; invia gli stessi valori già configurati:
dotnet run --project tests/HubTool.Tests/HubTool.Tests.csproj -c Release -- --light-write
```

`HubTool.exe --preview PERCORSO` produce schermate e controlli UI in una cartella dati isolata. `--benchmark PERCORSO` misura l’attività a finestra nascosta.
