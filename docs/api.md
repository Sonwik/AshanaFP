# API

The Blazor pages call the application services directly on the server, so the web UI does not need an HTTP API.
Ashana still exposes a small JSON API and a SignalR hub for clients outside Blazor: the TV screen at the counter,
monitoring, and a future mobile app. Code: [`ApiEndpoints.cs`](../src/Ashana.Web/Endpoints/ApiEndpoints.cs) and
[`OrderHub.cs`](../src/Ashana.Web/Realtime/OrderHub.cs).

Conventions:

* JSON with camelCase names; enums are strings (`"Cooking"`), times are ISO 8601 in canteen time (`+05:00`).
* Errors follow RFC 7807 (`application/problem+json`) with a stable `code`, for example `order.not_found`.
* Every `/api` route is rate limited per IP address (120 requests a minute by default; HTTP 429 when exceeded).
* Protected endpoints use the sign-in cookie of the web app. Without it they answer `401`; with the wrong role,
  `403`. They never redirect to the sign-in page.

## Endpoints

| Method and path | Who | Description |
|---|---|---|
| `GET /api/menu` | anyone | Categories and dishes with today's stock. Cached for 30 s, refreshed at once when the menu or stock changes. |
| `GET /api/slots` | anyone | Pickup slots of the current service day with their load. Cached for 10 s, refreshed on every order. |
| `GET /api/board` | anyone | Numbers being prepared and ready for pickup (what the TV shows). |
| `GET /api/orders/mine` | signed in | The caller's latest 30 orders. |
| `GET /api/orders/{id}` | signed in | One order. Students can read only their own orders (`403` otherwise); cooks and admins can read any. |
| `GET /api/admin/orders.csv?from=2026-10-01&to=2026-10-30` | Admin | Orders of a period (at most 62 days) as CSV with a UTF-8 BOM, so Excel shows `₸` and Cyrillic correctly. |
| `GET /health` | anyone | `Healthy` when the data store answers (for uptime monitoring). |

### `GET /api/menu`

```json
{
  "categories": [ { "id": "a47292e3-…", "name": "Mains", "icon": "🍛", "sortOrder": 1 } ],
  "items": [
    {
      "id": "ed4081d3-…", "categoryId": "3483c268-…",
      "name": "Achichuk", "description": "Tomato and onion salad, the classic partner of plov.",
      "price": 500, "emoji": "🍅", "imageUrl": null, "calories": 70,
      "tags": "Vegetarian, GlutenFree", "prepMinutes": 2,
      "dailyStock": 50, "remainingStock": 50,
      "isAvailable": true, "isArchived": false, "isOrderable": true, "isLowStock": false,
      "version": 0
    }
  ],
  "loadedAt": "2026-10-30T13:45:55+05:00"
}
```

### `GET /api/slots`

```json
{
  "days": [
    {
      "date": "2026-10-30", "title": "Today", "hasBookableSlots": true,
      "slots": [
        { "start": "2026-10-30T13:45:00+05:00", "end": "2026-10-30T14:00:00+05:00", "label": "13:45–14:00",
          "booked": 0, "capacity": 20, "remaining": 20, "loadPercent": 0,
          "state": "TooSoon", "isBookable": false }
      ]
    }
  ],
  "acceptingOrders": true,
  "pauseMessage": null,
  "firstBookable": { "start": "2026-10-30T14:00:00+05:00", "label": "14:00–14:15", "state": "Available", "…": "…" }
}
```

`state` is one of `Available`, `Busy` (75 % full or more), `Full` and `TooSoon` (starts before the kitchen could
cook it). After closing time the endpoint returns tomorrow's slots.

### `GET /api/board`

```json
{
  "preparing": [ { "number": 103, "status": "Cooking", "slotLabel": "14:00–14:15" } ],
  "ready":     [ { "number": 101, "status": "Ready",   "slotLabel": "14:00–14:15" } ],
  "generatedAt": "2026-10-30T13:45:55+05:00"
}
```

No names or order contents: the board is shown on a public screen.

### Errors

```http
GET /api/orders/6f1c… HTTP/1.1

HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Order not found.", "status": 404, "code": "order.not_found" }
```

| `Error.Kind` in the domain | HTTP status |
|---|---|
| Validation | 400 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict (sold out, slot full, edited by someone else) | 409 |
| Failure (other business rule) | 422 |

## SignalR hub `/hubs/orders`

A strongly typed hub (`Hub<IOrderHubClient>`). Transports in order of preference: WebSockets, Server-Sent Events,
long polling. Anonymous clients may connect; signed-in clients are added to extra groups automatically.

| Direction | Name | Payload | Who receives it |
|---|---|---|---|
| client → server | `JoinBoard()` | returns the current board snapshot | anyone; joins the `board` group |
| server → client | `BoardUpdated(snapshot)` | same shape as `GET /api/board` | `board` group (TV screens), at most every 250 ms |
| server → client | `OrderUpdated(order)` | full order | `kitchen` group (cooks and admins) and the order's owner (`user:{id}` group) |

Minimal JavaScript client (this is what [`board.js`](../src/Ashana.Web/wwwroot/js/board.js) does):

```js
const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/orders")
  .withAutomaticReconnect()
  .build();

connection.on("BoardUpdated", snapshot => render(snapshot));
await connection.start();
render(await connection.invoke("JoinBoard"));
```

The integration test [`RealtimeTests`](../tests/Ashana.IntegrationTests/RealtimeTests.cs) talks to the hub with a
raw WebSocket client ([`HubClient.cs`](../tests/Ashana.IntegrationTests/Support/HubClient.cs)), which shows the
protocol step by step: negotiate, handshake, invoke, receive.
