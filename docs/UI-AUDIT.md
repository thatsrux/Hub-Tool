# Verifica richiesta UI e dimensioni

Richiesta corrente: migliorare nettamente app e overlay, usare una barra iniziale di sole icone personalizzabile, rendere gli slider fluidi e integrare anteprima e profili videocamera.

## Evidenze raccolte

- Build e 23 controlli locali passati, inclusi due controlli di lettura hardware.
- Filtro: CPU, bus, host controller USB, root hub e code radice esclusi; hub USB esterni ammessi. Raggruppamento input e deduplicazione monitor verificati.
- Sul PC di verifica: 19 periferiche visibili, 4 endpoint audio e 2 monitor con controlli. I record tecnici duplicati vengono filtrati prima di costruire la UI.
- Icone vettoriali per audio/cuffie, microfoni, tastiere, mouse, monitor, webcam, hub USB, stampanti/scanner e storage. Nessun pacchetto di icone, font o immagine aggiuntivo.
- Verifica visuale tramite rendering delle cinque pagine, layout minimo 900 × 620, scheda videocamera, barra overlay e pannello dispositivo. Verifica della finestra nativa: WS_CAPTION assente.
- Apertura ed Escape esercitati sui gestori WPF reali; ancoraggio preservato. Verificate sia la barra orizzontale sia quella verticale, con tre dispositivi e il pulsante Hub.
- Acquisito e analizzato un fotogramma reale dalla EMEET SmartCam tramite callback AVICap; viene decodificato a 960 × 540 e mostrato come immagine WPF, quindi compare anche nel rendering diagnostico senza bordi o host nativi instabili.
- Overlay verticale verificato con sette icone: dodici ricostruzioni consecutive generano lo stesso hash grafico. Sul bordo destro, l’espansione mantiene fisso il bordo e colloca il pannello direttamente a sinistra.
- Hover verificato spostando realmente il puntatore sulla prima icona: il bordo arrotondato resta completo e la dimensione desiderata del contenuto rientra nella finestra senza clipping.
- Eliminate le dipendenze dirette da Windows Forms e System.Drawing; resta un solo asset, l'icona eseguibile multirisoluzione (3.719 byte).
- Campione idle senza screenshot: 20,006 s, 0 s CPU aggiuntivi misurati, 155,68 MiB working set, 97,26 MiB privati dopo 8 s di warm-up. Nessun GC forzato.

Pacchetto compatto: circa 685 KB EXE / 328 KB ZIP. Autonomo compresso: circa 72 MB EXE; nel campione locale del pacchetto, circa 235 MiB working set e 148 MiB privati. Il runtime condiviso resta l'opzione raccomandata per ridurre sia dimensioni sia memoria.

Le misure sono locali e non provano prestazioni identiche su qualsiasi driver o PC. L'assenza di decorazioni, la compattezza e la divisione in moduli non richiedono injection nei giochi.
