# ActionCableSharp
Action Cable v1 library for .NET 5.

## The Action Cable v1 Protocol
All exchanges are expected to be made via JSON-encoded objects. Note that ActionCableSharp wraps all of this behavior; you will not have to manipulate JSON strings when interacting with the library.

### Connecting
The WebSocket endpoint is specified in Rails' configuration through `config.action_cable.mount_path`. An origin listed in `config.action_cable.allowed_request_origins` must be specified when making the request to the WebSocket; it doesn't have to be a URL, it can be any string.

Once the connection is initiated, the server sends the following welcome message to the client, indicating the server has successfully received the connection:

```json
{ "type": "welcome" }
```

### Channels and Subscriptions
Action Cable adds the concept of channels and subscriptions on top of the standard WebSocket interface. Channels are defined on the server-side and the client can connect to any number of them. Furthermore, each client can subscribe more than once to each of these channels using an identifier to differentiate each subscription.

The server will ignore any messages that aren't a valid command or a message to a valid channel subscription.

A subscription request is initiated by sending the following message to the server:

```json
{
    "command": "subscribe",
    "identifier": "{\"channel_name\": \"ChannelName\"}"
}
```

`identifier` is a JSON-encoded object with at least a `channel_name` equal to a channel class name on the server side. It can contain additional fields if wanted that can be accessed on the server's side via the `params` attribute.

The server can then accept or reject the subscription. If the subscription is accepted, the server will send the following message:

```json
{
    "type": "confirm_subscription",
    "identifier": "{\"channel_name\": \"ChannelName\"}"
}
```

If it is rejected, the server will reply with this:
```json
{
    "type": "reject_subscription",
    "identifier": "{\"channel_name\": \"ChannelName\"}"
}
```

The `identifier` is the same as the one used when requesting the subscription. If the subscription is confirmed, the client can start sending messages to the server using the following format:

```json
{
    "command": "message",
    "identifier": "{\"channel_name\": \"ChannelName\"}",
    "data": "{\"action\": \"method_name\"}"
}
```

`data` is a JSON-encoded object that contains at least an `action` key with a value that is the name of a method in the specified `channel_name` class. `data` can contain additional fields that can be accessed through the first parameter of the called method.

### Ending the connection
The connection can be terminated gracefully from either the client or the server. Nothing special is necessary on the client's end when closing a connection; simply closing the WebSocket is enough. However, if the server decides to close the connection, it will first send the following message to the client:

```json
{
    "type": "disconnect",
    "reason": "some reason string",
    "reconnect": false
}
```

`reason` and `reconnect` are objets that can both be specified when calling the `close` method in Rails. If `reconnect` is `true`, the client should attempt to reconnect to the server after being disconnected. Once this message is sent, the server immediately sends a WebSocket Close message to indicate the client should also close it's end of the socket.

### Pings
The server will periodically ping the client to make sure the connection is alive. These messages have the following format:

```json
{
    "type": "ping",
    "timestamp": 39085681034
}
```

`timestamp` is the UNIX timestamp at which the message was sent.