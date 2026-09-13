# Verifica — 2026-09-13

## Eseguito localmente

- Build Release .NET SDK 8.0.425: zero errori e zero avvisi, con output isolato perché una build precedente era aperta.
- 11 controlli automatici di persistenza/inventario/profili: passati.
- 2 controlli aggiuntivi di lettura hardware: passati; 310 elementi, 4 endpoint audio, 2 monitor con controlli, zero errori di discovery.
- Rendering delle 4 pagine WPF da visual tree reale. Corretto contrasto della ComboBox, tema della finestra derivata e cattura delle dimensioni complete.
- Audit dipendenze NuGet: nessun pacchetto vulnerabile segnalato dalla sorgente consultata.
- Misurazione preliminare in finestra nascosta dopo rendering: 15,01 s, 0,625 s CPU, 187,32 MiB working set, 119,27 MiB privati. Nessun GC forzato o taglio artificiale del working set.

## Non dimostrato

- Le letture reali non provano tutte le scritture dei driver. Il test di riconnessione è sul modello; non sostituisce stacchi/riattacchi fisici per ogni periferica.
- La verifica delle pagine renderizzate non sostituisce la prova interattiva di tastiera, mouse e lettore di schermo.
- Non è stata verificata la resa dell'overlay con tutti i giochi, DPI e configurazioni multimonitor.
- Non sono state certificate memoria minima, consumo CPU minimo, compatibilità hardware universale o assenza di tutte le vulnerabilità.
- Firma Authenticode, certificazione, installer MSIX e supporto ARM64 restano aperti.

## Riproduzione

Eseguire `HubTool.Tests` su Windows. La modalità standard scrive solo preferenze temporanee e usa dispositivi simulati per le prove di merge/offline. `--hardware-read` legge le API reali senza modificare impostazioni. Il comando `--preview` dell'app scrive immagini e metriche in una cartella scelta, con una nuova cartella dati isolata per ogni esecuzione.
