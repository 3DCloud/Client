using System;
using System.Buffers;
using System.Text.Json;

namespace ActionCableSharp
{
    /// <summary>
    /// Contains the data of a received Action Cable message.
    /// </summary>
    public class ActionCableMessage
    {
        private readonly JsonElement message;
        private readonly JsonSerializerOptions? serializerOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionCableMessage"/> class.
        /// </summary>
        /// <param name="message">The <see cref="JsonElement"/> containing the serialized message data.</param>
        /// <param name="serializerOptions"><see cref="JsonSerializerOptions"/> to use when deserializing the data into a specific class.</param>
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
            return JsonSerializer.Deserialize<T>(this.GetMessageBytes(), this.serializerOptions);
        }

        /// <summary>
        /// Deserializes the message into an object of the type specified by <paramref name="returnType"/>.
        /// </summary>
        /// <param name="returnType">Type into which the message will be deserialized.</param>
        /// <returns>Message deserialized into an object of the type specified by <paramref name="returnType"/>.</returns>
        public object? AsObject(Type returnType)
        {
            return JsonSerializer.Deserialize(this.GetMessageBytes(), returnType, this.serializerOptions);
        }

        private ReadOnlySpan<byte> GetMessageBytes()
        {
            var bufferWriter = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                this.message.WriteTo(writer);
            }

            return bufferWriter.WrittenSpan;
        }
    }
}
