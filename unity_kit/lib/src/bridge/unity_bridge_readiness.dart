import 'dart:async';

import '../exceptions/exceptions.dart';
import '../models/models.dart';
import 'unity_bridge.dart';

/// Waits for a [UnityBridge] to become ready, with a time limit.
///
/// An extension rather than a [UnityBridge] member, so it works on every
/// bridge, including custom implementations and test doubles, without
/// adding anything they would have to implement.
///
/// Example:
/// ```dart
/// try {
///   await bridge.waitUntilReady(timeout: const Duration(seconds: 15));
///   await bridge.send(UnityMessage.command('StartGame'));
/// } on EngineReadyTimeoutException catch (e) {
///   debugPrint(e.message); // lists the usual blank-view causes
/// }
/// ```
extension UnityBridgeReadiness on UnityBridge {
  /// Default limit for [waitUntilReady].
  ///
  /// Generous on purpose: a cold Unity start on a low-end Android device
  /// can take well over ten seconds.
  static const Duration defaultReadyTimeout = Duration(seconds: 30);

  /// Completes once the bridge reports [UnityLifecycleState.ready].
  ///
  /// Completes immediately when [UnityBridge.isReady] is already true.
  ///
  /// Throws [EngineReadyTimeoutException] when Unity is not ready within
  /// [timeout], [EngineNotReadyException] when the bridge is disposed
  /// first, and [ArgumentError] when [timeout] is not positive.
  Future<void> waitUntilReady({
    Duration timeout = defaultReadyTimeout,
  }) async {
    if (timeout <= Duration.zero) {
      throw ArgumentError.value(timeout, 'timeout', 'must be positive');
    }
    if (isReady) return;
    if (currentState == UnityLifecycleState.disposed) {
      throw const EngineNotReadyException();
    }

    final UnityLifecycleState state;
    try {
      state = await lifecycleStream
          .firstWhere(
            (candidate) =>
                candidate == UnityLifecycleState.ready ||
                candidate == UnityLifecycleState.disposed,
          )
          .timeout(timeout);
    } on TimeoutException catch (error, stackTrace) {
      throw EngineReadyTimeoutException(
        timeout: timeout,
        cause: error,
        stackTrace: stackTrace,
      );
    } on StateError catch (error, stackTrace) {
      // The lifecycle stream closed, i.e. the bridge was disposed.
      throw EngineNotReadyException(cause: error, stackTrace: stackTrace);
    }

    if (state == UnityLifecycleState.disposed) {
      throw const EngineNotReadyException();
    }
  }
}
