# Dynamic Splat Viewer
## Belegarbeit AR SS24
Erstellt von: Christopher van Le

## Beschreibung
Der Dynamic Splat Viewer ist eine Unity VR Anwendung für die Windows PLattform. Sie baut auf folgendem Repository auf:
[UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting). Der grundlegende Unterschied zwischen den zwei Projekten, ist dass der
Dynamic Splat Viewer die Splatting Technik in Echtzeit anwendet und die Punktwolke dynamisch verändert werden kann. Im
Repository von Aras Pranckevičius wird die Punktwolke nur einmalig im Editor gesetzt und dann in der Runtime gerendert.
In meinem Projekt wurden die Shader so angepasst, dass die Punktwolke in Echtzeit verändert werden kann. Außerdem wurde
ein HTTP Listener implementiert, der den Pfad zu einer Punktwolke entgegennimmt, diese dann in die Anwendung lädt und 
dem Renderer weitergibt.

Unabhängig von der Unity Anwendung wurde das Repository von GraphDeco-Inria [Gaussian-Splatting](https://github.com/graphdeco-inria/gaussian-splatting)
verwendet, um die Splatting Technik zu implementieren. Der Code wurde nur geringfügig angepasst. Bis auf Änderungen in
den Konfiguriationsdateien und der Implementierung eines HTTP Senders, wurde der Code nicht verändert.

## Dokumentation der Änderungen
### [UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting)
Die Codeanpassungen wurden in den folgenden Dateien vorgenommen:

![Diff](/docs/Images/diff.png?raw=true "Diff")

Für eine genauere Ansicht, kann der Diff-Viewer im Repository aufgerufen werden:

[Diff-Viewer](https://github.com/aras-p/UnityGaussianSplatting/compare/main...christophervan-le-mw:UnityGaussianSplatting:main)

### [Gaussian-Splatting](https://github.com/graphdeco-inria/gaussian-splatting)

Die minimalen Änderungen können der Vollständigkeit halber hier eingesehen werden:

[Diff-Viewer](https://github.com/graphdeco-inria/gaussian-splatting/compare/main...christophervan-le-mw:gaussian-splatting:main)

### [XR Interaction Toolkit Starter Assets](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.0/manual/samples-starter-assets.html)

Die XR Interaction Toolkit Starter Assets wurden als Basis für die VR Anwendung verwendet. UI Elemente und Interaktionen 
wurden teilweise übernommen und angepasst. Vorallem das gut ausgestattete XR Rig haben bei der Entwicklung geholfen.

## Nutzung

