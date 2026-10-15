import 'unity_kit_exception.dart';

/// Exception thrown when Unity does not report ready within a time limit.
///
/// This is the typed form of the "blank Unity view" failure: the platform
/// view exists, but the player never sent `onUnityCreated`. The [message]
/// lists the usual causes so the log line is actionable on its own.
class EngineReadyTimeoutException extends UnityKitException {
  /// Creates a new [EngineReadyTimeoutException] for [timeout].
  EngineReadyTimeoutException({
    required this.timeout,
    super.cause,
    super.stackTrace,
  }) : super(message: describe(timeout));

  /// How long the caller waited before giving up.
  final Duration timeout;

  /// Builds the diagnostic message for a wait of [timeout].
  ///
  /// Also used by `UnityView` to log the same hint when its ready timer
  /// fires, so both paths point at the same checklist.
  static String describe(Duration timeout) =>
      'Unity did not report ready within ${timeout.inMilliseconds} ms. '
      'Check that the first scene has an active FlutterBridge GameObject, '
      'that the Unity export is part of the host build (unityLibrary on '
      'Android, UnityFramework.framework embedded on iOS), and the native '
      'log for a player start-up error.';

  @override
  String toString() => 'EngineReadyTimeoutException: $message';
}
