# Dynamic Splat Viewer

## Belegarbeit AR SS24

Erstellt von: Christopher van Le

## Beschreibung

Der Dynamic Splat Viewer ist eine Unity VR Anwendung für die Windows PLattform. Sie baut auf folgendem Repository auf:
[UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting). Der grundlegende Unterschied zwischen den
zwei Projekten, ist dass der
Dynamic Splat Viewer die Splatting Technik in Echtzeit anwendet und die Punktwolke dynamisch verändert werden kann. Im
Repository von Aras Pranckevičius wird die Punktwolke nur einmalig im Editor gesetzt und dann in der Runtime gerendert.
In meinem Projekt wurden die Shader so angepasst, dass die Punktwolke in Echtzeit verändert werden kann. Außerdem wurde
ein HTTP Listener implementiert, der den Pfad zu einer Punktwolke entgegennimmt, diese dann in die Anwendung lädt und
dem Renderer weitergibt.

Unabhängig von der Unity Anwendung wurde das Repository von
GraphDeco-Inria [Gaussian-Splatting](https://github.com/graphdeco-inria/gaussian-splatting)
verwendet, um die Splatting Technik zu implementieren. Der Code wurde nur geringfügig angepasst. Bis auf Änderungen in
den Konfiguriationsdateien und der Implementierung eines HTTP Senders, wurde der Code nicht verändert.

## Motivation

Das Konzept eines Dynamic Splat Viewer stammt aus der ursprünglichen Idee,
den [GaussianSLAM-Algorithmus](https://github.com/muskie82/MonoGS)
in eine Unity Anwendung zu integrieren. Dabei sollten die Kameras eines HMDs kontinuierlich Trainingsdaten für den
Algorithmus
generieren und deren Splat Repräsentation in Echtzeit über das Passthrough des HMDs überlagert werden. So hätte in der
Theorie, die reale Umgebung nach und nach durch die virtuelle Ersetzt werden können. Da die Implementierung des
GaussianSLAM
jedoch nicht in der vorgegebenen Zeit realisierbar war, wurde die Idee auf ein einfacheres Splatting Verfahren
reduziert.
Anstatt in Echtzeit rekonstruiert zu werden, wird die Umgebung erst vollständig aufgezeichnet und dann unmittelbar
danach
während des Trainings in der Anwendung dargestellt. Ein weiterer Punkt, weswegen der GaussianSLAM nicht umgesetzt wurde,
ist
die hohen Rechenanforderungen des Algorithmus. Selbst auf einem High-End Rechner sind noch keine Ergebnisse zu erwarten,
die in flüssig Echtzeit dargestellt werden können. Das ist besonders wichtig, da die Anwendung für VR gedacht war.

## Dokumentation der Änderungen an bestehenden Repositories

### Unity Anwendung ([UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting))

Die Codeanpassungen wurden in den folgenden Dateien vorgenommen:

![Diff](/docs/Images/diff.png?raw=true "Diff")

Die Datei `GaussianSplatReceiver` wurde hinzugefügt, um den Pfad zu einer Punktwolke per HTTP Post Request zu empfangen.
Außerdem sorgt der Receiver dafür, dass die Punktwolke in die Anwendung geladen wird und dem Renderer übergeben wird.

Der `GaussianSplatRenderer` wurde so angepasst, dass die Punktwolke in Echtzeit verändert werden kann und UI Elemente
das Verhalten des Renderers beeinflussen können.

Das `GaussianSplatRenderAsset` wurde hinzugefüt und baut auf dem `GaussianSplatAsset` auf.

```csharp
byte[]
```

statt

```csharp
NativeArray<byte>
```

um auch während der Laufzeit veränderbar zu sein. Neben vielen weiteren kleineren Änderungen ist dieses Refactoring der
wichtigste Schritt gewesen um die Punktwolken dynamisch laden zu können. Es wurde dabei auch darauf geachtet, dass die
ursprüngliche Funktionalität erhalten bleibt und die Anwendung weiterhin im Editor funktioniert.

Für eine genaue Ansicht, kann der Diff-Viewer im Repository aufgerufen werden (hier wird mein fork mit dem originalen
main-Branch verglichen):
[Diff-Viewer](https://github.com/aras-p/UnityGaussianSplatting/compare/main...christophervan-le-mw:UnityGaussianSplatting:main)

### Python Anwendung ([Gaussian-Splatting](https://github.com/graphdeco-inria/gaussian-splatting))

Die minimalen Änderungen können der Vollständigkeit halber hier eingesehen werden:
[Diff-Viewer](https://github.com/graphdeco-inria/gaussian-splatting/compare/main...christophervan-le-mw:gaussian-splatting:main)

### Unity Assets ([XR Interaction Toolkit Starter Assets](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.0/manual/samples-starter-assets.html))

Die XR Interaction Toolkit Starter Assets wurden als Basis für die VR Anwendung verwendet. UI Elemente und Interaktionen
wurden teilweise übernommen und angepasst. Vorallem das gut ausgestattete XR Rig haben bei der Entwicklung geholfen.

## Funktionsweise und Nutzung

Der Dynamic Splat Viewer ist standalone nicht sinnvoll nutzbar. Es wird ein Server benötigt, der die Punktwolke
bereitstellt.
Das kann entweder ein paralleles Gaussian Splatting Training sein oder bereits vorhandene Punktwolken.

Im hinterlegten Video wird der gesamte Funktionsumfang der Anwendung präsentiert.

### Use-Case 1: Realtime Monitoring des Trainingsprozesses

Das Training von Gaussian Splats, kann sehr rechenintensiv sein. Um den Fortschritt des Trainings zu überwachen, reicht
es oft nicht aus, nur die Metriken des Trainings zu betrachten. Die Punktwolke zu selbstdefinierten Zeitpunkten im
Trainingsprozess zu visualisieren, kann helfen, den Fortschritt besser zu verstehen

### Use-Case 2: Besseres Verständnis über Beschaffenheit der Daten

Auf einem einfachen Monitor ist es oft schwierig, 3D-Szenen zu verstehen. Durch die Visualisierung der Punktwolke in VR
kann der Benutzer beispielsweise fehlerhafte Bereiche in den Quelldaten erkennen, die auf dem Monitor nicht sichtbar
waren. Wenn die Kamera in einer Ecke des Raumes nicht genügend Daten aufgenommen hat, führt dies nur in bestimmten
Winkeln zu einer schlechten Rekonstruktion. In VR kann sich der Benutzer in die Ecke bewegen und die Punktwolke in sechs
Freiheitsgraden analysieren.

