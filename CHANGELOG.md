# Changelog

## 0.5.4 — 2026-09-15

- Aggiunta alle shortcut l’azione attiva/disattiva per Eco microfono e per ogni altro controllo binario 0/1.
- Conservata l’azione imposta valore come scelta separata; il campo numerico viene nascosto quando si seleziona il toggle.
- Il toggle parte sempre dallo stato corrente e conserva la destinazione anche se il dispositivo è scollegato.

## 0.5.3 — 2026-09-15

- Sostituito il fragile riquadro AVICap incorporato con una semplice anteprima a fotogrammi WPF 960 × 540, stabile e visibile anche nel rendering dell’app.
- Limitato l’aggiornamento dell’anteprima a quattro fotogrammi al secondo e arrestata la cattura appena si lascia la scheda della videocamera.
- Enhance ora verifica ogni valore rileggendolo dal driver; la prova hardware ha applicato 10 regolazioni su 10 e ripristinato le impostazioni iniziali senza errori.

## 0.5.2 — 2026-09-15

- Corretto il ritaglio del bordo hover delle icone: la misura dell’overlay ora include integralmente margini, padding e bordo sull’asse corto.
- Aggiunta una prova con puntatore reale sull’icona e cattura del bordo completo, mantenendo lo stile hover esistente.

## 0.5.1 — 2026-09-15

- Corretto il rendering intermittente delle icone nell’overlay verticale con composizione stabile, cache raster locale e pulsanti visivamente separati.
- Eliminato il ridimensionamento in due fasi: quando la barra è sul bordo destro, il pannello viene misurato prima e si apre direttamente a sinistra mantenendo fermo il bordo.
- Aggiunta una verifica UI con sette icone, dodici ricostruzioni identiche e apertura dal bordo destro verso sinistra.

## 0.5.0 — 2026-09-15

- Ridisegnata l’intera interfaccia con superfici, controlli, stati focus e icona Hub vettoriale più leggibili.
- Sostituito il vecchio badge overlay con una barra di icone orizzontale o verticale: ogni icona apre soltanto il relativo dispositivo in un pannello compatto e rifinito.
- Aggiunta la pagina Overlay per orientamento, dimensione icone, opacità, primo piano, compressione automatica, posizione e scelta dei dispositivi.
- `Ctrl+Alt+H` ora mostra o nasconde l’overlay; la vecchia shortcut viene migrata automaticamente.
- Aggiunta la rimozione persistente di un dispositivo con recupero dalla pagina Overlay; vengono puliti anche i riferimenti in profili e shortcut.
- Corretta la luminosità QuikLight: Hub converte il valore di attenuazione USB, quindi 0% indica il minimo e 100% il massimo.
- Slider ingranditi e resi fluidi: click diretto sul punto, trascinamento continuo, applicazione live limitata a 110 ms e discretizzazione esatta sul passo del driver.
- Aggiunta anteprima live nativa nella scheda videocamera, senza framework video aggiuntivi.
- Aggiunto “Enhance with AI”: analisi locale del fotogramma e ottimizzazione dei controlli immagine e delle modalità automatiche supportate.
- Aggiunti profili immagine distinti per ogni videocamera, con salvataggio, applicazione ed eliminazione.

## 0.4.0 — 2026-09-15

- Aggiunto il dispositivo “Luci dietro al monitor” per il controller DX Light/QuikLight USB `1A86:FE07`, rilevato e controllato direttamente via HID.
- Aggiunti accensione, luminosità, colore RGB/HEX, preset, sync schermo a 54 zone, fluidità, intensità e smoothing.
- Importate al primo avvio le impostazioni già presenti in DX Light; profili, shortcut, overlay e ripristino includono i controlli luce.
- Hub attende senza conflitti mentre DX Light è aperto e riapplica automaticamente l’ultimo stato dopo la sua chiusura, senza spegnere le luci alla propria uscita.
- Verificati protocollo, rilevamento e scrittura reale invariata sul controller; aggiunto benchmark con sync attivo.

## 0.3.5 — 2026-09-15

- Rimossi dall'inventario i nodi Tastiera HID, HID-compliant mouse e le altre interfacce input generiche; restano una sola voce Mouse e Tastiera con i controlli Windows.
- Escluse webcam virtuali come OBS Virtual Camera e code software come OneNote, Microsoft Print to PDF, XPS e Fax.
- Deduplicate le interfacce scanner dello stesso dispositivo multifunzione, mantenendo la stampante hardware effettiva.

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
