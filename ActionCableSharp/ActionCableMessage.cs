using System;
using System.Buffers;
using System.Text.Json;

namespace ActionCableSharp
{
    public class ActionCableMessage
    {
        private readonly JsonElement message;
        private readonly JsonSerializerOptions? serializerOptions;

        internal ActionCableMessage(JsonElement message, JsonSerializerOptions? serializerOptions)
        {
            this.message = message;
            this.serializerOptions = serializerOptions;
        }

        /// <summary>
        /// Deserializes the message into an object of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type into which the message will be deserialized.</typeparam>
        /// <returns>Message deserialized into an object of type <typeparamref name="T"/>.</returns>
        public T? AsObject<T>()
        {
            return JsonSerializer.Deserialize<T>(GetMessageBytes(), serializerOptions);
        }

        /// <summary>
        /// Deserializes the message into an object of the type specified by <paramref name="returnType"/>.
        /// </summary>
        /// <param name="returnType">Type into which the message will be deserialized.</param>
        /// <returns>Message deserialized into an object of the type specified by <paramref name="returnType"/>.</returns>
        public object? AsObject(Type returnType)
        {
            return JsonSerializer.Deserialize(GetMessageBytes(), returnType, serializerOptions);
        }

        private ReadOnlySpan<byte> GetMessageBytes()
        {
            var bufferWriter = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                message.WriteTo(writer);
            }

            return bufferWriter.WrittenSpan;
        }
    }
}
