# Changelog

## 0.3.4 — 2026-09-15

- Aggiunto il pulsante per ripristinare tutti i controlli supportati della videocamera ai valori predefiniti dichiarati dal driver.
- Il reset continua sugli altri parametri se il driver rifiuta una singola proprietà e mostra un riepilogo dell'esito.
- Dopo il reset, slider e modalità automatiche vengono riletti direttamente dalla videocamera.

## 0.3.3 — 2026-09-15

- Corretto l'interruttore eco microfono sui driver, incluso Fifine, che espongono un nodo mute ma ignorano la scrittura.
- Aggiunto un fallback che disattiva il sidetone tramite il livello minimo e ripristina il volume precedente alla riattivazione.
- Conservati stato disattivato e volume precedente tra riavvio, profili e riconnessioni.

## 0.3.2 — 2026-09-15

- Aggiunti volume e interruttore dell'eco microfono/sidetone nella scheda dell'uscita audio associata.
- Rilevato il percorso microfono nella topologia hardware Windows; i controlli compaiono solo quando sono realmente esposti dal driver.
- Inclusi i nuovi valori in profili, shortcut e ripristino alla riconnessione.

## 0.3.1 — 2026-09-14

- Riordinato il repository: applicazione in `src/`, verifiche in `tests/` e risorse di progetto nelle cartelle dedicate.
- Aggiunto `HubTool.exe` autonomo direttamente nella cartella principale, con nome stabile e senza dipendenza dal runtime .NET installato.
- Aggiornati packaging, documentazione e workflow GitHub ai nuovi percorsi.

## 0.3.0 — 2026-09-13

- Verificata e completata l'iterazione interrotta: build senza errori e test su profili, richieste parzialmente fallite e import/export.
- Limitato il catalogo alle periferiche d'interfaccia; esclusi CPU, bus, controller host, root hub e code radice. Raggruppate interfacce input e rimossi duplicati PnP dei monitor.
- Ridisegnata la UI con header compatto, filtri, contatori, icone vettoriali e riepilogo dei profili. Rimossi slogan e testi promozionali.
- Sostituito l'overlay con badge circolare 52 × 52 DIP, senza decorazioni native, trascinabile; apertura a moduli separati, Escape e posizione persistente.
- Aggiunti controlli webcam standard senza cattura video e aggiornamenti audio via binding.
- Eliminata Windows Forms; tray e monitor work area usano API native. Nessun asset raster o font superfluo; EXE autonomo compresso e variante EXE compatta.
- Aggiunti test del filtro e del raggruppamento, rendering a dimensioni minime, verifica nativa della barra del titolo e benchmark senza immagini.

## 0.2.0 — 2026-09-13

- Aggiunti controlli Windows diretti di mouse e tastiera, luminosità/contrasto DDC/CI, canali audio e dB.
- Estesi i profili ai controlli implementati e le shortcut a singoli dispositivi e programmi con argomenti.
- Corretto il merge dei dispositivi assenti e lo stato di connessione al riavvio; validati JSON e valori non finiti.
- Spostata la discovery lenta fuori dalla UI, aggiunte notifiche volume e lista virtualizzata.
- Ridisegnate le quattro pagine, aggiunti filtri, icona, posizione overlay e protezione dalla doppia istanza.
- Ridotte le dipendenze al solo NAudio.Wasapi/Core; aggiunti test, rendering diagnostico, misure preliminari e workflow GitHub.
- Preparati EXE autonomo, ZIP portable/compatto, licenze e checksum. La release resta sperimentale; copertura universale e consumi minimi non sono dimostrati.
- Pubblicata la repository `thatsrux/Hub-Tool`; reso esplicito l'elenco asset del workflow e validati i percorsi di packaging.
- Verificata la pipeline GitHub e riscaricato l'EXE della prerelease: hash corrispondente, avvio e rendering riusciti. Registrati i divari residui in `docs/REMAINING-WORK.md`.

## 0.1.0 — 2026-09-13 (in sviluppo)

- Creato progetto WPF e inventario SetupAPI persistente.
- Aggiunti endpoint Core Audio, profili, shortcut, tray e overlay.
- Documentati i limiti delle integrazioni disponibili e della verifica.
