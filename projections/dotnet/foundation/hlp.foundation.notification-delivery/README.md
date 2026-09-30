# Harborline.Foundation.NotificationDelivery

Slice 1 of `hlp.foundation.notification-delivery`, the notification delivery substrate (DES-0055, T-527): the code-registered delivery channel registry (the on-node inbox is the sole channel), tenant channel bindings that hold secret references and never secret values, and the on-node inbox store with read-state transitions, unread counting and mark-all-read.

Routing, subscriptions, delivery attempts and external transports are later T-527 slices; delivery attempts register as an execution-runtime run kind. The package is local and distribution-blocked.
