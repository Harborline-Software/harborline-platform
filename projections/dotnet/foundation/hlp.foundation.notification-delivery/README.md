# Harborline.Foundation.NotificationDelivery

Slice 1 of `hlp.foundation.notification-delivery`, the notification delivery substrate (DES-0055, T-527): the code-registered delivery channel registry (the on-node inbox is the sole channel), tenant channel bindings that hold secret references and never secret values, and the on-node inbox store with read-state transitions, unread counting and mark-all-read.

Routing, subscriptions, delivery attempts and external transports are later T-527 slices; delivery attempts register as an execution-runtime run kind. The package is local and distribution-blocked.

Interface revision 2 adds the required outcome classifier on `DeliveryChannel`. `Classify` returns
one of `Accepted`, `Delivered`, `Transient`, `Permanent` beside the transport diagnostic unchanged.
The inbox maps a committed write to `Delivered` and a failed write to `Permanent`; it has no retry
profile. No external transport mapping is defined. `ChannelContentClass` is unchanged.

`IInboxStore.DismissAsync(tenant, recipient, entryId)` sets that one delivery to `Dismissed`, so it
no longer contributes to unread count. A missing entry or a mismatched tenant or recipient refuses
`notification_delivery.inbox_entry_unknown`. Repeating dismissal is idempotent. List retains the
entry and its dismissed state; retention and surface filtering remain separate concerns.
