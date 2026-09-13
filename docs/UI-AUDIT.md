# Verifica richiesta UI e dimensioni

Richiesta corrente: verificare l'iterazione interrotta, mostrare solo periferiche d'interfaccia, migliorare spazi e icone, eliminare slogan/asset superflui e sostituire l'overlay-finestra con un'icona a moduli.

## Evidenze raccolte

- Build e 23 controlli locali passati, inclusi due controlli di lettura hardware.
- Filtro: CPU, bus, host controller USB, root hub e code radice esclusi; hub USB esterni ammessi. Raggruppamento input e deduplicazione monitor verificati.
- Sul PC di verifica: 19 periferiche visibili, 4 endpoint audio e 2 monitor con controlli. Due record globali input restano interni al motore dei profili e non compaiono nella UI.
- Icone vettoriali per audio/cuffie, microfoni, tastiere, mouse, monitor, webcam, hub USB, stampanti/scanner e storage. Nessun pacchetto di icone, font o immagine aggiuntivo.
- Verifica visuale tramite rendering delle quattro pagine, layout minimo 900 × 620 e badge/moduli. Verifica della finestra nativa: WS_CAPTION assente.
- Clic sul badge ed Escape esercitati sui gestori WPF reali; dimensione collassata 52 × 52 DIP. Posizione del badge preservata dopo l'apertura.
- Eliminate le dipendenze dirette da Windows Forms e System.Drawing; resta un solo asset, l'icona eseguibile multirisoluzione (3.719 byte).
- Campione idle senza screenshot: 20,006 s, 0 s CPU aggiuntivi misurati, 155,68 MiB working set, 97,26 MiB privati dopo 8 s di warm-up. Nessun GC forzato.

Pacchetto compatto: circa 685 KB EXE / 328 KB ZIP. Autonomo compresso: circa 72 MB EXE; nel campione locale del pacchetto, circa 235 MiB working set e 148 MiB privati. Il runtime condiviso resta l'opzione raccomandata per ridurre sia dimensioni sia memoria.

Le misure sono locali e non provano prestazioni identiche su qualsiasi driver o PC. L'assenza di decorazioni, la compattezza e la divisione in moduli non richiedono injection nei giochi.
