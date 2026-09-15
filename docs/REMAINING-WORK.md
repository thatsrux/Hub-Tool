# Evoluzione del prodotto

La richiesta corrente riguarda il perfezionamento della UI, il filtro delle periferiche, le icone, la compattezza e l'overlay a moduli. La verifica di questa revisione è in [UI-AUDIT.md](UI-AUDIT.md).

Il precedente obiettivo di controllare qualsiasi funzione di qualsiasi componente è stato chiarito: CPU, bus, controller host e componenti interni non devono apparire nell'elenco. Non vengono quindi considerati periferiche da aggiungere alla UI.

Restano possibili evoluzioni del prodotto, distinte dalla revisione UI:

- Altri provider documentati per funzioni proprietarie, senza indovinare protocolli HID.
- Selezione del monitor e calibrazione dell’ordine fisico dei LED per configurazioni QuikLight diverse da quella verificata a 54 LED/tre lati.
- Associazione manuale di ID hardware cambiati, sequenze di shortcut e mixer per applicazione.
- Firma Authenticode, installer e architetture aggiuntive.
- Ulteriori misure su PC/driver diversi. L'overlay standard non garantisce il fullscreen esclusivo o i desktop protetti.

La disponibilità di un dispositivo non implica che tutte le funzioni del suo driver siano esposte da Windows. La matrice CAPABILITIES descrive i controlli implementati.
