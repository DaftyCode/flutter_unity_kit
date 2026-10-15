import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:unity_kit/src/bridge/unity_binary_codec.dart';
import 'package:unity_kit/src/exceptions/exceptions.dart';
import 'package:unity_kit/src/models/unity_message.dart';

void main() {
  group('UnityBinaryCodec', () {
    test('round-trips a message with data', () {
      final original = UnityMessage.command('Move', {'x': 1.5, 'y': -2});
      final bytes = UnityBinaryCodec.encode(original);
      final decoded = UnityBinaryCodec.decode(bytes);

      expect(decoded.type, 'Move');
      expect(decoded.data, {'x': 1.5, 'y': -2});
    });

    test('round-trips a message without data', () {
      final original = UnityMessage.command('Ping');
      final decoded =
          UnityBinaryCodec.decode(UnityBinaryCodec.encode(original));

      expect(decoded.type, 'Ping');
      expect(decoded.data, isNull);
    });

    test('handles unicode type and payload', () {
      final original = UnityMessage.command('Zażółć', {'msg': 'gęślą jaźń'});
      final decoded =
          UnityBinaryCodec.decode(UnityBinaryCodec.encode(original));

      expect(decoded.type, 'Zażółć');
      expect(decoded.data, {'msg': 'gęślą jaźń'});
    });

    test('emits a valid frame header', () {
      final bytes = UnityBinaryCodec.encode(UnityMessage.command('X'));

      expect(bytes[0], UnityBinaryCodec.magic0);
      expect(bytes[1], UnityBinaryCodec.magic1);
      expect(bytes[2], UnityBinaryCodec.version);
      expect(UnityBinaryCodec.isBinaryFrame(bytes), isTrue);
    });

    test('isBinaryFrame rejects foreign bytes', () {
      expect(
        UnityBinaryCodec.isBinaryFrame(Uint8List.fromList([1, 2, 3])),
        isFalse,
      );
    });

    test('produces the cross-language golden frame for "Hi"', () {
      // This exact byte sequence is asserted by the Unity-side
      // UnityKitBinaryCodecTests.DecodeBytes_GoldenFrame test, locking the
      // wire format across Dart and C#.
      final bytes = UnityBinaryCodec.encode(UnityMessage.command('Hi'));

      expect(
        bytes,
        equals(
            [0x55, 0x4B, 0x01, 0x00, 0x02, 0x48, 0x69, 0x00, 0x00, 0x00, 0x00]),
      );
    });

    test('decode throws on short buffer', () {
      expect(
        () => UnityBinaryCodec.decode(Uint8List.fromList([0x55, 0x4B])),
        throwsA(isA<BridgeException>()),
      );
    });

    test('decode throws on bad magic', () {
      final bytes = UnityBinaryCodec.encode(UnityMessage.command('X'));
      bytes[0] = 0x00;
      expect(
        () => UnityBinaryCodec.decode(bytes),
        throwsA(isA<BridgeException>()),
      );
    });

    test('decode throws on unsupported version', () {
      final bytes = UnityBinaryCodec.encode(UnityMessage.command('X'));
      bytes[2] = 99;
      expect(
        () => UnityBinaryCodec.decode(bytes),
        throwsA(isA<BridgeException>()),
      );
    });
  });

  group('UnityBinaryWriter / UnityBinaryReader', () {
    test('round-trips typed primitives in order', () {
      final bytes = (UnityBinaryWriter()
            ..writeString('player')
            ..writeInt32(-42)
            ..writeInt64(9007199254740991)
            ..writeFloat64(3.14159)
            ..writeBool(true))
          .takeBytes();

      final reader = UnityBinaryReader(bytes);
      expect(reader.readString(), 'player');
      expect(reader.readInt32(), -42);
      expect(reader.readInt64(), 9007199254740991);
      expect(reader.readFloat64(), closeTo(3.14159, 1e-9));
      expect(reader.readBool(), isTrue);
      expect(reader.hasMore, isFalse);
    });

    test('reader throws when reading past the end', () {
      final bytes = (UnityBinaryWriter()..writeInt32(1)).takeBytes();
      final reader = UnityBinaryReader(bytes)..readInt32();

      expect(reader.readInt32, throwsA(isA<BridgeException>()));
    });

    test('reader throws when a string length exceeds the buffer', () {
      // Arrange
      final bytes = (UnityBinaryWriter()
            ..writeInt32(10)
            ..writeBool(true))
          .takeBytes();
      final reader = UnityBinaryReader(bytes);

      // Act & Assert
      expect(reader.readString, throwsA(isA<BridgeException>()));
    });

    test('reader treats the string length prefix as unsigned', () {
      // Arrange: 0xFFFFFFFF is -1 when read signed, 4294967295 unsigned.
      final bytes = Uint8List.fromList([0xFF, 0xFF, 0xFF, 0xFF, 0x41]);
      final reader = UnityBinaryReader(bytes);

      // Act & Assert
      expect(reader.readString, throwsA(isA<BridgeException>()));
    });

    test('reader decodes a string written into a sub-view of a buffer', () {
      // Arrange
      final payload = (UnityBinaryWriter()..writeString('héllo')).takeBytes();
      final backing = Uint8List(payload.length + 3)
        ..setRange(3, 3 + payload.length, payload);
      final view = Uint8List.sublistView(backing, 3);

      // Act
      final value = UnityBinaryReader(view).readString();

      // Assert
      expect(value, 'héllo');
    });
  });
}
