# Verifica — 2026-09-15

## Eseguito localmente

- Build Release con .NET SDK 8.0.425: zero errori e zero avvisi.
- 35 controlli ordinari: persistenza, inventario, migrazione degli endpoint audio e delle shortcut, dispositivo dimenticato e rimosso definitivamente, comando di avvio silenzioso, protocollo QuikLight, raccomandazioni camera e toggle shortcut 0→1→0; tutti passati.
- Scrittura reale invariata con DX Light e Hub chiusi: luminosità e colore statico accettati dal controller USB `1A86:FE07`, senza errori.
- Rendering delle cinque pagine WPF, layout compatto, overlay orizzontale/verticale e pannello espanso completato. Verificati apertura, Escape, ancoraggio e assenza della barra del titolo.
- Anteprima collegata alla EMEET SmartCam e resa come immagine WPF 960 × 540; il fotogramma è visibile anche nella cattura della UI. Enhance ha proposto 10 valori, li ha applicati e riletti tutti dal driver, poi ha ripristinato integralmente i valori iniziali senza errori.
- Toggle sidetone verificato sulla Fifine: Hub ha forzato il fallback sul volume hardware, confermato lo stato disattivato, quindi riattivato l’eco e ripristinato il volume iniziale.
- Passaggio operativo verificato avviando Hub dopo DX Light: importati stato acceso, sync attivo, colore `#E300FF` e 54 LED; nessun errore registrato.
- Campione dell’EXE autonomo 0.5 in esecuzione con le impostazioni reali per 20 s: 0,25 s CPU, circa 0,08% della capacità totale sui 16 thread logici del Ryzen 7 7700; working set 248,8 MiB e memoria privata 158 MiB.

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
