import 'dart:async';

import 'package:fake_async/fake_async.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:unity_kit/src/bridge/unity_bridge.dart';
import 'package:unity_kit/src/bridge/unity_bridge_readiness.dart';
import 'package:unity_kit/src/exceptions/exceptions.dart';
import 'package:unity_kit/src/models/models.dart';

/// Minimal bridge that only drives readiness, the way a host's own
/// implementation or test double would.
class _FakeReadinessBridge implements UnityBridge {
  final StreamController<UnityLifecycleState> _lifecycleController =
      StreamController<UnityLifecycleState>.broadcast();

  UnityLifecycleState _state = UnityLifecycleState.initializing;

  @override
  UnityLifecycleState get currentState => _state;

  @override
  bool get isReady => _state == UnityLifecycleState.ready;

  @override
  Stream<UnityLifecycleState> get lifecycleStream =>
      _lifecycleController.stream;

  void emit(UnityLifecycleState state) {
    _state = state;
    _lifecycleController.add(state);
  }

  Future<void> close() => _lifecycleController.close();

  @override
  Object? noSuchMethod(Invocation invocation) =>
      throw UnimplementedError(invocation.memberName.toString());
}

void main() {
  late _FakeReadinessBridge bridge;

  setUp(() {
    bridge = _FakeReadinessBridge();
  });

  group('UnityBridgeReadiness.waitUntilReady', () {
    test('completes immediately when already ready', () async {
      // Arrange
      bridge.emit(UnityLifecycleState.ready);

      // Act & Assert
      await expectLater(bridge.waitUntilReady(), completes);
    });

    test('completes when the bridge becomes ready', () async {
      // Arrange
      final wait = bridge.waitUntilReady();

      // Act
      bridge.emit(UnityLifecycleState.ready);

      // Assert
      await expectLater(wait, completes);
    });

    test('ignores intermediate states before ready', () async {
      // Arrange
      var completed = false;
      final wait = bridge.waitUntilReady().then((_) => completed = true);

      // Act
      bridge.emit(UnityLifecycleState.initializing);
      await Future<void>.delayed(Duration.zero);

      // Assert
      expect(completed, isFalse);
      bridge.emit(UnityLifecycleState.ready);
      await wait;
      expect(completed, isTrue);
    });

    test('throws EngineReadyTimeoutException after the timeout', () {
      fakeAsync((async) {
        // Arrange
        const timeout = Duration(seconds: 5);
        Object? caught;
        bridge.waitUntilReady(timeout: timeout).catchError((Object error) {
          caught = error;
        });

        // Act
        async.elapse(timeout + const Duration(milliseconds: 1));

        // Assert
        expect(caught, isA<EngineReadyTimeoutException>());
        final exception = caught! as EngineReadyTimeoutException;
        expect(exception.timeout, timeout);
        expect(exception.message, contains('FlutterBridge'));
      });
    });

    test('throws EngineNotReadyException when disposed first', () async {
      // Arrange
      final wait = bridge.waitUntilReady();

      // Act
      bridge.emit(UnityLifecycleState.disposed);

      // Assert
      await expectLater(wait, throwsA(isA<EngineNotReadyException>()));
    });

    test('throws EngineNotReadyException when already disposed', () async {
      // Arrange
      bridge.emit(UnityLifecycleState.disposed);

      // Act & Assert
      await expectLater(
        bridge.waitUntilReady(),
        throwsA(isA<EngineNotReadyException>()),
      );
    });

    test('throws EngineNotReadyException when the stream closes', () async {
      // Arrange
      final expectation = expectLater(
        bridge.waitUntilReady(),
        throwsA(isA<EngineNotReadyException>()),
      );

      // Act
      await bridge.close();

      // Assert
      await expectation;
    });

    test('rejects a non-positive timeout', () async {
      // Act & Assert
      await expectLater(
        bridge.waitUntilReady(timeout: Duration.zero),
        throwsA(isA<ArgumentError>()),
      );
    });
  });

  group('EngineReadyTimeoutException', () {
    test('is a UnityKitException with the timeout in its message', () {
      // Arrange
      final exception = EngineReadyTimeoutException(
        timeout: const Duration(seconds: 2),
      );

      // Assert
      expect(exception, isA<UnityKitException>());
      expect(exception.message, contains('2000 ms'));
      expect(
        exception.toString(),
        startsWith('EngineReadyTimeoutException: '),
      );
    });
  });
}
