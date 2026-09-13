Hub Tool 0.3.0: revisione dell'interfaccia e dell'overlay.

- Solo periferiche d'interfaccia: esclusi CPU, bus, controller host e root hub; icone specifiche per tipo.
- UI più compatta, filtri per categoria, contatori e riepilogo dei profili; rimossi slogan.
- Overlay circolare 52 × 52 DIP, trascinabile e senza barra del titolo. Il clic apre schede separate; Escape comprime mantenendo la posizione.
- Verificata l'iterazione precedente; aggiunti import/export profili e conservazione delle impostazioni fallite.
- Rimossa Windows Forms, nessun asset aggiuntivo oltre all'icona EXE. EXE compatto per .NET 8 Desktop Runtime e EXE autonomo compresso.

Build e controlli automatici passati, incluso filtro hardware e rendering a dimensione minima. L'EXE non è firmato Authenticode; i checksum SHA-256 sono inclusi. Consulta README per runtime richiesto e limiti dei driver.
