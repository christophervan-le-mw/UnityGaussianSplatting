# Dynamic Splat Viewer
## Belegarbeit AR SS24
Erstellt von: Christopher van Le

## Beschreibung
Der Dynamic Splat Viewer ist eine Unity VR Anwendung für die Windows PLattform. Sie baut auf folgendem Repository auf:
https://github.com/aras-p/UnityGaussianSplatting. Der grundlegende Unterschied zwischen den zwei Projekten, ist dass der
Dynamic Splat Viewer die Splatting Technik in Echtzeit anwendet und die Punktwolke dynamisch verändert werden kann. Im
Repository von Aras Pranckevičius wird die Punktwolke nur einmalig im Editor gesetzt und dann in der Runtime gerendert.
In meinem Projekt wurden die Shader so angepasst, dass die Punktwolke in Echtzeit verändert werden kann. Außerdem wurde
ein HTTP Listener implementiert, der den Pfad zu einer Punktwolke entgegennimmt, diese dann in die Anwendung lädt und 
dem Renderer weitergibt.

## Codeanpassungen
Die Codeanpassungen wurden in den folgenden Dateien vorgenommen:
![Diff](/docs/Images/diff.jpg?raw=true "Diff")

Für eine genauere Ansicht, kann der Diff-Viewer im Repository aufgerufen werden:
[Diff-Viewer](https://github.com/aras-p/UnityGaussianSplatting/compare/main...christophervan-le-mw:UnityGaussianSplatting:main)

