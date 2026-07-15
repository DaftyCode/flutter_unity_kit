# Unity Kit Exporter

Editor-only Unity package used to export Unity as a library and deploy it into
a Flutter application. It supports Android, iOS and WebGL.

Install it from a Git repository with Unity Package Manager, selecting this
directory as the package path. For example:

```text
https://github.com/erykkruk/flutter_unity_kit.git?path=/unity_kit/unity_exporter
```

When the Unity project is nested anywhere below a Flutter project, the exporter
finds the nearest `pubspec.yaml` automatically. For other layouts, configure the
path in **Flutter > Settings** or set `UNITY_KIT_FLUTTER_PROJECT` in CI.

Use **Flutter > Export Android**, **Flutter > Export iOS**, or
**Flutter > Export WebGL**. An export creates a standalone artifact and deploys
it automatically when a Flutter project is available.
